#ifndef XRAY_VULKAN_PRESENTATION_OUTPUT_H_INCLUDED
#define XRAY_VULKAN_PRESENTATION_OUTPUT_H_INCLUDED
#include <xray/render/vulkan/api.h>
#include <xray/platform_surface.h>
#include <vector>
#include <atomic>

namespace xray { namespace render { namespace vulkan {
// Shared by the backend and its cooked output lease. GPU objects are destroyed
// only by backend shutdown/resize, after submit and presentation fences retire.
struct presentation_output {
    std::atomic<bool> close_requested{false},closed{false};
    platform::native_surface_handle surface_handle = {};
    bool is_offscreen = false;
    VkSurfaceKHR surface = VK_NULL_HANDLE;
    bool swapchain_dirty = false;
    VkSwapchainKHR swapchain = VK_NULL_HANDLE;
    VkFormat swapchain_format = VK_FORMAT_UNDEFINED;
    VkExtent2D swapchain_extent = {0,0};
    std::vector<VkImage> swapchain_images;
    std::vector<VkImageView> swapchain_image_views;
    std::vector<VkDeviceMemory> offscreen_color_memory;
    uint32_t current_image_index = 0;
    uint64_t presented_frames = 0;
    VkFormat depth_format = VK_FORMAT_UNDEFINED;
    struct depth_attachment {
        VkImage image = VK_NULL_HANDLE;
        VkDeviceMemory memory = VK_NULL_HANDLE;
        VkImageView view = VK_NULL_HANDLE;
    };
    std::vector<depth_attachment> depth_attachments;
    std::vector<VkSemaphore> render_finished_semaphores;
    std::vector<VkFence> present_fences;
    std::vector<bool> present_pending;
    std::vector<VkFence> images_in_flight;
    float clear_color[4] = {0,0,0,1};
};
} } }
#endif
