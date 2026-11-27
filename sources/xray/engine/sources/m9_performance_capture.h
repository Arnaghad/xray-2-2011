#pragma once
#include <xray/engine/capture_control_bus.h>
#include <xray/render/vulkan/sources/vulkan_backend.h>
#include <xray/render/vulkan/sources/vulkan_performance.h>
#include <xray/render/vulkan/diagnostics.h>
#include <xray/engine/m9_performance_protocol.h>
#include <xray/os_platform.h>
#include "m8_output_capture.h"
#if XRAY_PLATFORM_WINDOWS
#	include <xray/os_include.h>
#	include <psapi.h>
#endif
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <charconv>
#include <string>

namespace xray { namespace engine { namespace m9_performance {
struct session {
    std::filesystem::path directory;
    std::ofstream stream;
    uint64_t started=0, previous=0, warmup=0, stress=0, heap_sample_ns=0;
    render::vulkan::heap_budget_snapshot heaps;
};
inline std::unique_ptr<session> current;
inline unsigned seconds(std::string const& text,unsigned maximum) {
    unsigned value=0;auto parsed=std::from_chars(text.data(),text.data()+text.size(),value);
    if (parsed.ec!=std::errc{} || parsed.ptr!=text.data()+text.size() || !value || value>maximum)
        throw std::runtime_error("Invalid performance duration");
    return value;
}
template<class Values> inline void array(std::ostream& stream,Values const& values) {
    stream<<'[';bool separator=false;for(auto value:values) {if(separator)stream<<',';stream<<value;separator=true;}stream<<']';
}
struct process_memory_snapshot {
    uint64_t private_bytes = 0;
    uint64_t working_set_bytes = 0;
    uint64_t peak_commit_bytes = 0;
    uint64_t peak_working_set_bytes = 0;
};
inline bool query_process_memory(process_memory_snapshot& out) {
#if XRAY_PLATFORM_WINDOWS
    PROCESS_MEMORY_COUNTERS_EX memory={};memory.cb=sizeof(memory);
    using process_memory_function=BOOL(WINAPI*)(HANDLE,PPROCESS_MEMORY_COUNTERS,DWORD);
    static auto get_memory=reinterpret_cast<process_memory_function>(GetProcAddress(GetModuleHandleA("kernel32.dll"),"K32GetProcessMemoryInfo"));
    if(!get_memory || !get_memory(GetCurrentProcess(),reinterpret_cast<PPROCESS_MEMORY_COUNTERS>(&memory),sizeof(memory)))
        return false;
    out.private_bytes=memory.PrivateUsage;
    out.working_set_bytes=memory.WorkingSetSize;
    out.peak_commit_bytes=memory.PeakPagefileUsage;
    out.peak_working_set_bytes=memory.PeakWorkingSetSize;
    return true;
#else
    std::ifstream status("/proc/self/status");
    if (!status) return false;
    std::string line;
    auto parse_kb = [](std::string const& text, char const* key, uint64_t& target) {
        std::size_t const len = std::strlen(key);
        if (text.compare(0, len, key) != 0) return;
        std::size_t pos = len;
        while (pos < text.size() && (text[pos] == ' ' || text[pos] == '\t' || text[pos] == ':')) ++pos;
        uint64_t kb = 0;
        auto parsed = std::from_chars(text.data() + pos, text.data() + text.size(), kb);
        if (parsed.ec == std::errc{}) target = kb * 1024ull;
    };
    while (std::getline(status, line)) {
        parse_kb(line, "VmData", out.private_bytes);
        parse_kb(line, "VmRSS", out.working_set_bytes);
        parse_kb(line, "VmPeak", out.peak_commit_bytes);
        parse_kb(line, "VmHWM", out.peak_working_set_bytes);
    }
    return true;
#endif
}
inline void start(render::vulkan::vulkan_backend& backend,platform::native_surface_handle const& window,std::filesystem::path const& directory,char const* durations) {
    if(current)throw std::runtime_error("Duplicate performance session");
    std::string text=durations;auto comma=text.find(',');
    if(comma==std::string::npos)throw std::runtime_error("Performance duration requires warmup,stress seconds");
    unsigned warmup=seconds(text.substr(0,comma),900),stress=seconds(text.substr(comma+1),3600);
    auto value=std::make_unique<session>();value->directory=directory;
    for(auto name:{"performance.jsonl","performance-start.json","performance-complete.json"})
        if(std::filesystem::exists(directory/name))throw std::runtime_error("Performance evidence already exists");
    backend.enable_performance_timestamps();
    auto initial=backend.performance();
    auto diagnostics=render::vulkan::diagnostics();
    if(!diagnostics.validation_enabled || diagnostics.validation_errors || diagnostics.validation_warnings)
        throw std::runtime_error("Performance contract requires actual clean validation");
    value->heaps=backend.memory_heaps();
    if(!value->heaps.available)throw std::runtime_error("Performance contract requires actual memory-budget queries");
    value->stream.open(directory/"performance.jsonl");value->stream<<std::setprecision(17);
    if(!value->stream)throw std::runtime_error("Cannot open performance measurements");
    value->started=render::vulkan::performance_clock_ns();value->previous=value->started;
    value->heap_sample_ns=value->started;
    value->warmup=uint64_t(warmup)*1000000000;value->stress=uint64_t(stress)*1000000000;
    if(!capture_control_bus::set_flag(window,hold_property))throw std::runtime_error("Cannot hold performance content phase");
    std::ofstream metadata(directory/"performance-start.json");
    metadata<<std::setprecision(17);
    metadata<<"{\"version\":1,\"warmup_seconds\":"<<warmup<<",\"stress_seconds\":"<<stress
        <<",\"started_ns\":"<<value->started<<",\"outputs\":"<<initial.outputs.size()
        <<",\"timestamp_valid_bits\":"<<initial.timestamp_valid_bits<<",\"timestamp_period_ns\":"<<initial.timestamp_period_ns
        <<",\"cold_pipeline_creations\":"<<initial.pipeline_creations<<",\"cold_pipeline_creation_ns\":"<<initial.pipeline_creation_ns
        <<",\"gpu_name\":"<<std::quoted(diagnostics.gpu_name)<<",\"api_version\":"<<diagnostics.api_version
        <<",\"validation_enabled\":"<<(diagnostics.validation_enabled?"true":"false")
        <<",\"validation_errors\":"<<diagnostics.validation_errors<<",\"validation_warnings\":"<<diagnostics.validation_warnings<<"}\n";
    metadata.close();if(!metadata)throw std::runtime_error("Cannot write performance start metadata");
    current=std::move(value);
    std::cerr<<"M9 performance started: warmup="<<warmup<<" stress="<<stress<<" outputs="<<initial.outputs.size()<<'\n';
}
inline bool sample(render::vulkan::vulkan_backend& backend) {
    if(!current)return false;
    auto& value=*current;auto now=render::vulkan::performance_clock_ns();auto elapsed=now-value.started;
    auto state=backend.performance();
    process_memory_snapshot memory={};
    if(!query_process_memory(memory))
        throw std::runtime_error("Cannot query process memory");
    if(!value.heap_sample_ns || now-value.heap_sample_ns>=1000000000) {
        value.heaps=backend.memory_heaps();value.heap_sample_ns=now;
    }
    bool complete=elapsed>=value.warmup+value.stress;
    auto& out=value.stream;
    out<<"{\"elapsed_ns\":"<<elapsed<<",\"owner_interval_ns\":"<<(now-value.previous)
       <<",\"phase\":\""<<(complete?"complete":elapsed<value.warmup?"warmup":"stress")<<"\""
       <<",\"device_bytes\":"<<state.device_memory.live_bytes<<",\"device_peak_bytes\":"<<state.device_memory.peak_bytes
       <<",\"device_allocations\":"<<state.device_memory.allocations<<",\"device_frees\":"<<state.device_memory.frees
       <<",\"host_bytes\":"<<state.host_memory.live_bytes<<",\"host_allocations\":"<<state.host_memory.allocations
       <<",\"private_bytes\":"<<memory.private_bytes<<",\"working_set_bytes\":"<<memory.working_set_bytes
       <<",\"peak_commit_bytes\":"<<memory.peak_commit_bytes<<",\"peak_working_set_bytes\":"<<memory.peak_working_set_bytes
       <<",\"pipelines\":"<<state.pipelines<<",\"pipeline_creations\":"<<state.pipeline_creations
       <<",\"pipeline_creation_ns\":"<<state.pipeline_creation_ns<<",\"device_idle_calls\":"<<state.device_idle_calls
       <<",\"pending_pass_objects\":"<<state.pending_pass_objects
       <<",\"cached_pass_objects\":"<<state.cached_pass_objects<<",\"pass_pair_creations\":"<<state.pass_pair_creations
       <<",\"resources\":";array(out,state.resources);
    out<<",\"retired_resources\":";array(out,state.retired_resources);
    out<<",\"heap_sample_ns\":"<<value.heap_sample_ns<<",\"heap_budget_available\":"<<(value.heaps.available?"true":"false")<<",\"heaps\":[";
    bool separator=false;
    for(auto const& heap:value.heaps.heaps) {
        if(separator)out<<',';separator=true;
        out<<"{\"local\":"<<(heap.device_local?"true":"false")<<",\"size\":"<<heap.size
           <<",\"budget\":"<<heap.budget<<",\"usage\":"<<heap.usage<<'}';
    }
    out<<"],\"outputs\":[";separator=false;
    for(auto const& output:state.outputs) {
        if(separator)out<<',';separator=true;
        out<<"{\"id\":"<<output.output_id<<",\"presented\":"<<output.presented_frames
           <<",\"cpu_samples\":"<<output.cpu_samples<<",\"cpu_serial\":"<<output.cpu_serial<<",\"cpu_ns\":"<<output.cpu_frame_ns
           <<",\"gpu_samples\":"<<output.gpu_samples<<",\"gpu_serial\":"<<output.gpu_serial<<",\"gpu_ns\":"<<output.gpu_frame_ns
           <<",\"sets\":"<<output.descriptor_sets_allocated<<",\"writes\":"<<output.descriptors_written
           <<",\"pools\":"<<output.descriptor_pools<<",\"set_capacity\":"<<output.descriptor_set_capacity
           <<",\"descriptor_capacity\":"<<output.descriptor_capacity<<",\"uniform_chunks\":"<<output.uniform_chunks
           <<",\"uniform_bytes\":"<<output.uniform_bytes<<",\"capture\":"<<(output.capture_enabled?"true":"false")<<'}';
    }
    out<<"]}\n";if(!out)throw std::runtime_error("Cannot write performance measurement");
    value.previous=now;
    if(complete) {
        value.stream.close();if(!value.stream)throw std::runtime_error("Cannot close performance measurements");
        auto selected=backend.output();auto outputs=backend.outputs();
        for(size_t i=0;i<outputs.size();++i) {
            m8_outputs::protected_window(outputs[i]->surface_handle);backend.select_output(outputs[i]);
            m8_outputs::write_image(value.directory/(i ? "performance-final.preview.bmp" : "performance-final.main.bmp"),backend.capture_frame());
        }
        backend.select_output(selected);
        std::ofstream metadata(value.directory/"performance-complete.json");
        metadata<<"{\"version\":1,\"complete\":true,\"elapsed_ns\":"<<elapsed<<"}\n";
        metadata.close();if(!metadata)throw std::runtime_error("Cannot write performance complete metadata");
        current.reset();
        return true;
    }
    return false;
}
} } }
