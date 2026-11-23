#pragma once

#if defined(USE_VULKAN_RENDERER)
#include <stdexcept>
#include <string>
namespace xray::render::vulkan {
// Reject at the producer before copying payloads or queuing render work. The
// engine world repeats the guard for callers that bypass the public facade.
[[noreturn]] inline void unsupported_legacy(char const* operation) {
    throw std::runtime_error(std::string("Vulkan unsupported operation: ")+operation+" (outside the M8 capability profile)");
}
}
#define XRAY_REJECT_VULKAN_LEGACY(operation) ::xray::render::vulkan::unsupported_legacy(#operation)
#else
#define XRAY_REJECT_VULKAN_LEGACY(operation) ((void)0)
#endif
