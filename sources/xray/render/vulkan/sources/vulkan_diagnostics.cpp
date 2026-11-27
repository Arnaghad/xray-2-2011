#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <mutex>
#include <xray/macro_extensions.h>
#include <xray/os_platform.h>
#if XRAY_PLATFORM_WINDOWS
#	include <xray/os_include.h>
#endif
#include "vulkan_diagnostics.h"

namespace xray { namespace render { namespace vulkan {
namespace {
std::mutex s_mutex;
diagnostics_snapshot s_snapshot;
}

diagnostics_snapshot diagnostics() {
    std::lock_guard<std::mutex> lock(s_mutex);
    return s_snapshot;
}

void reset_diagnostics() {
    std::lock_guard<std::mutex> lock(s_mutex);
    s_snapshot = {};
}

void diagnostic(char const* level, char const* format, ...) {
    char message[2048];
    va_list args;
    va_start(args, format);
    std::vsnprintf(message, sizeof(message), format, args);
    va_end(args);
    // Smoke bootstrap runs before the core logging world exists.
    std::fprintf(stderr, "[Vulkan %s] %s\n", level, message);
    std::fflush(stderr);
#if XRAY_PLATFORM_WINDOWS
    OutputDebugStringA(message);
    OutputDebugStringA("\n");
#endif
    if (std::strcmp(level, "error") == 0) {
        std::lock_guard<std::mutex> lock(s_mutex);
        std::snprintf(s_snapshot.last_error, sizeof(s_snapshot.last_error), "%s", message);
    }
}

void record_validation(bool error, char const* message) {
    {
        std::lock_guard<std::mutex> lock(s_mutex);
        if (error) ++s_snapshot.validation_errors;
        else ++s_snapshot.validation_warnings;
    }
    diagnostic(error ? "error" : "warning", "validation: %s", message);
}

void record_device(char const* name, uint32_t api_version, bool present_fences) {
    std::lock_guard<std::mutex> lock(s_mutex);
    std::snprintf(s_snapshot.gpu_name, sizeof(s_snapshot.gpu_name), "%s", name);
    s_snapshot.api_version = api_version;
    s_snapshot.present_fences_enabled = present_fences;
}
void record_validation_enabled(bool enabled) {
    std::lock_guard<std::mutex> lock(s_mutex);
    s_snapshot.validation_enabled = enabled;
}
void record_instance(uint32_t loader_version,uint32_t instance_version) {
    std::lock_guard<std::mutex> lock(s_mutex);
    s_snapshot.loader_api_version=loader_version;
    s_snapshot.instance_api_version=instance_version;
}
void record_swapchain_recreation() {
    std::lock_guard<std::mutex> lock(s_mutex);
    ++s_snapshot.swapchain_recreations;
}
void record_static_culling(static_culling_statistics const& value) {
    std::lock_guard<std::mutex> lock(s_mutex);
    s_snapshot.static_culling=value;
}
void record_texture_reload(texture_reload_statistics const& value) {
    std::lock_guard<std::mutex> lock(s_mutex); s_snapshot.texture_reload=value;
}
} } }
