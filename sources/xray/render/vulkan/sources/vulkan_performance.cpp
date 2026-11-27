#include "vulkan_backend.h"
#include "vulkan_performance.h"
#include "vulkan_creation.h"
#include <cmath>
#include <cstring>

namespace xray { namespace render { namespace vulkan {
void vulkan_backend::enable_performance_timestamps() {
    require_output_owner();
    if (!m_initialized || m_recording || m_frame_submitted)
        throw std::runtime_error("Performance timing requires a ready backend between frames");
    if (m_performance_enabled) return;
    VkPhysicalDeviceProperties properties={};
    vkGetPhysicalDeviceProperties(m_physical_device,&properties);
    uint32_t count=0; vkGetPhysicalDeviceQueueFamilyProperties(m_physical_device,&count,nullptr);
    std::vector<VkQueueFamilyProperties> queues(count);
    vkGetPhysicalDeviceQueueFamilyProperties(m_physical_device,&count,queues.data());
    auto bits=queues.at(m_graphics_queue_family_index).timestampValidBits;
    if (!bits || bits>64 || !std::isfinite(properties.limits.timestampPeriod) || properties.limits.timestampPeriod<=0)
        throw std::runtime_error("Graphics queue does not support performance timestamps");
    m_timestamp_valid_bits=bits; m_timestamp_period=properties.limits.timestampPeriod;
    m_performance_enabled=true;
}

performance_snapshot vulkan_backend::performance() const {
    if (m_owner_thread) require_output_owner();
    performance_snapshot result;
    result.device_memory=m_memory_statistics; result.host_memory=host_memory_usage();
    result.resources=resource_counts(); result.retired_resources=retired_resource_counts();
    result.pipelines=m_pipeline_map.size(); result.pipeline_creations=m_pipeline_creations;
    result.pipeline_creation_ns=m_pipeline_creation_ns; result.device_idle_calls=m_device_idle_calls;
    result.pending_pass_objects=0;
    result.cached_pass_objects=0;result.pass_pair_creations=m_pass_pair_creations;
    result.timestamps_enabled=m_performance_enabled;
    result.timestamp_valid_bits=m_timestamp_valid_bits; result.timestamp_period_ns=m_timestamp_period;
    result.outputs.reserve(m_outputs.size());
    for (auto const& entry:m_outputs) {
        auto value=entry.frames->performance;
        value.output_id=entry.presentation->surface_handle.handle;
        value.presented_frames=entry.presentation->presented_frames;
        value.capture_enabled=entry.frames->capture_enabled;
        for (auto const& frame:entry.frames->materials) {
            value.descriptor_pools+=frame.pools.size();
            value.uniform_chunks+=frame.uniforms.size();
            for (auto const& chunk:frame.uniforms) value.uniform_bytes+=chunk.resource.size;
        }
        // Each retained pool reserves 192 sets, 128 UBO + 384 image + 128 sampler descriptors.
        value.descriptor_set_capacity=value.descriptor_pools*192;
        value.descriptor_capacity=value.descriptor_pools*640;
        result.outputs.push_back(value);
    }
    return result;
}

heap_budget_snapshot vulkan_backend::memory_heaps() const {
    require_output_owner();
    if (!m_initialized) throw std::runtime_error("Memory heap query requires an initialized backend");
    uint32_t count=0;
    auto result=vkEnumerateDeviceExtensionProperties(m_physical_device,nullptr,&count,nullptr);
    if(result!=VK_SUCCESS)throw std::runtime_error("Cannot enumerate memory-budget capability");
    std::vector<VkExtensionProperties> extensions(count);
    result=vkEnumerateDeviceExtensionProperties(m_physical_device,nullptr,&count,extensions.data());
    if(result!=VK_SUCCESS)throw std::runtime_error("Cannot read memory-budget capability");
    bool supported=false;
    for(auto const& value:extensions) if(!std::strcmp(value.extensionName,VK_EXT_MEMORY_BUDGET_EXTENSION_NAME))supported=true;
    if(!supported)return {};
    VkPhysicalDeviceMemoryBudgetPropertiesEXT budget={};budget.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MEMORY_BUDGET_PROPERTIES_EXT;
    VkPhysicalDeviceMemoryProperties2 properties={};properties.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MEMORY_PROPERTIES_2;
    properties.pNext=&budget;vkGetPhysicalDeviceMemoryProperties2(m_physical_device,&properties);
    heap_budget_snapshot snapshot;snapshot.available=true;
    for(uint32_t i=0;i<properties.memoryProperties.memoryHeapCount;++i) {
        auto const& heap=properties.memoryProperties.memoryHeaps[i];
        snapshot.heaps.push_back({heap.size,budget.heapBudget[i],budget.heapUsage[i],bool(heap.flags & VK_MEMORY_HEAP_DEVICE_LOCAL_BIT)});
    }
    return snapshot;
}

void vulkan_backend::read_frame_timestamp(u32 slot) {
    if (!m_frames->timestamp_pending[slot]) return;
    uint64_t ticks[2]={};
    // The caller already waited this slot's submission fence. No WAIT_BIT,
    // queue wait, device idle or extra synchronization is introduced here.
    auto result=vkGetQueryPoolResults(m_device,m_frames->timestamps,slot*2,2,sizeof(ticks),ticks,
        sizeof(uint64_t),VK_QUERY_RESULT_64_BIT);
    if (result!=VK_SUCCESS) throw std::runtime_error("Completed frame timestamp unavailable (VkResult="+std::to_string(result)+")");
    auto& value=m_frames->performance;
    ++value.gpu_samples; value.gpu_serial=m_frames->serials[slot];
    value.gpu_frame_ns=double(timestamp_delta(ticks[0],ticks[1],m_timestamp_valid_bits))*m_timestamp_period;
    m_frames->timestamp_pending[slot]=false;
}

void vulkan_backend::prepare_frame_timestamps() {
    if (!m_performance_enabled) return;
    if (!m_frames->timestamps) {
        VkQueryPoolCreateInfo info={}; info.sType=VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO;
        info.queryType=VK_QUERY_TYPE_TIMESTAMP; info.queryCount=MAX_FRAMES_IN_FLIGHT*2;
        auto result=create_handle(m_frames->timestamps,vkCreateQueryPool,m_device,&info,allocation_callbacks());
        if (result!=VK_SUCCESS) throw std::runtime_error("Create frame timestamp pool (VkResult="+std::to_string(result)+")");
    }
}

void vulkan_backend::begin_frame_timestamp(VkCommandBuffer command) {
    if (!m_performance_enabled) return;
    auto first=m_frames->current_frame*2;
    vkCmdResetQueryPool(command,m_frames->timestamps,first,2);
    vkCmdWriteTimestamp2(command,VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT,m_frames->timestamps,first);
}
} } }
