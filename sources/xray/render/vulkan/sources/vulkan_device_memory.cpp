#include "vulkan_backend.h"
#include "vulkan_creation.h"
#include "vulkan_diagnostics.h"
#include <algorithm>

namespace xray { namespace render { namespace vulkan {
VkResult vulkan_backend::allocate_memory(VkMemoryAllocateInfo const& info, VkDeviceMemory& memory) {
    VkDeviceMemory candidate=VK_NULL_HANDLE;
    auto result=create_handle(candidate, vkAllocateMemory, m_device, &info, allocation_callbacks());
    if (result!=VK_SUCCESS) return result;
    ++m_memory_statistics.allocations; ++m_memory_statistics.live_allocations;
    m_memory_statistics.live_bytes+=info.allocationSize;
    m_memory_statistics.peak_bytes=std::max(m_memory_statistics.peak_bytes,m_memory_statistics.live_bytes);
    try { m_memory_allocations.emplace(candidate,info.allocationSize); }
    catch (...) {
        vkFreeMemory(m_device,candidate,allocation_callbacks());
        ++m_memory_statistics.frees; --m_memory_statistics.live_allocations;
        m_memory_statistics.live_bytes-=info.allocationSize;
        return VK_ERROR_OUT_OF_HOST_MEMORY;
    }
    memory=candidate;
    return VK_SUCCESS;
}
void vulkan_backend::free_memory(VkDeviceMemory memory) noexcept {
    if (!memory) return;
    auto found=m_memory_allocations.find(memory);
    if (found==m_memory_allocations.end()) diagnostic("error","Untracked Vulkan device-memory release");
    else {
        m_memory_statistics.live_bytes-=found->second;
        --m_memory_statistics.live_allocations; ++m_memory_statistics.frees;
        m_memory_allocations.erase(found);
    }
    vkFreeMemory(m_device,memory,allocation_callbacks());
}
void vulkan_backend::free_buffer(vulkan_buffer_entry const& buffer) noexcept {
    if (buffer.mapped) vkUnmapMemory(m_device,buffer.memory);
    if (buffer.buffer) vkDestroyBuffer(m_device,buffer.buffer,allocation_callbacks());
    free_memory(buffer.memory);
}
void vulkan_backend::free_texture(vulkan_texture_entry const& texture) noexcept {
    if (texture.sample_view) vkDestroyImageView(m_device,texture.sample_view,allocation_callbacks());
    if (texture.view) vkDestroyImageView(m_device,texture.view,allocation_callbacks());
    if (texture.image) vkDestroyImage(m_device,texture.image,allocation_callbacks());
    free_memory(texture.memory);
}
} } }
