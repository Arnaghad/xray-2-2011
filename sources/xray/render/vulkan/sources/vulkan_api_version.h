#ifndef XRAY_VULKAN_API_VERSION_H_INCLUDED
#define XRAY_VULKAN_API_VERSION_H_INCLUDED
#include <vulkan/vulkan_core.h>

namespace xray { namespace render { namespace vulkan {
inline constexpr uint32_t required_api_version=VK_API_VERSION_1_4;
inline constexpr bool supports_required_api(uint32_t version) {
    return VK_API_VERSION_VARIANT(version)==0 && version>=required_api_version;
}
} } }
#endif
