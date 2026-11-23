#ifndef XRAY_VULKAN_CREATION_H_INCLUDED
#define XRAY_VULKAN_CREATION_H_INCLUDED
#include <vulkan/vulkan.h>

namespace xray { namespace render { namespace vulkan {
// Error results leave creation outputs undefined. Publish only successful handles.
template<class Handle, class Function, class... Args>
VkResult create_handle(Handle& output, Function function, Args... args) {
    Handle candidate = VK_NULL_HANDLE;
    VkResult const result = function(args..., &candidate);
    if (result == VK_SUCCESS) output = candidate;
    return result;
}
} } }
#endif
