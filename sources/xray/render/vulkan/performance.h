#ifndef XRAY_RENDER_VULKAN_PERFORMANCE_H_INCLUDED
#define XRAY_RENDER_VULKAN_PERFORMANCE_H_INCLUDED

#include <cstdint>
#include <cstddef>
#include <array>
#include <vector>

namespace xray { namespace render { namespace vulkan {

// Requested Vulkan memory sizes, including staging/retired allocations; excludes
// implementation-owned swapchain images and physical residency/heap budget.
struct device_memory_statistics {
    uint64_t live_bytes=0, peak_bytes=0, live_allocations=0, allocations=0, frees=0;
};
// Process-wide VkAllocationCallbacks requests. Excludes allocator alignment
// headers, engine/STL allocations and driver allocations outside the callbacks.
struct host_memory_statistics {
    uint64_t live_bytes=0, peak_bytes=0, live_allocations=0, allocations=0, frees=0;
};
struct output_performance_statistics {
    uintptr_t output_id=0;
    uint64_t presented_frames=0, cpu_samples=0, cpu_serial=0, cpu_frame_ns=0;
    uint64_t gpu_samples=0, gpu_serial=0;
    double gpu_frame_ns=0;
    uint64_t descriptor_sets_allocated=0, descriptors_written=0;
    uint64_t descriptor_pools=0, descriptor_set_capacity=0, descriptor_capacity=0;
    uint64_t uniform_chunks=0, uniform_bytes=0;
    bool capture_enabled=false;
};
struct performance_snapshot {
    device_memory_statistics device_memory;
    host_memory_statistics host_memory;
    std::array<size_t,5> resources={}, retired_resources={};
    uint64_t pipelines=0, pipeline_creations=0, pipeline_creation_ns=0;
    uint64_t device_idle_calls=0, pending_pass_objects=0;
    uint64_t cached_pass_objects=0, pass_pair_creations=0;
    bool timestamps_enabled=false;
    uint32_t timestamp_valid_bits=0;
    float timestamp_period_ns=0;
    std::vector<output_performance_statistics> outputs;
};
struct heap_memory_statistics {
    uint64_t size=0, budget=0, usage=0;
    bool device_local=false;
};
struct heap_budget_snapshot {
    bool available=false;
    std::vector<heap_memory_statistics> heaps;
};

} } }
#endif
