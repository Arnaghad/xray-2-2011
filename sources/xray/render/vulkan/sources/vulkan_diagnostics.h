#ifndef XRAY_RENDER_VULKAN_DIAGNOSTICS_INTERNAL_H_INCLUDED
#define XRAY_RENDER_VULKAN_DIAGNOSTICS_INTERNAL_H_INCLUDED

#include <xray/render/vulkan/diagnostics.h>

namespace xray { namespace render { namespace vulkan {
void reset_diagnostics();
void diagnostic(char const* level, char const* format, ...);
void record_validation(bool error, char const* message);
void record_device(char const* name, uint32_t api_version, bool present_fences);
void record_instance(uint32_t loader_version,uint32_t instance_version);
void record_validation_enabled(bool enabled);
void record_swapchain_recreation();
void record_static_culling(static_culling_statistics const&);
void record_texture_reload(texture_reload_statistics const&);
} } }
#endif
