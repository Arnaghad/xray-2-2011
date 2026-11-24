#include "vulkan_backend.h"
#include "vulkan_diagnostics.h"
#include "vulkan_creation.h"
#include "material_contract.h"
#include "texture_upload.h"
#include "attachment_contract.h"
#include <algorithm>
#include <cmath>
#include <cstring>

namespace xray { namespace render { namespace vulkan {
namespace {
void check(VkResult r, char const* operation) {
    if (r != VK_SUCCESS) throw std::runtime_error(std::string(operation) + " (VkResult=" + std::to_string(r) + ")");
}
void flush(VkDevice d, vulkan_buffer_entry const& b, bool coherent) {
    if (coherent) return;
    VkMappedMemoryRange range = {};
    range.sType = VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
    range.memory = b.memory; range.size = VK_WHOLE_SIZE;
    check(vkFlushMappedMemoryRanges(d, 1, &range), "Flush host buffer");
}
VkFilter filter(rhi::sampler_filter value) {
    switch (value) {
        case rhi::sampler_filter::nearest: return VK_FILTER_NEAREST;
        case rhi::sampler_filter::linear: return VK_FILTER_LINEAR;
        default: throw std::runtime_error("Unsupported sampler filter");
    }
}
VkSamplerAddressMode address(rhi::sampler_address value) {
    switch (value) {
        case rhi::sampler_address::repeat: return VK_SAMPLER_ADDRESS_MODE_REPEAT;
        case rhi::sampler_address::clamp: return VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        case rhi::sampler_address::mirror: return VK_SAMPLER_ADDRESS_MODE_MIRRORED_REPEAT;
        default: throw std::runtime_error("Unsupported sampler address");
    }
}
}
vulkan_buffer_entry vulkan_backend::allocate_host_buffer(VkDeviceSize size, VkBufferUsageFlags usage, bool& coherent) {
    vulkan_buffer_entry b; b.size = size;
    try {
        VkBufferCreateInfo info = {}; info.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        info.size = size; info.usage = usage; info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        check(create_handle(b.buffer, vkCreateBuffer, m_device, &info, allocation_callbacks()), "Create host buffer");
        VkMemoryRequirements req; vkGetBufferMemoryRequirements(m_device, b.buffer, &req);
        VkPhysicalDeviceMemoryProperties properties; vkGetPhysicalDeviceMemoryProperties(m_physical_device, &properties);
        u32 type = UINT32_MAX;
        for (u32 i = 0; i < properties.memoryTypeCount; ++i) {
            if (!(req.memoryTypeBits & (1u << i)) || !(properties.memoryTypes[i].propertyFlags & VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT)) continue;
            type = i;
            if (properties.memoryTypes[i].propertyFlags & VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) break;
        }
        if (type == UINT32_MAX) throw std::runtime_error("No host-visible memory");
        coherent = (properties.memoryTypes[type].propertyFlags & VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) != 0;
        VkMemoryAllocateInfo allocation = {}; allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocation.allocationSize = req.size; allocation.memoryTypeIndex = type;
        check(allocate_memory(allocation, b.memory), "Allocate host memory");
        check(vkBindBufferMemory(m_device, b.buffer, b.memory, 0), "Bind host memory");
        check(vkMapMemory(m_device, b.memory, 0, VK_WHOLE_SIZE, 0, &b.mapped), "Map host memory");
        return b;
    } catch (...) { free_buffer(b); throw; }
}
vulkan_texture_entry vulkan_backend::upload_texture(rhi::texture_desc const& desc, std::vector<rhi::texture_subresource_data> const& data) {
    VkPhysicalDeviceProperties properties; vkGetPhysicalDeviceProperties(m_physical_device, &properties);
    bool cube=desc.type==rhi::texture_type::type_cube;
    u32 dimension=cube ? properties.limits.maxImageDimensionCube : properties.limits.maxImageDimension2D;
    if (desc.width > dimension || desc.height > dimension || desc.array_layers>properties.limits.maxImageArrayLayers)
        throw std::runtime_error("Texture exceeds device limits");
    auto packed = pack_texture(desc, data);
    if (sampled_texture_block(desc.pixel_format).extent == 4 && !m_bc_textures_enabled)
        throw std::runtime_error("Device does not support BC texture compression");
    vulkan_texture_entry t; t.width = desc.width; t.height = desc.height; t.description = desc;
    t.format = rhi::to_vk_format(desc.pixel_format);
    VkFormatProperties fp; vkGetPhysicalDeviceFormatProperties(m_physical_device, t.format, &fp);
    VkFormatFeatureFlags required = VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT | VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT | VK_FORMAT_FEATURE_TRANSFER_DST_BIT;
    if ((fp.optimalTilingFeatures & required) != required) throw std::runtime_error("Sampled texture format unsupported");
    vulkan_buffer_entry staging; VkCommandBuffer cmd = VK_NULL_HANDLE; VkFence fence = VK_NULL_HANDLE;
    bool submitted = false;
    auto cleanup = [&] {
        if (submitted) vkWaitForFences(m_device, 1, &fence, VK_TRUE, UINT64_MAX);
        if (fence) vkDestroyFence(m_device, fence, allocation_callbacks());
        if (cmd) vkFreeCommandBuffers(m_device, m_frames->command_pool, 1, &cmd);
        free_buffer(staging);
    };
    try {
        bool coherent = false;
        staging = allocate_host_buffer(packed.bytes.size(), VK_BUFFER_USAGE_TRANSFER_SRC_BIT, coherent);
        std::memcpy(staging.mapped, packed.bytes.data(), packed.bytes.size()); flush(m_device, staging, coherent);
        VkImageCreateInfo image = {}; image.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
        image.flags=cube ? VK_IMAGE_CREATE_CUBE_COMPATIBLE_BIT : 0;
        image.imageType = VK_IMAGE_TYPE_2D; image.format = t.format; image.extent = {desc.width, desc.height, 1};
        image.mipLevels = desc.mip_levels; image.arrayLayers = desc.array_layers; image.samples = VK_SAMPLE_COUNT_1_BIT;
        image.tiling = VK_IMAGE_TILING_OPTIMAL; image.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
        image.sharingMode = VK_SHARING_MODE_EXCLUSIVE; image.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        check(create_handle(t.image, vkCreateImage, m_device, &image, allocation_callbacks()), "Create sampled image");
        VkMemoryRequirements req; vkGetImageMemoryRequirements(m_device, t.image, &req);
        VkMemoryAllocateInfo allocation = {}; allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocation.allocationSize = req.size; allocation.memoryTypeIndex = find_memory_type(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        check(allocate_memory(allocation, t.memory), "Allocate texture memory");
        check(vkBindImageMemory(m_device, t.image, t.memory, 0), "Bind texture memory");
        VkImageViewCreateInfo view = {}; view.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        view.image = t.image; view.viewType = cube ? VK_IMAGE_VIEW_TYPE_CUBE : desc.type==rhi::texture_type::type_2d_array ? VK_IMAGE_VIEW_TYPE_2D_ARRAY : VK_IMAGE_VIEW_TYPE_2D; view.format = t.format;
        view.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, desc.mip_levels, 0, desc.array_layers};
        check(create_handle(t.view, vkCreateImageView, m_device, &view, allocation_callbacks()), "Create texture view");
        VkCommandBufferAllocateInfo commands = {}; commands.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
        commands.commandPool = m_frames->command_pool; commands.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; commands.commandBufferCount = 1;
        check(create_handle(cmd, vkAllocateCommandBuffers, m_device, &commands), "Allocate upload commands");
        VkCommandBufferBeginInfo begin = {}; begin.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO; begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        check(vkBeginCommandBuffer(cmd, &begin), "Begin texture upload");
        VkImageMemoryBarrier2 barrier = {};
        barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        barrier.srcStageMask = VK_PIPELINE_STAGE_2_NONE;
        barrier.srcAccessMask = VK_ACCESS_2_NONE;
        barrier.dstStageMask = VK_PIPELINE_STAGE_2_COPY_BIT;
        barrier.dstAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
        barrier.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        barrier.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        barrier.srcQueueFamilyIndex = barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        barrier.image = t.image;
        barrier.subresourceRange = view.subresourceRange;
        VkDependencyInfo dep = {};
        dep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        dep.imageMemoryBarrierCount = 1;
        dep.pImageMemoryBarriers = &barrier;
        vkCmdPipelineBarrier2(cmd, &dep);

        std::vector<VkBufferImageCopy> regions(size_t(desc.mip_levels)*desc.array_layers);
        for (u32 layer=0;layer<desc.array_layers;++layer)
        for (u32 mip = 0; mip < desc.mip_levels; ++mip) {
            size_t index=size_t(layer)*desc.mip_levels+mip;
            regions[index].bufferOffset = packed.offsets[index]; regions[index].imageSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, mip, layer, 1};
            regions[index].imageExtent = {std::max(1u, desc.width >> mip), std::max(1u, desc.height >> mip), 1};
        }
        vkCmdCopyBufferToImage(cmd, staging.buffer, t.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, u32(regions.size()), regions.data());

        barrier.srcStageMask = VK_PIPELINE_STAGE_2_COPY_BIT;
        barrier.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
        barrier.dstStageMask = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
        barrier.dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
        barrier.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        barrier.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        vkCmdPipelineBarrier2(cmd, &dep);
        check(vkEndCommandBuffer(cmd), "End texture upload");

        VkFenceCreateInfo fi = {}; fi.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        check(create_handle(fence, vkCreateFence, m_device, &fi, allocation_callbacks()), "Create upload fence");
        VkCommandBufferSubmitInfo cmd_info = {};
        cmd_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_SUBMIT_INFO;
        cmd_info.commandBuffer = cmd;
        VkSubmitInfo2 submit = {};
        submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO_2;
        submit.commandBufferInfoCount = 1;
        submit.pCommandBufferInfos = &cmd_info;
        check(vkQueueSubmit2(m_graphics_queue, 1, &submit, fence), "Submit texture upload"); submitted = true;
        check(vkWaitForFences(m_device, 1, &fence, VK_TRUE, UINT64_MAX), "Wait texture upload");
        t.state = rhi::resource_state::shader_resource; t.contents_valid = true;
        cleanup(); return t;
    } catch (...) { cleanup(); free_texture(t); throw; }
}
rhi::rhi_texture_handle vulkan_backend::create_texture(rhi::texture_desc const& desc, std::vector<rhi::texture_subresource_data> const& data) {
    if (!m_initialized) return 0;
    vulkan_texture_entry t;
    try {
        if (desc.usage == rhi::texture_usage::render_target || desc.usage == rhi::texture_usage::depth_stencil) {
            if (!data.empty()) throw std::runtime_error("Attachment initial uploads unsupported");
            t = allocate_attachment(desc);
        } else t = upload_texture(desc, data);
        return m_textures.allocate(t);
    }
    catch (std::exception const& e) { free_texture(t); diagnostic("error", "Texture creation failed: %s", e.what()); return 0; }
}
rhi::rhi_texture_handle vulkan_backend::create_texture(rhi::texture_type type, u32 width, u32 height, rhi::format fmt, bool mipmaps) {
    rhi::texture_desc desc; desc.type = type; desc.width = width; desc.height = height; desc.pixel_format = fmt;
    if (type==rhi::texture_type::type_cube) desc.array_layers=6;
    if (mipmaps) for (u32 size = std::max(width, height); size > 1; size >>= 1) ++desc.mip_levels;
    return create_texture(desc, {});
}
void vulkan_backend::update_texture(rhi::rhi_texture_handle handle, std::vector<rhi::texture_subresource_data> const& data) {
    auto* old = m_textures.get(handle);
    if (!m_initialized || !old || old->description.usage != rhi::texture_usage::sampled) throw std::runtime_error("Updates require a live sampled texture");
    // Copy-on-update keeps descriptor snapshots in recorded/pending draws intact.
    auto next = upload_texture(old->description, data);
    try { m_retired_textures.push_back({retirement_serial(), *old}); }
    catch (...) { free_texture(next); throw; }
    *old = std::move(next); old->in_use = true;
    if (auto it = m_bindless_2d_slots.find(handle); it != m_bindless_2d_slots.end())
        write_bindless_descriptor(0, it->second, *old);
    else if (auto cit = m_bindless_cube_slots.find(handle); cit != m_bindless_cube_slots.end())
        write_bindless_descriptor(1, cit->second, *old);
    collect_retired();
}
void vulkan_backend::destroy_texture(rhi::rhi_texture_handle handle) {
    auto* t = m_textures.get(handle); if (!t) return;
    if (m_pass_active && !m_presentation_pass) {
        for (auto const& a : m_pass_description.colors) if (a.texture == handle) throw std::runtime_error("Cannot destroy an active attachment");
        if (m_pass_description.depth.texture == handle) throw std::runtime_error("Cannot destroy active depth attachment");
    }
    u32 slot = 0; bool cube = false;
    if (auto it = m_bindless_2d_slots.find(handle); it != m_bindless_2d_slots.end()) {
        slot = it->second; m_bindless_2d_slots.erase(it);
    } else if (auto cit = m_bindless_cube_slots.find(handle); cit != m_bindless_cube_slots.end()) {
        slot = cit->second; cube = true; m_bindless_cube_slots.erase(cit);
    }
    m_retired_textures.push_back({retirement_serial(), *t, slot, cube});
    m_textures.release(handle); *t = {};
    for (auto& bound : m_bound_textures) if (bound==handle) bound=0;
    collect_retired();
}
rhi::rhi_sampler_handle vulkan_backend::create_sampler(rhi::sampler_desc const& desc) {
    if (!m_initialized) return 0;
    vulkan_sampler_entry entry;
    try {
        VkPhysicalDeviceProperties p; vkGetPhysicalDeviceProperties(m_physical_device, &p);
        if (!std::isfinite(desc.min_lod) || !std::isfinite(desc.max_lod) || !std::isfinite(desc.lod_bias)
            || desc.min_lod < 0 || desc.max_lod < desc.min_lod || std::abs(desc.lod_bias) > p.limits.maxSamplerLodBias || desc.anisotropy != 1
            || desc.compare<rhi::comparison_func::never || desc.compare>rhi::comparison_func::always)
            throw std::runtime_error("Invalid sampler LOD or unsupported anisotropy");
        VkSamplerCreateInfo info = {}; info.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        info.magFilter = filter(desc.mag_filter); info.minFilter = filter(desc.min_filter);
        filter(desc.mip_filter); info.mipmapMode = desc.mip_filter == rhi::sampler_filter::linear ? VK_SAMPLER_MIPMAP_MODE_LINEAR : VK_SAMPLER_MIPMAP_MODE_NEAREST;
        info.addressModeU = address(desc.address_u); info.addressModeV = address(desc.address_v); info.addressModeW = info.addressModeV;
        info.minLod = desc.min_lod; info.maxLod = desc.max_lod; info.mipLodBias = desc.lod_bias; info.maxAnisotropy = 1;
        info.compareEnable=desc.comparison; info.compareOp=rhi::to_vk_compare_op(desc.compare);
        check(create_handle(entry.sampler, vkCreateSampler, m_device, &info, allocation_callbacks()), "Create sampler");
        entry.description = desc;
        return m_samplers.allocate(entry);
    } catch (std::exception const& e) {
        if (entry.sampler) vkDestroySampler(m_device, entry.sampler, allocation_callbacks());
        diagnostic("error", "Sampler creation failed: %s", e.what()); return 0;
    }
}
void vulkan_backend::destroy_sampler(rhi::rhi_sampler_handle handle) {
    auto* s = m_samplers.get(handle); if (!s) return;
    m_retired_samplers.push_back({retirement_serial(), s->sampler}); m_samplers.release(handle); *s = {};
    if (m_bound_sampler == handle) m_bound_sampler = 0; collect_retired();
    if (m_bound_comparison_sampler==handle) m_bound_comparison_sampler=0;
}
void vulkan_backend::update_buffer(rhi::rhi_buffer_handle handle, void const* data, size_t size) {
    auto* b = m_buffers.get(handle);
    if (!m_initialized || !b || !data || !size || size > b->size)
        throw std::runtime_error("Buffer update requires live buffer and valid size");
    if (b->type == rhi::buffer_type::constant) {
        if (size != b->size) throw std::runtime_error("Constant update requires exact size");
        std::memcpy(b->index_data.data(), data, size);
        return;
    }
    if (b->type != rhi::buffer_type::structured)
        throw std::runtime_error("Buffer update supports only constant or structured buffers");
    void* mapped = nullptr;
    check(vkMapMemory(m_device, b->memory, 0, VK_WHOLE_SIZE, 0, &mapped), "Map structured buffer");
    std::memcpy(mapped, data, size);
    VkMappedMemoryRange range = {};
    range.sType = VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
    range.memory = b->memory;
    range.size = VK_WHOLE_SIZE;
    check(vkFlushMappedMemoryRanges(m_device, 1, &range), "Flush structured buffer");
    vkUnmapMemory(m_device, b->memory);
}
void vulkan_backend::bind_constant_buffer(u32 slot, rhi::rhi_buffer_handle handle) {
    if (!m_recording || slot > 1) throw std::runtime_error("Constant binding slot/state invalid");
    auto* b = m_buffers.get(handle);
    if (handle && (!b || b->type != rhi::buffer_type::constant || (slot==0 ? b->size!=sizeof(frame_constants)
        : b->size!=sizeof(temporal_constants) && b->size!=sizeof(postprocess_constants) && b->size!=sizeof(object_constants) && b->size!=sizeof(surface_object_constants)
            && b->size!=sizeof(mapped_surface_object_constants) && b->size!=sizeof(material_object_constants)
            && b->size!=sizeof(detailed_surface_object_constants) && b->size!=sizeof(emissive_material_object_constants)
            && b->size!=sizeof(extended_gbuffer_object_constants) && b->size!=sizeof(extended_material_object_constants)
            && b->size!=sizeof(extended_forward_object_constants) && b->size!=sizeof(decal_constants)
            && b->size!=sizeof(lighting_constants) && b->size!=sizeof(shadow_lighting_constants))))
        throw std::runtime_error("Constant binding violates frame/object ABI");
    m_bound_constants[slot] = handle;
}
void vulkan_backend::bind_texture(u32 slot, rhi::rhi_texture_handle handle) {
    if (!m_recording || slot>=m_bound_textures.size() || (handle && !m_textures.get(handle))) throw std::runtime_error("Invalid texture binding");
    m_bound_textures[slot] = handle;
}
void vulkan_backend::bind_sampler(u32 slot, rhi::rhi_sampler_handle handle) {
    if (!m_recording || slot>1 || (handle && !m_samplers.get(handle))) throw std::runtime_error("Invalid sampler binding");
    (slot ? m_bound_comparison_sampler : m_bound_sampler)=handle;
}
void vulkan_backend::write_bindless_descriptor(u32 binding, u32 slot, vulkan_texture_entry const& texture) {
    if (!m_bindless_enabled || !m_bindless_set) return;
    bool depth = depth_format(texture.description.pixel_format);
    VkDescriptorImageInfo image = {VK_NULL_HANDLE, texture.sample_view ? texture.sample_view : texture.view,
        depth ? VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};
    VkWriteDescriptorSet write = {};
    write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    write.dstSet = m_bindless_set;
    write.dstBinding = binding;
    write.dstArrayElement = slot;
    write.descriptorCount = 1;
    write.descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
    write.pImageInfo = &image;
    vkUpdateDescriptorSets(m_device, 1, &write, 0, nullptr);
}
u32 vulkan_backend::register_bindless_texture(rhi::rhi_texture_handle handle) {
    if (!m_initialized || !m_bindless_enabled || !m_bindless_set || !handle) return 0;
    auto* texture = m_textures.get(handle);
    if (!texture || !texture->contents_valid || texture->description.usage != rhi::texture_usage::sampled) return 0;
    bool cube = texture->description.type == rhi::texture_type::type_cube;
    if (!cube && texture->description.type != rhi::texture_type::type_2d) return 0;
    auto& slots = cube ? m_bindless_cube_slots : m_bindless_2d_slots;
    if (auto it = slots.find(handle); it != slots.end()) return it->second;
    auto& free_slots = cube ? m_free_bindless_cube : m_free_bindless_2d;
    u32& next_slot = cube ? m_next_bindless_cube : m_next_bindless_2d;
    u32 const limit = cube ? MAX_BINDLESS_CUBE : MAX_BINDLESS_2D;
    u32 slot = 0;
    if (!free_slots.empty()) {
        slot = free_slots.back();
        free_slots.pop_back();
    } else if (next_slot < limit) {
        slot = next_slot++;
    } else {
        return 0;
    }
    slots.emplace(handle, slot);
    write_bindless_descriptor(cube ? 1u : 0u, slot, *texture);
    return slot;
}
void vulkan_backend::initialize_material_resources() {
    VkPhysicalDeviceProperties p; vkGetPhysicalDeviceProperties(m_physical_device, &p);
    if (p.limits.maxUniformBufferRange < sizeof(shadow_lighting_constants))
        throw std::runtime_error("Uniform range too small");
    m_uniform_alignment = std::max(p.limits.minUniformBufferOffsetAlignment, p.limits.nonCoherentAtomSize);
    unsigned char white[4] = {255,255,255,255};
    m_fallback_texture = create_texture(rhi::texture_desc{}, {{0, 0, white, 4, 0}});
    if (!m_fallback_texture) throw std::runtime_error(std::string("Fallback texture unavailable: ") + diagnostics().last_error);
    unsigned char black[4] = {0,0,0,255};
    rhi::texture_desc cube_desc = {};
    cube_desc.type = rhi::texture_type::type_cube;
    cube_desc.array_layers = 6;
    std::vector<rhi::texture_subresource_data> cube_faces(6);
    for (u32 f = 0; f < 6; ++f) cube_faces[f] = {0, f, black, 4, 0};
    m_fallback_cubemap = create_texture(cube_desc, cube_faces);
    if (!m_fallback_cubemap) throw std::runtime_error(std::string("Fallback cubemap unavailable: ") + diagnostics().last_error);
    m_fallback_sampler = create_sampler({});
    if (!m_fallback_sampler) throw std::runtime_error(std::string("Fallback sampler unavailable: ") + diagnostics().last_error);
    if (m_bindless_enabled && m_descriptor_layouts[3] != VK_NULL_HANDLE) {
        VkDescriptorPoolSize pool_size = {VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, MAX_BINDLESS_2D + MAX_BINDLESS_CUBE};
        VkDescriptorPoolCreateInfo pool_info = {};
        pool_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        pool_info.flags = VK_DESCRIPTOR_POOL_CREATE_UPDATE_AFTER_BIND_BIT;
        pool_info.maxSets = 1;
        pool_info.poolSizeCount = 1;
        pool_info.pPoolSizes = &pool_size;
        check(create_handle(m_bindless_pool, vkCreateDescriptorPool, m_device, &pool_info, allocation_callbacks()), "Create bindless descriptor pool");
        VkDescriptorSetAllocateInfo alloc_info = {};
        alloc_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
        alloc_info.descriptorPool = m_bindless_pool;
        alloc_info.descriptorSetCount = 1;
        alloc_info.pSetLayouts = &m_descriptor_layouts[3];
        check(vkAllocateDescriptorSets(m_device, &alloc_info, &m_bindless_set), "Allocate bindless descriptor set");
        write_bindless_descriptor(0, 0, *m_textures.get(m_fallback_texture));
        write_bindless_descriptor(1, 0, *m_textures.get(m_fallback_cubemap));
    }
}
void vulkan_backend::reset_material_frame() {
    auto& frame = m_frames->materials[m_frames->current_frame];
    for (auto& chunk : frame.uniforms) chunk.cursor = 0;
    // The slot fence and command reset precede this rewind. Keep the allocated
    // sets and overwrite their bindings only when this slot records new draws.
    frame.descriptor_cursor = frame.compute_descriptor_cursor = frame.uniform_cursor = 0;
}
void vulkan_backend::cleanup_material_frames() noexcept {
    for (auto& frame : m_frames->materials) {
        for (auto pool : frame.pools) vkDestroyDescriptorPool(m_device, pool, allocation_callbacks());
        for (auto const& chunk : frame.uniforms) free_buffer(chunk.resource);
        frame = {};
    }
}
VkDescriptorBufferInfo vulkan_backend::uniform_slice(void const* data, size_t size) {
    auto& frame = m_frames->materials[m_frames->current_frame];
    for (;;) {
        if (frame.uniform_cursor == frame.uniforms.size()) {
            uniform_chunk chunk; chunk.resource = allocate_host_buffer(std::max<VkDeviceSize>(32768, m_uniform_alignment * 2), VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT, chunk.coherent);
            try { frame.uniforms.push_back(chunk); } catch (...) { free_buffer(chunk.resource); throw; }
            diagnostic("info", "Uniform arena grew: slot=%u chunks=%zu", m_frames->current_frame, frame.uniforms.size());
        }
        auto& chunk = frame.uniforms[frame.uniform_cursor];
        auto offset = (chunk.cursor + m_uniform_alignment - 1) / m_uniform_alignment * m_uniform_alignment;
        if (offset + size > chunk.resource.size) { ++frame.uniform_cursor; continue; }
        std::memcpy(static_cast<unsigned char*>(chunk.resource.mapped) + offset, data, size);
        chunk.cursor = offset + size; flush(m_device, chunk.resource, chunk.coherent);
        return {chunk.resource.buffer, offset, size};
    }
}
void vulkan_backend::bind_material_descriptors() {
    auto* vs = m_shaders.get(m_current_state.vs_shader); auto* ps = m_shaders.get(m_current_state.ps_shader);
    if (!((vs ? vs->descriptor_mask : 0) | (ps ? ps->descriptor_mask : 0))) return;
    frame_constants fallback_frame; identity(fallback_frame.view); identity(fallback_frame.projection);
    auto object_abi=[](auto const* shader) { return shader && (shader->descriptor_mask & 2)
        ? ((shader->descriptor_mask & screen_effect_object_abi) ? sizeof(screen_effect_constants)
            : (shader->descriptor_mask & decal_object_abi) ? sizeof(decal_constants)
            : (shader->descriptor_mask & temporal_object_abi) ? sizeof(temporal_constants)
            : (shader->descriptor_mask & postprocess_object_abi) ? sizeof(postprocess_constants)
            : (shader->descriptor_mask & shadow_lighting_object_abi) ? sizeof(shadow_lighting_constants)
            : (shader->descriptor_mask & environment_lighting_object_abi) ? sizeof(lighting_constants)
            : (shader->descriptor_mask & local_lighting_object_abi) ? size_t(160)
            : (shader->descriptor_mask & lighting_object_abi) ? size_t(128)
            : (shader->descriptor_mask & emissive_material_object_abi) ? sizeof(emissive_material_object_constants)
            : (shader->descriptor_mask & extended_material_object_abi) ? sizeof(extended_material_object_constants)
            : (shader->descriptor_mask & extended_gbuffer_object_abi) ? sizeof(extended_gbuffer_object_constants)
            : (shader->descriptor_mask & extended_forward_object_abi) ? sizeof(extended_forward_object_constants)
            : (shader->descriptor_mask & detailed_surface_object_abi) ? sizeof(detailed_surface_object_constants)
            : (shader->descriptor_mask & material_object_abi) ? sizeof(material_object_constants)
            : (shader->descriptor_mask & mapped_surface_object_abi) ? sizeof(mapped_surface_object_constants)
            : (shader->descriptor_mask & surface_object_abi) ? sizeof(surface_object_constants) : sizeof(object_constants)) : size_t(0); };
    size_t vs_abi=object_abi(vs),ps_abi=object_abi(ps);
    constexpr u64 versions=surface_object_abi|mapped_surface_object_abi|material_object_abi|detailed_surface_object_abi|extended_gbuffer_object_abi|extended_material_object_abi|extended_forward_object_abi|lighting_object_abi|local_lighting_object_abi|emissive_material_object_abi|environment_lighting_object_abi|shadow_lighting_object_abi|postprocess_object_abi|temporal_object_abi|decal_object_abi|screen_effect_object_abi;
    if (vs_abi && ps_abi && (vs_abi!=ps_abi || (vs->descriptor_mask & versions)!=(ps->descriptor_mask & versions)))
        throw std::runtime_error("Vertex/pixel object ABI versions disagree");
    size_t object_size=std::max(vs_abi,ps_abi); if (!object_size) object_size=sizeof(object_constants);
    material_object_constants fallback_object = {}; identity(fallback_object.mapped.scalar.object.world);
    for (auto& value : fallback_object.mapped.scalar.object.tint) value = 1;
    fallback_object.mapped.scalar.surface[0]=fallback_object.mapped.scalar.surface[1]=1; fallback_object.mapped.scalar.surface[2]=.04f;
    fallback_object.mapped.map_parameters[1]=1;
    fallback_object.specular[0]=fallback_object.specular[1]=fallback_object.specular[2]=1;
    fallback_object.specular[3]=1;
    if (((vs ? vs->descriptor_mask : 0) | (ps ? ps->descriptor_mask : 0)) & (lighting_object_abi|emissive_material_object_abi|extended_gbuffer_object_abi|extended_material_object_abi|extended_forward_object_abi|postprocess_object_abi|temporal_object_abi|decal_object_abi|screen_effect_object_abi))
        if (!m_bound_constants[1]) throw std::runtime_error("Lighting/emissive/postprocess shader requires explicit constants");
    auto* f = m_buffers.get(m_bound_constants[0]); auto* o = m_buffers.get(m_bound_constants[1]);
    if ((vs_abi || ps_abi) && o && o->size!=object_size) throw std::runtime_error("Bound object buffer does not match shader ABI version");
    VkDescriptorBufferInfo buffers[2] = {
        uniform_slice(f ? static_cast<void const*>(f->index_data.data()) : &fallback_frame, sizeof(fallback_frame)),
        uniform_slice(o && (vs_abi || ps_abi) ? static_cast<void const*>(o->index_data.data()) : &fallback_object, object_size)};
    auto* sampler = m_samplers.get(m_bound_sampler ? m_bound_sampler : m_fallback_sampler);
    if (!sampler) throw std::runtime_error("Material fallback sampler missing");
    u64 descriptor_mask=(vs ? vs->descriptor_mask : 0) | (ps ? ps->descriptor_mask : 0);
    if ((descriptor_mask & 8) && sampler->description.comparison)
        throw std::runtime_error("Ordinary texture sampling cannot use a comparison sampler");
    bool shadow=(descriptor_mask & shadow_depth_binding)!=0;
    auto* comparison=m_samplers.get(shadow ? m_bound_comparison_sampler : m_fallback_sampler);
    if (!comparison || (shadow && !comparison->description.comparison))
        throw std::runtime_error("Shadow comparison requires an explicit comparison sampler in slot 1");
    std::array<u64,8> texture_bits={4,normal_texture_binding,roughness_texture_binding,specular_texture_binding,detail_normal_texture_binding,shadow_depth_binding|extra_texture_binding,extra_texture_binding_1,extra_texture_binding_2};
    VkDescriptorImageInfo images[10] = {};
    for (unsigned slot=0;slot<texture_bits.size();++slot) {
        bool used=(descriptor_mask & texture_bits[slot])!=0;
        auto handle=used ? m_bound_textures[slot] : 0;
        if (used && (slot || (descriptor_mask & (postprocess_object_abi|temporal_object_abi|lighting_object_abi|decal_object_abi|screen_effect_object_abi))) && !handle)
            throw std::runtime_error("Mapped material/postprocess shader requires an explicit texture");
        auto* texture=m_textures.get(handle ? handle : m_fallback_texture);
        if (!texture) throw std::runtime_error("Material texture is missing");
        bool cube=used && slot==0 && (descriptor_mask & cube_texture_binding);
        bool array=used && (descriptor_mask & array_binding(slot));
        if (used && (cube ? !handle || texture->description.type!=rhi::texture_type::type_cube
                        : array ? !handle || texture->description.type!=rhi::texture_type::type_2d_array
                        : texture->description.type!=rhi::texture_type::type_2d))
            throw std::runtime_error("Bound texture dimension does not match shader (cubemap requires an explicit texture)");
        bool depth=depth_format(texture->description.pixel_format);
        bool multisample=slot<5 && (descriptor_mask & multisample_texture_binding(slot));
        if (used && (texture->description.samples>1)!=multisample) throw std::runtime_error("Bound texture sample count does not match shader image type");
        if (used && slot==5 && (descriptor_mask & shadow_depth_binding) && (!depth || !texture->depth_comparison_supported))
            throw std::runtime_error("Shadow comparison requires a depth texture with depth-comparison format support");
        if (!texture->contents_valid || (texture->state!=rhi::resource_state::shader_resource && texture->state!=rhi::resource_state::depth_read))
            throw std::runtime_error("Material texture must be initialized and transitioned for sampling (no attachment feedback)");
        if (depth && slot!=5 && !multisample && !(slot==3 && (descriptor_mask & temporal_object_abi)) && !(slot==2 && (descriptor_mask & extended_forward_object_abi)) && (sampler->description.min_filter!=rhi::sampler_filter::nearest || sampler->description.mag_filter!=rhi::sampler_filter::nearest
            || sampler->description.mip_filter!=rhi::sampler_filter::nearest)) throw std::runtime_error("Depth sampling requires a nearest sampler");
        images[slot]={VK_NULL_HANDLE,texture->sample_view ? texture->sample_view : texture->view,
            depth ? VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};
    }
    images[8]={sampler->sampler,VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED};
    images[9]={comparison->sampler,VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED};
    auto& frame = m_frames->materials[m_frames->current_frame]; std::array<VkDescriptorSet,3> sets{};
    if(frame.descriptor_cursor<frame.descriptors.size())sets=frame.descriptors[frame.descriptor_cursor];
    else {
      // Reserve before the Vulkan allocation so publishing the three handles
      // cannot fail and consume an unreachable portion of a retained pool.
      if(frame.descriptors.size()==frame.descriptors.capacity())frame.descriptors.reserve(frame.descriptors.size()+64);
      for (;;) {
        bool fresh = frame.pool_cursor == frame.pools.size();
        if (frame.pool_cursor == frame.pools.size()) {
            VkDescriptorPoolSize sizes[4] = {{VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 128}, {VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 512}, {VK_DESCRIPTOR_TYPE_SAMPLER, 128}, {VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 128}};
            VkDescriptorPoolCreateInfo info = {}; info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
            info.maxSets = 192; info.poolSizeCount = 4; info.pPoolSizes = sizes;
            VkDescriptorPool pool = VK_NULL_HANDLE;
            check(create_handle(pool, vkCreateDescriptorPool, m_device, &info, allocation_callbacks()), "Create material descriptor pool");
            try { frame.pools.push_back(pool); } catch (...) { vkDestroyDescriptorPool(m_device, pool, allocation_callbacks()); throw; }
            diagnostic("info", "Descriptor pools grew: slot=%u pools=%zu", m_frames->current_frame, frame.pools.size());
        }
        VkDescriptorSetAllocateInfo info = {}; info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
        info.descriptorPool = frame.pools[frame.pool_cursor]; info.descriptorSetCount = 3; info.pSetLayouts = m_descriptor_layouts;
        auto result = vkAllocateDescriptorSets(m_device, &info, sets.data());
        if (result == VK_ERROR_OUT_OF_POOL_MEMORY || result == VK_ERROR_FRAGMENTED_POOL) {
            if (fresh) throw std::runtime_error("Fresh material descriptor pool cannot hold one draw");
            ++frame.pool_cursor; continue;
        }
        check(result, "Allocate material descriptors");
        frame.descriptors.push_back(sets);
        m_frames->performance.descriptor_sets_allocated+=3;
        break;
      }
    }
    ++frame.descriptor_cursor;
    VkWriteDescriptorSet writes[12] = {};
    for (auto& w : writes) { w.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET; w.descriptorCount = 1; }
    writes[0].dstSet = sets[0]; writes[0].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER; writes[0].pBufferInfo = &buffers[0];
    writes[1].dstSet = sets[2]; writes[1].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER; writes[1].pBufferInfo = &buffers[1];
    writes[2].dstSet = sets[1]; writes[2].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE; writes[2].pImageInfo = &images[0];
    writes[3].dstSet = sets[1]; writes[3].dstBinding = 1; writes[3].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER; writes[3].pImageInfo = &images[8];
    for (unsigned i=0;i<4;++i) {
        writes[4+i].dstSet=sets[1]; writes[4+i].dstBinding=2+i; writes[4+i].descriptorType=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE; writes[4+i].pImageInfo=&images[1+i];
    }
    writes[8].dstSet=sets[1]; writes[8].dstBinding=6; writes[8].descriptorType=VK_DESCRIPTOR_TYPE_SAMPLER; writes[8].pImageInfo=&images[9];
    writes[9].dstSet=sets[1]; writes[9].dstBinding=7; writes[9].descriptorType=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE; writes[9].pImageInfo=&images[5];
    writes[10].dstSet=sets[1]; writes[10].dstBinding=8; writes[10].descriptorType=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE; writes[10].pImageInfo=&images[6];
    writes[11].dstSet=sets[1]; writes[11].dstBinding=9; writes[11].descriptorType=VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE; writes[11].pImageInfo=&images[7];
    vkUpdateDescriptorSets(m_device, 12, writes, 0, nullptr);
    m_frames->performance.descriptors_written+=12;
    if (m_bindless_enabled && m_bindless_set != VK_NULL_HANDLE) {
        VkDescriptorSet bind_sets[4] = {sets[0], sets[1], sets[2], m_bindless_set};
        vkCmdBindDescriptorSets(m_frames->command_buffers[m_frames->current_frame], VK_PIPELINE_BIND_POINT_GRAPHICS, m_pipeline_layout, 0, 4, bind_sets, 0, nullptr);
    } else {
        vkCmdBindDescriptorSets(m_frames->command_buffers[m_frames->current_frame], VK_PIPELINE_BIND_POINT_GRAPHICS, m_pipeline_layout, 0, 3, sets.data(), 0, nullptr);
    }
}
} } }
