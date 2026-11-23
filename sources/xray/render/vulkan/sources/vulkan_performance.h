#ifndef XRAY_RENDER_VULKAN_PERFORMANCE_INTERNAL_H_INCLUDED
#define XRAY_RENDER_VULKAN_PERFORMANCE_INTERNAL_H_INCLUDED
#include <xray/render/vulkan/performance.h>
#include <chrono>

namespace xray { namespace render { namespace vulkan {
host_memory_statistics host_memory_usage();
inline uint64_t performance_clock_ns() {
    return uint64_t(std::chrono::duration_cast<std::chrono::nanoseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count());
}
inline uint64_t timestamp_delta(uint64_t start, uint64_t end, uint32_t valid_bits) {
    if (!valid_bits || valid_bits>64) return 0;
    return (end-start) & (valid_bits==64 ? UINT64_MAX : ((uint64_t(1)<<valid_bits)-1));
}
} } }
#endif
