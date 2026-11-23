#include "pch.h"
#include "vulkan_debug_renderer.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
debug_batch snapshot_debug(std::span<vertex_colored const> vertices,std::span<u16 const> indices,bool triangles,bool depth) {
    if (vertices.empty()!=indices.empty() || vertices.size()>65536 || indices.size()>196608 || indices.size()%(triangles ? 3 : 2))
        throw std::runtime_error("Invalid debug primitive count (65536 vertices / 196608 indices maximum)");
    for (auto const& vertex : vertices) for (float value : {vertex.position.x,vertex.position.y,vertex.position.z})
        if (!std::isfinite(value)) throw std::runtime_error("Debug position must be finite");
    for (auto index : indices) if (index>=vertices.size()) throw std::runtime_error("Debug index is out of range");
    debug_batch result; result.triangles=triangles; result.depth=depth;
    result.vertices.reserve(vertices.size()); result.indices.assign(indices.begin(),indices.end());
    for (auto const& vertex : vertices) {
        math::color color(vertex.color);
        result.vertices.push_back({{vertex.position.x,vertex.position.y,vertex.position.z},
            {color.get_Rf(),color.get_Gf(),color.get_Bf(),color.get_Af()}});
    }
    return result;
}
debug_renderer::debug_renderer(rhi::rhi_backend& backend) : m_backend(&backend) {
    try {
        rhi::shader_compiler compiler;
        for (auto stage : {rhi::shader_stage::vertex,rhi::shader_stage::pixel}) {
            rhi::shader_compile_options options; options.entry=stage==rhi::shader_stage::vertex ? "debug_vs" : "debug_ps";
            if (stage==rhi::shader_stage::vertex) options.flags={"-fvk-invert-y"};
            auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/debug_primitives.hlsl",stage,options);
            if (!code.success) throw std::runtime_error(code.error_log);
            auto& handle=stage==rhi::shader_stage::vertex ? m_vs : m_ps;
            handle=backend.create_shader(stage,code.bytecode.data(),code.bytecode.size()*4,options.entry.c_str());
            if (!handle) throw std::runtime_error("Debug shader creation failed");
        }
        m_frame=backend.create_buffer(rhi::buffer_type::constant,sizeof(frame_constants),nullptr);
        if (!m_frame) throw std::runtime_error("Debug constants creation failed");
    } catch (...) { release(); throw; }
}
debug_renderer::~debug_renderer() { release(); }
void debug_renderer::release() {
    if (rhi::rhi_device::ref().get_backend()==m_backend) {
        for (auto const& item : m_cache) { m_backend->destroy_buffer(item.vertices); m_backend->destroy_buffer(item.indices); }
        m_backend->destroy_buffer(m_frame); m_backend->destroy_shader(m_vs); m_backend->destroy_shader(m_ps);
    }
    m_cache.clear(); m_frame=m_vs=m_ps=0;
}
void debug_renderer::draw(std::vector<std::shared_ptr<debug_batch const>> const& batches,frame_constants const& camera,bool overlay) {
    auto& b=*m_backend;
    b.update_buffer(m_frame,&camera,sizeof(camera)); b.bind_constant_buffer(0,m_frame);
    b.bind_shader(rhi::shader_stage::vertex,m_vs); b.bind_shader(rhi::shader_stage::pixel,m_ps);
    b.set_vertex_layout({sizeof(debug_vertex),{{0,rhi::format::r32g32b32_float,0},{1,rhi::format::r32g32b32a32_float,12}}});
    b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);
    b.set_stencil_state(false,0,255,0,rhi::comparison_func::always);
    b.set_blend_state(true,rhi::blend::src_alpha,rhi::blend::inv_src_alpha,rhi::blend_op::add);
    for (auto const& batch : batches) {
        auto it=std::find_if(m_cache.begin(),m_cache.end(),[&](auto const& item) {
            return item.data->vertices==batch->vertices && item.data->indices==batch->indices;
        });
        if (it==m_cache.end()) {
            entry next; next.data=batch;
            try {
                next.vertices=b.create_buffer(rhi::buffer_type::vertex,batch->vertices.size()*sizeof(debug_vertex),batch->vertices.data());
                next.indices=b.create_buffer(rhi::buffer_type::index,batch->indices.size()*sizeof(u16),batch->indices.data());
                if (!next.vertices || !next.indices) throw std::runtime_error("Debug geometry creation failed");
                m_cache.push_back(next);
            } catch (...) { b.destroy_buffer(next.vertices); b.destroy_buffer(next.indices); throw; }
            it=std::prev(m_cache.end());
        }
        it->used=true;
        b.set_topology(batch->triangles ? rhi::topology::triangle_list : rhi::topology::line_list);
        b.set_depth_stencil_state(batch->depth && !overlay,false,rhi::comparison_func::less_equal);
        b.bind_vertex_buffer(it->vertices,0); b.bind_index_buffer(it->indices,0,rhi::index_format::uint16);
        b.draw_indexed(static_cast<u32>(batch->indices.size()),0,0);
    }
}
void debug_renderer::collect() {
    for (auto it=m_cache.begin();it!=m_cache.end();) {
        if (it->used) { ++it; continue; }
        m_backend->destroy_buffer(it->vertices); m_backend->destroy_buffer(it->indices); it=m_cache.erase(it);
    }
}
} } }
