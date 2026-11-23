#ifndef XRAY_RENDER_VULKAN_DIAGNOSTICS_H_INCLUDED
#define XRAY_RENDER_VULKAN_DIAGNOSTICS_H_INCLUDED

#include <xray/render/vulkan/library_linkage.h>
#include <cstdint>

namespace xray { namespace render { namespace vulkan {

struct static_culling_statistics {
    uint64_t models_tested=0,models_culled=0;
    uint64_t surfaces_tested=0,surfaces_culled=0,surfaces_hidden=0,surfaces_submitted=0;
};
struct texture_reload_statistics {
    uint64_t requested=0,reloaded=0,unchanged=0,failed=0,cancelled=0,pending=0;
};
struct diagnostics_snapshot {
    char gpu_name[256] = {};
    char last_error[512] = {};
    uint32_t api_version = 0;
    uint32_t loader_api_version = 0;
    uint32_t instance_api_version = 0;
    uint32_t validation_errors = 0;
    uint32_t validation_warnings = 0;
    uint32_t swapchain_recreations = 0;
    bool validation_enabled = false;
    bool present_fences_enabled = false;
    static_culling_statistics static_culling; // Last successful draw_scene queue, not GPU completion.
    texture_reload_statistics texture_reload; // Current resource session, CPU query completion only.
};

XRAY_RENDER_VULKAN_API diagnostics_snapshot diagnostics();

} } }
#endif
