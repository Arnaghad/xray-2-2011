#include "vulkan_backend.h"
#include <cstring>

namespace xray { namespace render { namespace vulkan {
namespace {
void check_capture(VkResult result,char const* operation) {
    if (result!=VK_SUCCESS) throw std::runtime_error(std::string(operation)+" (VkResult="+std::to_string(result)+")");
}
}
void vulkan_backend::cleanup_capture() noexcept {
    for (auto& slot : m_frames->captures) { free_buffer(slot.buffer); slot={}; }
    m_frames->capture_valid=false; m_frames->capture_enabled=false; m_frames->capture_frame=0;
}
std::array<size_t,2> vulkan_backend::capture_storage() const {
    std::array<size_t,2> result={};
    for (auto const& slot : m_frames->captures) if (slot.buffer.buffer) { ++result[0]; result[1]+=size_t(slot.buffer.size); }
    return result;
}
void vulkan_backend::record_capture(VkCommandBuffer cmd) {
    auto extent=m_output->swapchain_extent; auto format=m_output->swapchain_format;
    if (format!=VK_FORMAT_B8G8R8A8_SRGB && format!=VK_FORMAT_B8G8R8A8_UNORM
        && format!=VK_FORMAT_R8G8B8A8_SRGB && format!=VK_FORMAT_R8G8B8A8_UNORM)
        throw std::runtime_error("Unsupported test capture surface format");
    auto& slot=m_frames->captures[m_frames->current_frame]; VkDeviceSize bytes=VkDeviceSize(extent.width)*extent.height*4;
    // begin_frame already waited this slot's submission fence. Keep the other slot alive.
    if (slot.buffer.size!=bytes) {
        bool coherent=false;
        auto next=allocate_host_buffer(bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT,coherent);
        free_buffer(slot.buffer); slot.buffer=std::move(next); slot.coherent=coherent;
    }
    slot.width=extent.width; slot.height=extent.height; slot.format=format;
    VkImageMemoryBarrier2 to_transfer = {};
    to_transfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
    to_transfer.srcStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
    to_transfer.srcAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT;
    to_transfer.dstStageMask = VK_PIPELINE_STAGE_2_COPY_BIT;
    to_transfer.dstAccessMask = VK_ACCESS_2_TRANSFER_READ_BIT;
    to_transfer.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    to_transfer.newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
    to_transfer.srcQueueFamilyIndex = to_transfer.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    to_transfer.image = m_output->swapchain_images[m_output->current_image_index];
    to_transfer.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    VkDependencyInfo pre_copy = {};
    pre_copy.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    pre_copy.imageMemoryBarrierCount = 1;
    pre_copy.pImageMemoryBarriers = &to_transfer;
    vkCmdPipelineBarrier2(cmd, &pre_copy);

    VkBufferImageCopy region={}; region.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};
    region.imageExtent={extent.width,extent.height,1};
    vkCmdCopyImageToBuffer(cmd,to_transfer.image,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,slot.buffer.buffer,1,&region);

    VkBufferMemoryBarrier2 host = {};
    host.sType = VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;
    host.srcStageMask = VK_PIPELINE_STAGE_2_COPY_BIT;
    host.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
    host.dstStageMask = VK_PIPELINE_STAGE_2_HOST_BIT;
    host.dstAccessMask = VK_ACCESS_2_HOST_READ_BIT;
    host.srcQueueFamilyIndex = host.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    host.buffer = slot.buffer.buffer;
    host.size = VK_WHOLE_SIZE;

    VkImageMemoryBarrier2 to_present = {};
    to_present.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
    to_present.srcStageMask = VK_PIPELINE_STAGE_2_COPY_BIT;
    to_present.srcAccessMask = VK_ACCESS_2_TRANSFER_READ_BIT;
    to_present.dstStageMask = VK_PIPELINE_STAGE_2_NONE;
    to_present.dstAccessMask = VK_ACCESS_2_NONE;
    to_present.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
    to_present.newLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
    to_present.srcQueueFamilyIndex = to_present.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    to_present.image = to_transfer.image;
    to_present.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};

    VkDependencyInfo post_copy = {};
    post_copy.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    post_copy.bufferMemoryBarrierCount = 1;
    post_copy.pBufferMemoryBarriers = &host;
    if (!m_output->is_offscreen) {
        post_copy.imageMemoryBarrierCount = 1;
        post_copy.pImageMemoryBarriers = &to_present;
    }
    vkCmdPipelineBarrier2(cmd, &post_copy);
}
vulkan_backend::captured_frame vulkan_backend::capture_frame() {
    if (!m_initialized || !m_frames->capture_enabled || !m_frames->capture_valid || m_recording)
        throw std::runtime_error("Test capture requires a completed recorded frame and an opted-in window");
    auto const& slot=m_frames->captures[m_frames->capture_frame];
    check_capture(vkWaitForFences(m_device,1,&m_frames->in_flight[m_frames->capture_frame],VK_TRUE,30000000000ull),"Wait test capture fence");
    if (!slot.coherent) {
        VkMappedMemoryRange range={}; range.sType=VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
        range.memory=slot.buffer.memory; range.size=VK_WHOLE_SIZE;
        check_capture(vkInvalidateMappedMemoryRanges(m_device,1,&range),"Invalidate test capture memory");
    }
    captured_frame result; result.width=slot.width; result.height=slot.height;
    result.bgra.resize(size_t(slot.width)*slot.height*4); std::memcpy(result.bgra.data(),slot.buffer.mapped,result.bgra.size());
    bool rgba=slot.format==VK_FORMAT_R8G8B8A8_SRGB || slot.format==VK_FORMAT_R8G8B8A8_UNORM;
    for (size_t i=0;i<result.bgra.size();i+=4) {
        if (rgba) std::swap(result.bgra[i],result.bgra[i+2]);
        result.bgra[i+3]=0; // Normalize the unused BI_RGB byte instead of retaining desktop alpha.
    }
    return result;
}
} } }
