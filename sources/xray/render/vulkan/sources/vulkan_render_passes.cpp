#include "vulkan_backend.h"
#include "vulkan_creation.h"
#include "attachment_contract.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>

namespace xray { namespace render { namespace vulkan {
namespace {
void check(VkResult result, char const* operation) {
    if (result != VK_SUCCESS) throw std::runtime_error(std::string(operation) + " (VkResult=" + std::to_string(result) + ")");
}
VkImageAspectFlags aspects(rhi::format f) {
    return depth_format(f) ? VK_IMAGE_ASPECT_DEPTH_BIT | (f == rhi::format::d24_s8_unorm ? VK_IMAGE_ASPECT_STENCIL_BIT : 0) : VK_IMAGE_ASPECT_COLOR_BIT;
}
bool has_stencil_aspect(VkFormat f) {
    return f == VK_FORMAT_D24_UNORM_S8_UINT || f == VK_FORMAT_D32_SFLOAT_S8_UINT;
}
struct access_scope { VkImageLayout layout; VkPipelineStageFlags2 stages; VkAccessFlags2 access; };
access_scope scope(rhi::resource_state s, bool depth) {
    switch (s) {
        case rhi::resource_state::undefined: return {VK_IMAGE_LAYOUT_UNDEFINED, VK_PIPELINE_STAGE_2_NONE, VK_ACCESS_2_NONE};
        case rhi::resource_state::render_target:
            if (!depth) return {VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT, VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT};
            break;
        case rhi::resource_state::depth_write:
            if (depth) return {VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL, VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT, VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT};
            break;
        case rhi::resource_state::depth_read:
            if (!depth) break;
            return {VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL,
                VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT,
                VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_2_SHADER_SAMPLED_READ_BIT};
        case rhi::resource_state::shader_resource:
            return {depth ? VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT, VK_ACCESS_2_SHADER_SAMPLED_READ_BIT};
        case rhi::resource_state::copy_src:
            if (!depth) return {VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_READ_BIT};
            break;
        case rhi::resource_state::copy_dst:
            if (!depth) return {VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_PIPELINE_STAGE_2_TRANSFER_BIT, VK_ACCESS_2_TRANSFER_WRITE_BIT};
            break;
        default: break;
    }
    throw std::runtime_error("Unsupported attachment transition state");
}
VkAttachmentLoadOp load_op(rhi::attachment_load load) {
    return load == rhi::attachment_load::load ? VK_ATTACHMENT_LOAD_OP_LOAD : load == rhi::attachment_load::clear ? VK_ATTACHMENT_LOAD_OP_CLEAR : VK_ATTACHMENT_LOAD_OP_DONT_CARE;
}
VkAttachmentStoreOp store_op(rhi::attachment_store store) {
    return store == rhi::attachment_store::store ? VK_ATTACHMENT_STORE_OP_STORE : VK_ATTACHMENT_STORE_OP_DONT_CARE;
}
}
bool vulkan_backend::supports_samples(rhi::format format,u32 samples) const {
    if (!m_initialized || (samples!=1 && samples!=2 && samples!=4 && samples!=8) || (samples>1 && !m_sample_shading_enabled)) return false;
    bool depth=depth_format(format);
    VkImageUsageFlags usage=VK_IMAGE_USAGE_SAMPLED_BIT | (depth ? VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT
        : VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT);
    VkImageFormatProperties properties={};
    return vkGetPhysicalDeviceImageFormatProperties(m_physical_device,rhi::to_vk_format(format),VK_IMAGE_TYPE_2D,VK_IMAGE_TILING_OPTIMAL,usage,0,&properties)==VK_SUCCESS
        && (properties.sampleCounts & samples);
}
vulkan_texture_entry vulkan_backend::allocate_attachment(rhi::texture_desc const& desc) {
    validate_attachment(desc);
    if (!supports_samples(desc.pixel_format,desc.samples)) throw std::runtime_error("Unsupported attachment sample count/format or sample-rate shading");
    VkPhysicalDeviceProperties properties; vkGetPhysicalDeviceProperties(m_physical_device, &properties);
    if (desc.width > properties.limits.maxImageDimension2D || desc.height > properties.limits.maxImageDimension2D
        || desc.width > properties.limits.maxFramebufferWidth || desc.height > properties.limits.maxFramebufferHeight)
        throw std::runtime_error("Attachment exceeds device limits");
    bool depth = depth_format(desc.pixel_format);
    vulkan_texture_entry t; t.description = desc; t.width = desc.width; t.height = desc.height; t.format = rhi::to_vk_format(desc.pixel_format);
    VkFormatProperties fp; vkGetPhysicalDeviceFormatProperties(m_physical_device, t.format, &fp);
    VkFormatFeatureFlags required = VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT | (depth ? VK_FORMAT_FEATURE_DEPTH_STENCIL_ATTACHMENT_BIT : VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BIT | VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT);
    if ((fp.optimalTilingFeatures & required) != required) throw std::runtime_error("Attachment format unsupported by device");
    if (depth) {
        VkFormatProperties3 features={}; features.sType=VK_STRUCTURE_TYPE_FORMAT_PROPERTIES_3;
        VkFormatProperties2 properties2={}; properties2.sType=VK_STRUCTURE_TYPE_FORMAT_PROPERTIES_2; properties2.pNext=&features;
        vkGetPhysicalDeviceFormatProperties2(m_physical_device,t.format,&properties2);
        t.depth_comparison_supported=(features.optimalTilingFeatures & VK_FORMAT_FEATURE_2_SAMPLED_IMAGE_DEPTH_COMPARISON_BIT)!=0;
    }
    try {
        VkImageCreateInfo image = {}; image.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
        image.imageType = VK_IMAGE_TYPE_2D; image.format = t.format; image.extent = {desc.width, desc.height, 1};
        image.mipLevels = image.arrayLayers = 1; image.samples = static_cast<VkSampleCountFlagBits>(desc.samples); image.tiling = VK_IMAGE_TILING_OPTIMAL;
        image.usage = VK_IMAGE_USAGE_SAMPLED_BIT | (depth ? VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT : VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT);
        if (!depth) image.usage|=VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT;
        image.sharingMode = VK_SHARING_MODE_EXCLUSIVE; image.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        check(create_handle(t.image, vkCreateImage, m_device, &image, allocation_callbacks()), "Create attachment image");
        VkMemoryRequirements req; vkGetImageMemoryRequirements(m_device, t.image, &req);
        VkMemoryAllocateInfo allocation = {}; allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocation.allocationSize = req.size; allocation.memoryTypeIndex = find_memory_type(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        check(allocate_memory(allocation, t.memory), "Allocate attachment memory");
        check(vkBindImageMemory(m_device, t.image, t.memory, 0), "Bind attachment memory");
        VkImageViewCreateInfo view = {}; view.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        view.image = t.image; view.viewType = VK_IMAGE_VIEW_TYPE_2D; view.format = t.format;
        view.subresourceRange = {aspects(desc.pixel_format), 0, 1, 0, 1};
        check(create_handle(t.view, vkCreateImageView, m_device, &view, allocation_callbacks()), "Create attachment view");
        if (depth) {
            view.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
            check(create_handle(t.sample_view, vkCreateImageView, m_device, &view, allocation_callbacks()), "Create depth sampling view");
        }
        return t;
    } catch (...) {
        if (t.sample_view) vkDestroyImageView(m_device, t.sample_view, allocation_callbacks());
        if (t.view) vkDestroyImageView(m_device, t.view, allocation_callbacks());
        if (t.image) vkDestroyImage(m_device, t.image, allocation_callbacks());
        free_memory(t.memory);
        throw;
    }
}
void vulkan_backend::transition_texture(rhi::rhi_texture_handle handle, rhi::resource_state next) {
    auto* t = m_textures.get(handle);
    if (!m_recording || m_pass_active || !t || (t->description.usage != rhi::texture_usage::render_target && t->description.usage != rhi::texture_usage::depth_stencil)
        || next == rhi::resource_state::undefined) throw std::runtime_error("Transitions require a live attachment outside a render pass");
    bool depth = depth_format(t->description.pixel_format);
    auto before = scope(t->state, depth), after = scope(next, depth);
    if ((next == rhi::resource_state::shader_resource || next == rhi::resource_state::depth_read || next==rhi::resource_state::copy_src) && !t->contents_valid)
        throw std::runtime_error("Cannot sample undefined/discarded attachment contents");
    VkImageMemoryBarrier2 barrier = {};
    barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
    barrier.srcStageMask = before.stages;
    barrier.srcAccessMask = before.access;
    barrier.dstStageMask = after.stages;
    barrier.dstAccessMask = after.access;
    barrier.oldLayout = before.layout;
    barrier.newLayout = after.layout;
    barrier.srcQueueFamilyIndex = barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.image = t->image;
    barrier.subresourceRange = {aspects(t->description.pixel_format), 0, 1, 0, 1};
    VkDependencyInfo dep = {};
    dep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    dep.imageMemoryBarrierCount = 1;
    dep.pImageMemoryBarriers = &barrier;
    vkCmdPipelineBarrier2(m_frames->command_buffers[m_frames->current_frame], &dep);
    t->state = next;
}
void vulkan_backend::set_pass_viewport() {
    VkViewport viewport = {0, 0, float(m_pass_extent.width), float(m_pass_extent.height), 0, 1};
    VkRect2D scissor = {{0, 0}, m_pass_extent};
    vkCmdSetViewport(m_frames->command_buffers[m_frames->current_frame], 0, 1, &viewport);
    vkCmdSetScissor(m_frames->command_buffers[m_frames->current_frame], 0, 1, &scissor);
}
void vulkan_backend::begin_presentation_pass(bool preserve_contents) {
    if (!m_recording || m_pass_active) throw std::runtime_error("Presentation pass requires an idle recording frame");
    if (preserve_contents && !m_presentation_contents) throw std::runtime_error("No presentation contents to preserve in this frame");
    VkCommandBuffer cmd = m_frames->command_buffers[m_frames->current_frame];
    VkImage color_img = m_output->swapchain_images[m_output->current_image_index];
    VkImageView color_view = m_output->swapchain_image_views[m_output->current_image_index];
    auto const& depth_att = m_output->depth_attachments[m_output->current_image_index];
    bool const depth_has_stencil = has_stencil_aspect(m_output->depth_format);
    VkImageAspectFlags const depth_aspect = VK_IMAGE_ASPECT_DEPTH_BIT | (depth_has_stencil ? VK_IMAGE_ASPECT_STENCIL_BIT : 0);

    if (!preserve_contents) {
        VkImageMemoryBarrier2 barriers[2] = {};
        barriers[0].sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        barriers[0].srcStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
        barriers[0].srcAccessMask = VK_ACCESS_2_NONE;
        barriers[0].dstStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
        barriers[0].dstAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT;
        barriers[0].oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        barriers[0].newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        barriers[0].srcQueueFamilyIndex = barriers[0].dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        barriers[0].image = color_img;
        barriers[0].subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};

        barriers[1].sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        barriers[1].srcStageMask = VK_PIPELINE_STAGE_2_NONE;
        barriers[1].srcAccessMask = VK_ACCESS_2_NONE;
        barriers[1].dstStageMask = VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT;
        barriers[1].dstAccessMask = VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
        barriers[1].oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        barriers[1].newLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
        barriers[1].srcQueueFamilyIndex = barriers[1].dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        barriers[1].image = depth_att.image;
        barriers[1].subresourceRange = {depth_aspect, 0, 1, 0, 1};

        VkDependencyInfo dep = {};
        dep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        dep.imageMemoryBarrierCount = 2;
        dep.pImageMemoryBarriers = barriers;
        vkCmdPipelineBarrier2(cmd, &dep);
    } else {
        VkMemoryBarrier2 mem = {};
        mem.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER_2;
        mem.srcStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT;
        mem.srcAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
        mem.dstStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT;
        mem.dstAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
        VkDependencyInfo dep = {};
        dep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        dep.memoryBarrierCount = 1;
        dep.pMemoryBarriers = &mem;
        vkCmdPipelineBarrier2(cmd, &dep);
    }

    m_pass_color_formats.resize(1);
    m_pass_color_formats[0] = u32(m_output->swapchain_format);
    VkRenderingAttachmentInfo color_info = {};
    color_info.sType = VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;
    color_info.imageView = color_view;
    color_info.imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    color_info.loadOp = preserve_contents ? VK_ATTACHMENT_LOAD_OP_LOAD : VK_ATTACHMENT_LOAD_OP_CLEAR;
    color_info.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    std::memcpy(color_info.clearValue.color.float32, m_output->clear_color, sizeof(float) * 4);

    VkRenderingAttachmentInfo depth_info = {};
    depth_info.sType = VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;
    depth_info.imageView = depth_att.view;
    depth_info.imageLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
    depth_info.loadOp = preserve_contents ? VK_ATTACHMENT_LOAD_OP_LOAD : VK_ATTACHMENT_LOAD_OP_CLEAR;
    depth_info.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    depth_info.clearValue.depthStencil = {1.0f, 0};

    VkRenderingInfo rendering = {};
    rendering.sType = VK_STRUCTURE_TYPE_RENDERING_INFO;
    rendering.renderArea = {{0, 0}, m_output->swapchain_extent};
    rendering.layerCount = 1;
    rendering.colorAttachmentCount = 1;
    rendering.pColorAttachments = &color_info;
    rendering.pDepthAttachment = &depth_info;
    rendering.pStencilAttachment = depth_has_stencil ? &depth_info : nullptr;
    vkCmdBeginRendering(cmd, &rendering);

    m_pass_active = m_presentation_pass = true;
    m_pass_has_stencil = depth_has_stencil;
    m_pass_extent = m_output->swapchain_extent;
    m_pass_depth_format = m_output->depth_format;
    m_pass_samples = 1;
    set_pass_viewport();
}
void vulkan_backend::begin_render_pass(rhi::render_pass_desc const& desc) {
    if (!m_recording || m_pass_active || desc.colors.size() > 4 || (desc.colors.empty() && !desc.depth.texture))
        throw std::runtime_error("Invalid render pass boundary/attachment count");
    VkPhysicalDeviceProperties properties; vkGetPhysicalDeviceProperties(m_physical_device, &properties);
    if (desc.colors.size() > properties.limits.maxColorAttachments) throw std::runtime_error("MRT exceeds device limits");
    std::array<rhi::rhi_texture_handle, 5> unique{};
    u32 unique_count = 0;
    VkExtent2D extent = {};
    u32 samples=0;
    std::array<VkRenderingAttachmentInfo, 4> color_infos{};
    std::array<u32, 4> color_formats{};
    u32 color_count = 0;
    VkRenderingAttachmentInfo depth_info = {};
    VkFormat depth_vk_format = VK_FORMAT_UNDEFINED;
    bool depth_has_stencil = false;
    auto build_attachment = [&](rhi::rhi_texture_handle handle, rhi::attachment_load load, rhi::attachment_store store, bool is_depth, VkClearValue clear, VkFormat& fmt_out) {
        auto* t = m_textures.get(handle);
        bool const duplicate = std::find(unique.begin(), unique.begin() + unique_count, handle) != unique.begin() + unique_count;
        if (!duplicate && unique_count < unique.size()) {
            unique[unique_count++] = handle;
        }
        bool const read_only_depth = is_depth && t && t->state == rhi::resource_state::depth_read && load == rhi::attachment_load::load && store == rhi::attachment_store::store;
        if (!t || duplicate || t->description.usage != (is_depth ? rhi::texture_usage::depth_stencil : rhi::texture_usage::render_target)
            || (!read_only_depth && t->state != (is_depth ? rhi::resource_state::depth_write : rhi::resource_state::render_target)))
            throw std::runtime_error("Invalid/aliased/untransitioned render pass attachment");
        validate_attachment_ops(load, store, t->contents_valid);
        if (!extent.width) extent = {t->width, t->height};
        if (extent.width != t->width || extent.height != t->height) throw std::runtime_error("Mismatched attachment dimensions");
        if (samples && samples!=t->description.samples) throw std::runtime_error("Mismatched attachment sample counts");
        samples=t->description.samples;
        fmt_out = t->format;
        VkRenderingAttachmentInfo att = {};
        att.sType = VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;
        att.imageView = t->view;
        att.imageLayout = scope(t->state, is_depth).layout;
        att.resolveMode = VK_RESOLVE_MODE_NONE;
        att.loadOp = load_op(load);
        att.storeOp = read_only_depth ? VK_ATTACHMENT_STORE_OP_NONE : store_op(store);
        att.clearValue = clear;
        return att;
    };
    for (auto const& color : desc.colors) {
        VkClearValue clear = {}; for (unsigned i = 0; i < 4; ++i) {
            if (!std::isfinite(color.clear[i])) throw std::runtime_error("Non-finite color clear");
            clear.color.float32[i] = color.clear[i];
        }
        VkFormat fmt = VK_FORMAT_UNDEFINED;
        color_infos[color_count] = build_attachment(color.texture, color.load, color.store, false, clear, fmt);
        color_formats[color_count] = u32(fmt);
        ++color_count;
    }
    if (desc.depth.texture) {
        if (!std::isfinite(desc.depth.clear_depth) || desc.depth.clear_depth < 0 || desc.depth.clear_depth > 1 || desc.depth.clear_stencil > 255)
            throw std::runtime_error("Invalid depth/stencil clear");
        VkClearValue clear = {}; clear.depthStencil = {desc.depth.clear_depth, desc.depth.clear_stencil};
        depth_info = build_attachment(desc.depth.texture, desc.depth.load, desc.depth.store, true, clear, depth_vk_format);
        depth_has_stencil = has_stencil_aspect(depth_vk_format);
    }
    auto retained = desc;
    VkRenderingInfo rendering = {};
    rendering.sType = VK_STRUCTURE_TYPE_RENDERING_INFO;
    rendering.renderArea = {{0, 0}, extent};
    rendering.layerCount = 1;
    rendering.colorAttachmentCount = color_count;
    rendering.pColorAttachments = color_count ? color_infos.data() : nullptr;
    rendering.pDepthAttachment = desc.depth.texture ? &depth_info : nullptr;
    rendering.pStencilAttachment = depth_has_stencil ? &depth_info : nullptr;
    vkCmdBeginRendering(m_frames->command_buffers[m_frames->current_frame], &rendering);

    m_pass_active = true; m_presentation_pass = false; m_pass_has_stencil = depth_has_stencil; m_pass_extent = extent;
    m_pass_color_formats.assign(color_formats.begin(), color_formats.begin() + color_count);
    m_pass_description = std::move(retained);
    m_pass_depth_format = desc.depth.texture ? u32(depth_vk_format) : 0;
    m_pass_samples=samples;
    set_pass_viewport();
}
void vulkan_backend::end_render_pass() {
    if (!m_recording || !m_pass_active) throw std::runtime_error("No active render pass");
    vkCmdEndRendering(m_frames->command_buffers[m_frames->current_frame]);
    if (!m_presentation_pass) {
        // Without full-coverage tracking, discard-load cannot establish valid
        // whole-image contents even if some pixels were drawn.
        for (auto const& a : m_pass_description.colors) m_textures.get(a.texture)->contents_valid = a.store == rhi::attachment_store::store && a.load != rhi::attachment_load::discard;
        if (auto* t = m_textures.get(m_pass_description.depth.texture))
            t->contents_valid = m_pass_description.depth.store == rhi::attachment_store::store && m_pass_description.depth.load != rhi::attachment_load::discard;
    }
    else m_presentation_contents = true;
    m_pass_active = m_presentation_pass = m_pass_has_stencil = false;
    m_pass_description = {};
}
void vulkan_backend::resolve_texture(rhi::rhi_texture_handle source,rhi::rhi_texture_handle destination) {
    auto* src=m_textures.get(source); auto* dst=m_textures.get(destination);
    if (!m_recording || m_pass_active || !src || !dst || source==destination || !src->contents_valid
        || src->description.usage!=rhi::texture_usage::render_target || dst->description.usage!=rhi::texture_usage::render_target
        || src->description.samples<=1 || dst->description.samples!=1 || src->format!=dst->format
        || src->width!=dst->width || src->height!=dst->height)
        throw std::runtime_error("Resolve requires matching multisample/single-sample color targets outside a pass");
    transition_texture(source,rhi::resource_state::copy_src); transition_texture(destination,rhi::resource_state::copy_dst);
    VkImageResolve region={}; region.srcSubresource=region.dstSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};
    region.extent={src->width,src->height,1};
    vkCmdResolveImage(m_frames->command_buffers[m_frames->current_frame],src->image,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,dst->image,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&region);
    dst->contents_valid=true;
    transition_texture(source,rhi::resource_state::shader_resource); transition_texture(destination,rhi::resource_state::shader_resource);
}
} } }
