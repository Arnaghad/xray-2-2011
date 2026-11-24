#include <algorithm>
#include <cstring>
#include <limits>
#include <cmath>
#include "vulkan_backend.h"
#include "vulkan_diagnostics.h"
#include "vulkan_creation.h"
#include "spirv_entry.h"
#include "material_contract.h"

namespace xray { namespace render { namespace vulkan {
namespace {
void check_result(VkResult result, char const* name) {
    if (result != VK_SUCCESS) throw std::runtime_error(std::string(name) + " (VkResult=" + std::to_string(result) + ")");
}
}

u64 vulkan_backend::retirement_serial() const {
    // Recording commands can reference resources before their submit exists.
    return m_submitted_serial + (m_recording ? 1 : 0);
}
void vulkan_backend::collect_retired(bool force) noexcept {
    (void)force;
    for (auto it = m_retired_textures.begin(); it != m_retired_textures.end();) {
        if (!force && it->serial > m_completed_serial) { ++it; continue; }
        if (!force && it->bindless_slot > 0)
            (it->bindless_cube ? m_free_bindless_cube : m_free_bindless_2d).push_back(it->bindless_slot);
        if (it->resource.sample_view) vkDestroyImageView(m_device, it->resource.sample_view, allocation_callbacks());
        vkDestroyImageView(m_device, it->resource.view, allocation_callbacks());
        vkDestroyImage(m_device, it->resource.image, allocation_callbacks());
        free_memory(it->resource.memory);
        it = m_retired_textures.erase(it);
    }
    for (auto it = m_retired_samplers.begin(); it != m_retired_samplers.end();) {
        if (!force && it->serial > m_completed_serial) { ++it; continue; }
        vkDestroySampler(m_device, it->sampler, allocation_callbacks());
        it = m_retired_samplers.erase(it);
    }
    for (auto it = m_retired_pipelines.begin(); it != m_retired_pipelines.end();) {
        if (!force && it->serial > m_completed_serial) { ++it; continue; }
        vkDestroyPipeline(m_device, it->pipeline, allocation_callbacks());
        it = m_retired_pipelines.erase(it);
    }
    for (auto it = m_retired_shaders.begin(); it != m_retired_shaders.end();) {
        if (!force && it->serial > m_completed_serial) { ++it; continue; }
        vkDestroyShaderModule(m_device, it->module, allocation_callbacks());
        it = m_retired_shaders.erase(it);
    }
    for (auto it = m_retired_buffers.begin(); it != m_retired_buffers.end();) {
        if (!force && it->serial > m_completed_serial) { ++it; continue; }
        free_buffer(it->resource);
        it = m_retired_buffers.erase(it);
    }
}
rhi::rhi_buffer_handle vulkan_backend::create_buffer(rhi::buffer_type type, size_t size, void const* data) {
    if (!m_initialized || !size || (type != rhi::buffer_type::vertex && type != rhi::buffer_type::index && type != rhi::buffer_type::constant && type != rhi::buffer_type::structured)) {
        diagnostic("error", "Buffers require an initialized device, nonzero size and valid type");
        return RHI_INVALID_HANDLE;
    }
    vulkan_buffer_entry resource;
    resource.size = size; resource.type = type;
    try {
        if (type == rhi::buffer_type::constant) {
            if (size > 65536) throw std::runtime_error("Constant buffer exceeds 64 KiB");
            resource.index_data.resize(size);
            if (data) std::memcpy(resource.index_data.data(), data, size);
            return m_buffers.allocate(resource);
        }
        VkBufferCreateInfo info = {};
        info.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        info.size = size;
        info.usage = type == rhi::buffer_type::vertex ? VK_BUFFER_USAGE_VERTEX_BUFFER_BIT
            : type == rhi::buffer_type::index ? VK_BUFFER_USAGE_INDEX_BUFFER_BIT
            : (VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_VERTEX_BUFFER_BIT);
        info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        check_result(create_handle(resource.buffer, vkCreateBuffer, m_device, &info, allocation_callbacks()), "Create buffer");
        VkMemoryRequirements requirements;
        vkGetBufferMemoryRequirements(m_device, resource.buffer, &requirements);
        VkPhysicalDeviceMemoryProperties properties;
        vkGetPhysicalDeviceMemoryProperties(m_physical_device, &properties);
        uint32_t memory_type = UINT32_MAX;
        for (uint32_t i = 0; i < properties.memoryTypeCount; ++i) {
            if (!(requirements.memoryTypeBits & (1u << i))) continue;
            auto flags = properties.memoryTypes[i].propertyFlags;
            if (!(flags & VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT)) continue;
            memory_type = i;
            if (flags & VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) break;
        }
        if (memory_type == UINT32_MAX) throw std::runtime_error("No host-visible buffer memory type");
        VkMemoryAllocateInfo allocation = {};
        allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocation.allocationSize = requirements.size;
        allocation.memoryTypeIndex = memory_type;
        check_result(allocate_memory(allocation, resource.memory), "Allocate buffer memory");
        check_result(vkBindBufferMemory(m_device, resource.buffer, resource.memory, 0), "Bind buffer memory");
        check_result(vkMapMemory(m_device, resource.memory, 0, VK_WHOLE_SIZE, 0, &resource.mapped), "Map buffer memory");
        if (data) std::memcpy(resource.mapped, data, size);
        else std::memset(resource.mapped, 0, size);
        if (type == rhi::buffer_type::index) {
            auto* bytes = static_cast<unsigned char*>(resource.mapped);
            resource.index_data.assign(bytes, bytes + size);
        }
        if (!(properties.memoryTypes[memory_type].propertyFlags & VK_MEMORY_PROPERTY_HOST_COHERENT_BIT)) {
            VkMappedMemoryRange range = {};
            range.sType = VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
            range.memory = resource.memory; range.size = VK_WHOLE_SIZE;
            check_result(vkFlushMappedMemoryRanges(m_device, 1, &range), "Flush immutable buffer upload");
        }
        vkUnmapMemory(m_device, resource.memory);
        resource.mapped = nullptr;
        // Immutable host writes are available to GPU reads at queue submission.
        // No transfer command/staging buffer is needed for this host-visible path.
        return m_buffers.allocate(resource);
    } catch (std::exception const& error) {
        free_buffer(resource);
        diagnostic("error", "Buffer creation failed: %s", error.what());
        return RHI_INVALID_HANDLE;
    }
}
void vulkan_backend::destroy_buffer(rhi::rhi_buffer_handle handle) {
    auto* resource = m_buffers.get(handle);
    if (!resource) return;
    m_retired_buffers.push_back({retirement_serial(), *resource});
    m_buffers.release(handle);
    *resource = {};
    if (m_bound_vb == handle) m_bound_vb = RHI_INVALID_HANDLE;
    if (m_bound_ib == handle) m_bound_ib = RHI_INVALID_HANDLE;
    for (auto& bound : m_bound_constants) if (bound == handle) bound = 0;
    collect_retired();
}
rhi::rhi_shader_handle vulkan_backend::create_shader(rhi::shader_stage stage, void const* bytecode, size_t size, char const* entry) {
    if (!m_initialized || !bytecode || size < 20 || size % sizeof(u32)) {
        diagnostic("error", "Shader requires an initialized device and aligned SPIR-V byte count");
        return RHI_INVALID_HANDLE;
    }
    vulkan_shader_entry resource;
    try {
        std::vector<u32> words(size / sizeof(u32));
        std::memcpy(words.data(), bytecode, size);
        resource.descriptor_mask = reflect_material_contract(words, stage, entry, m_shader_demote_enabled, m_sample_shading_enabled, m_bindless_enabled);
        resource.stage = stage; resource.entry_point = entry;
        VkShaderModuleCreateInfo info = {};
        info.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        info.codeSize = size; info.pCode = words.data();
        check_result(create_handle(resource.module, vkCreateShaderModule, m_device, &info, allocation_callbacks()), "Create shader module");
        return m_shaders.allocate(resource);
    } catch (std::exception const& error) {
        if (resource.module) vkDestroyShaderModule(m_device, resource.module, allocation_callbacks());
        diagnostic("error", "Shader creation failed: %s", error.what());
        return RHI_INVALID_HANDLE;
    }
}
void vulkan_backend::destroy_shader(rhi::rhi_shader_handle handle) {
    auto* resource = m_shaders.get(handle);
    if (!resource) return;
    auto const serial = retirement_serial();
    // Remove lookup entries now, but retain recorded/pending GPU pipelines.
    for (auto it = m_pipeline_map.begin(); it != m_pipeline_map.end();) {
        auto const& key = it->first;
        if (key.vs_shader != handle && key.ps_shader != handle && key.gs_shader != handle) { ++it; continue; }
        m_retired_pipelines.push_back({serial, it->second});
        it = m_pipeline_map.erase(it);
    }
    if (auto it = m_compute_pipelines.find(handle); it != m_compute_pipelines.end()) {
        m_retired_pipelines.push_back({serial, it->second});
        m_compute_pipelines.erase(it);
    }
    m_retired_shaders.push_back({serial, resource->module});
    m_shaders.release(handle);
    *resource = {};
    if (m_current_state.vs_shader == handle) m_current_state.vs_shader = 0;
    if (m_current_state.ps_shader == handle) m_current_state.ps_shader = 0;
    collect_retired();
}
void vulkan_backend::bind_vertex_buffer(rhi::rhi_buffer_handle handle, u32 offset) {
    auto* buffer = m_buffers.get(handle);
    if (!m_recording || !buffer || (buffer->type != rhi::buffer_type::vertex && buffer->type != rhi::buffer_type::structured) || offset >= buffer->size || offset % 4)
        throw std::runtime_error("Invalid vertex buffer binding (state/handle/type/offset)");
    VkDeviceSize position = offset;
    vkCmdBindVertexBuffers(m_frames->command_buffers[m_frames->current_frame], 0, 1, &buffer->buffer, &position);
    m_bound_vb = handle; m_bound_vb_offset = offset;
}
void vulkan_backend::bind_index_buffer(rhi::rhi_buffer_handle handle, u32 offset, rhi::index_format type) {
    if (type != rhi::index_format::uint16 && type != rhi::index_format::uint32) throw std::runtime_error("Unknown index format");
    auto* buffer = m_buffers.get(handle);
    u32 const size = type == rhi::index_format::uint16 ? 2 : 4;
    if (!m_recording || !buffer || buffer->type != rhi::buffer_type::index || offset >= buffer->size || offset % size)
        throw std::runtime_error("Invalid index buffer binding (state/handle/type/offset)");
    vkCmdBindIndexBuffer(m_frames->command_buffers[m_frames->current_frame], buffer->buffer, offset,
        type == rhi::index_format::uint16 ? VK_INDEX_TYPE_UINT16 : VK_INDEX_TYPE_UINT32);
    m_bound_ib = handle; m_bound_ib_offset = offset; m_index_format = type;
}
void vulkan_backend::bind_shader(rhi::shader_stage stage, rhi::rhi_shader_handle handle) {
    auto const* shader = m_shaders.get(handle);
    if (!shader || shader->stage != stage) throw std::runtime_error("Invalid/stale shader handle or stage mismatch");
    if (stage == rhi::shader_stage::vertex) m_current_state.vs_shader = handle;
    else if (stage == rhi::shader_stage::pixel) m_current_state.ps_shader = handle;
    else throw std::runtime_error("Stage 2 graphics supports only vertex/pixel shaders");
}
void vulkan_backend::set_depth_bias(float constant,float slope) {
    if (!std::isfinite(constant) || !std::isfinite(slope) || std::abs(constant)>1e6f || std::abs(slope)>16)
        throw std::runtime_error("Depth bias requires finite constant +/-1e6 and slope +/-16");
    m_depth_bias_constant=constant; m_depth_bias_slope=slope;
    m_current_state.depth_bias_enable=constant!=0 || slope!=0;
}
void vulkan_backend::bind_depth_bias() {
    if (m_current_state.depth_write && !m_presentation_pass && m_pass_description.depth.texture) {
        if (auto* depth = m_textures.get(m_pass_description.depth.texture); depth && depth->state == rhi::resource_state::depth_read)
            throw std::runtime_error("Cannot write to a read-only depth attachment");
    }
    if (m_current_state.depth_bias_enable && (!m_current_state.depth_enable || !m_pass_depth_format))
        throw std::runtime_error("Depth bias requires an active depth attachment and depth test");
    vkCmdSetDepthBias(m_frames->command_buffers[m_frames->current_frame],m_depth_bias_constant,0,m_depth_bias_slope);
}
void vulkan_backend::draw(u32 count, u32 first) {
    if (!m_recording || !m_pass_active) throw std::runtime_error("Draw outside render pass");
    if (!count) return;
    auto const& layout = m_current_state.vertex_layout;
    if (!layout.attributes.empty()) {
        auto* vertex = m_buffers.get(m_bound_vb);
        u64 const required = (u64(first) + count) * layout.stride;
        if (!vertex || required > vertex->size - m_bound_vb_offset) throw std::runtime_error("Vertex draw exceeds bound buffer");
    }
    bind_material_descriptors();
    bind_depth_bias();
    auto pipeline = get_or_create_pipeline(m_current_state);
    vkCmdBindPipeline(m_frames->command_buffers[m_frames->current_frame], VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline);
    vkCmdDraw(m_frames->command_buffers[m_frames->current_frame], count, 1, first, 0);
}
void vulkan_backend::draw_indexed(u32 count, u32 first, u32 base) {
    if (!m_recording || !m_pass_active) throw std::runtime_error("Indexed draw outside render pass");
    if (!count) return;
    auto* indices = m_buffers.get(m_bound_ib);
    auto* vertices = m_buffers.get(m_bound_vb);
    auto const& layout = m_current_state.vertex_layout;
    u32 const size = m_index_format == rhi::index_format::uint16 ? 2 : 4;
    u64 const begin = u64(m_bound_ib_offset) + u64(first) * size;
    if (!indices || !vertices || layout.attributes.empty() || !layout.stride || base > INT32_MAX
        || begin + u64(count) * size > indices->size) throw std::runtime_error("Invalid indexed draw range");
    for (u32 i = 0; i < count; ++i) {
        u32 index = 0;
        std::memcpy(&index, indices->index_data.data() + begin + u64(i) * size, size);
        if ((u64(index) + base + 1) * layout.stride > vertices->size - m_bound_vb_offset)
            throw std::runtime_error("Index references a vertex outside the bound buffer");
    }
    bind_material_descriptors();
    bind_depth_bias();
    auto pipeline = get_or_create_pipeline(m_current_state);
    vkCmdBindPipeline(m_frames->command_buffers[m_frames->current_frame], VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline);
    vkCmdDrawIndexed(m_frames->command_buffers[m_frames->current_frame], count, 1, first, static_cast<int32_t>(base), 0);
}
VkPipeline vulkan_backend::get_or_create_compute_pipeline(rhi::rhi_shader_handle shader_handle) {
    if (auto it = m_compute_pipelines.find(shader_handle); it != m_compute_pipelines.end())
        return it->second;
    auto* cs = m_shaders.get(shader_handle);
    if (!cs || cs->stage != rhi::shader_stage::compute)
        throw std::runtime_error("Compute dispatch requires a live compute shader");
    VkComputePipelineCreateInfo info = {};
    info.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO;
    info.stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    info.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT;
    info.stage.module = cs->module;
    info.stage.pName = cs->entry_point.c_str();
    info.layout = m_compute_pipeline_layout;
    VkPipeline pipeline = VK_NULL_HANDLE;
    check_result(vkCreateComputePipelines(m_device, m_pipeline_cache, 1, &info, allocation_callbacks(), &pipeline), "Create compute pipeline");
    m_compute_pipelines.emplace(shader_handle, pipeline);
    return pipeline;
}
void vulkan_backend::dispatch_compute_buffers(rhi::rhi_shader_handle shader_handle, rhi::rhi_buffer_handle const* storage_buffers, u32 storage_count, rhi::rhi_buffer_handle constant_buffer, u32 group_x, u32 group_y, u32 group_z) {
    if (!m_recording || m_pass_active) throw std::runtime_error("Compute dispatch must occur outside render pass");
    if (!group_x || !group_y || !group_z) return;
    if (!storage_buffers || storage_count != 2 || !constant_buffer)
        throw std::runtime_error("Compute dispatch expects two storage buffers and one constant buffer");
    auto* b0 = m_buffers.get(storage_buffers[0]);
    auto* b1 = m_buffers.get(storage_buffers[1]);
    auto* cb = m_buffers.get(constant_buffer);
    if (!b0 || !b1 || !cb || b0->type != rhi::buffer_type::structured || b1->type != rhi::buffer_type::structured || cb->type != rhi::buffer_type::constant)
        throw std::runtime_error("Compute dispatch buffer handles or types invalid");
    VkPipeline pipeline = get_or_create_compute_pipeline(shader_handle);
    auto& frame = m_frames->materials[m_frames->current_frame];
    VkDescriptorSet set = VK_NULL_HANDLE;
    if (frame.compute_descriptor_cursor < frame.compute_descriptors.size()) {
        set = frame.compute_descriptors[frame.compute_descriptor_cursor];
    } else {
        if (frame.compute_descriptors.size() == frame.compute_descriptors.capacity())
            frame.compute_descriptors.reserve(frame.compute_descriptors.size() + 32);
        for (;;) {
            bool fresh = frame.pool_cursor == frame.pools.size();
            if (fresh) {
                VkDescriptorPoolSize sizes[4] = {
                    {VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 128},
                    {VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 512},
                    {VK_DESCRIPTOR_TYPE_SAMPLER, 128},
                    {VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 128}};
                VkDescriptorPoolCreateInfo pool_info = {};
                pool_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
                pool_info.maxSets = 192;
                pool_info.poolSizeCount = 4;
                pool_info.pPoolSizes = sizes;
                VkDescriptorPool pool = VK_NULL_HANDLE;
                check_result(create_handle(pool, vkCreateDescriptorPool, m_device, &pool_info, allocation_callbacks()), "Create compute descriptor pool");
                try { frame.pools.push_back(pool); } catch (...) { vkDestroyDescriptorPool(m_device, pool, allocation_callbacks()); throw; }
            }
            VkDescriptorSetAllocateInfo alloc_info = {};
            alloc_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
            alloc_info.descriptorPool = frame.pools[frame.pool_cursor];
            alloc_info.descriptorSetCount = 1;
            alloc_info.pSetLayouts = &m_compute_descriptor_layout;
            VkResult alloc_res = vkAllocateDescriptorSets(m_device, &alloc_info, &set);
            if (alloc_res == VK_ERROR_OUT_OF_POOL_MEMORY || alloc_res == VK_ERROR_FRAGMENTED_POOL) {
                if (fresh) throw std::runtime_error("Fresh compute descriptor pool cannot hold one dispatch");
                ++frame.pool_cursor;
                continue;
            }
            check_result(alloc_res, "Allocate compute descriptor set");
            frame.compute_descriptors.push_back(set);
            break;
        }
    }
    ++frame.compute_descriptor_cursor;
    VkDescriptorBufferInfo storage0_info = {b0->buffer, 0, b0->size};
    VkDescriptorBufferInfo storage1_info = {b1->buffer, 0, b1->size};
    VkDescriptorBufferInfo cb_info = uniform_slice(cb->index_data.data(), cb->size);
    VkWriteDescriptorSet writes[3] = {};
    for (u32 w = 0; w < 3; ++w) {
        writes[w].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        writes[w].dstSet = set;
        writes[w].dstBinding = w;
        writes[w].descriptorCount = 1;
        writes[w].descriptorType = w < 2 ? VK_DESCRIPTOR_TYPE_STORAGE_BUFFER : VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    }
    writes[0].pBufferInfo = &storage0_info;
    writes[1].pBufferInfo = &storage1_info;
    writes[2].pBufferInfo = &cb_info;
    vkUpdateDescriptorSets(m_device, 3, writes, 0, nullptr);
    VkCommandBuffer cmd = m_frames->command_buffers[m_frames->current_frame];
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipeline);
    vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, m_compute_pipeline_layout, 0, 1, &set, 0, nullptr);
    vkCmdDispatch(cmd, group_x, group_y, group_z);
}
void vulkan_backend::compute_to_vertex_barrier(rhi::rhi_buffer_handle handle) {
    if (!m_recording || m_pass_active) throw std::runtime_error("Compute barrier must occur outside render pass");
    auto* b = m_buffers.get(handle);
    if (!b) return;
    VkBufferMemoryBarrier2 barrier = {};
    barrier.sType = VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;
    barrier.srcStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    barrier.srcAccessMask = VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT;
    barrier.dstStageMask = VK_PIPELINE_STAGE_2_VERTEX_ATTRIBUTE_INPUT_BIT;
    barrier.dstAccessMask = VK_ACCESS_2_VERTEX_ATTRIBUTE_READ_BIT;
    barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.buffer = b->buffer;
    barrier.offset = 0;
    barrier.size = VK_WHOLE_SIZE;
    VkDependencyInfo dep = {};
    dep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    dep.bufferMemoryBarrierCount = 1;
    dep.pBufferMemoryBarriers = &barrier;
    vkCmdPipelineBarrier2(m_frames->command_buffers[m_frames->current_frame], &dep);
}
} } }
