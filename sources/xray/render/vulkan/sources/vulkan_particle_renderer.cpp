#include "pch.h"
#include "vulkan_particle_renderer.h"
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <cmath>
#include <cstring>
#include <stdexcept>
namespace xray { namespace render { namespace vulkan {
namespace {
bool same_geometry(particle_billboard_batch const& a,particle_billboard_batch const& b) {
    if(&a==&b)return true;
    return a.indices==b.indices && a.vertices.size()==b.vertices.size()
        && (a.vertices.empty() || !std::memcmp(a.vertices.data(),b.vertices.data(),a.vertices.size()*sizeof(particle_vertex)));
}
}
particle_renderer::particle_renderer(rhi::rhi_backend& backend) : m_backend(&backend) {
    if (rhi::rhi_device::ref().get_backend()!=&backend) throw std::runtime_error("Particle renderer requires the active RHI backend");
    try {
        rhi::shader_compiler compiler;
        for (auto stage : {rhi::shader_stage::vertex,rhi::shader_stage::pixel}) {
            rhi::shader_compile_options options;options.entry=stage==rhi::shader_stage::vertex ? "particle_vs" : "particle_ps";
            if (stage==rhi::shader_stage::vertex) options.flags={"-fvk-invert-y"};
            auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/particle_billboards.hlsl",stage,options);
            if (!code.success) throw std::runtime_error(code.error_log);
            auto& handle=stage==rhi::shader_stage::vertex ? m_vs : m_ps;
            handle=backend.create_shader(stage,code.bytecode.data(),code.bytecode.size()*4,options.entry.c_str());
            if (!handle) throw std::runtime_error("Particle shader creation failed");
        }
        m_frame=backend.create_buffer(rhi::buffer_type::constant,sizeof(frame_constants),nullptr);
        rhi::sampler_desc sampler;sampler.address_u=sampler.address_v=rhi::sampler_address::clamp;sampler.max_lod=0;
        m_sampler=backend.create_sampler(sampler);
        if (!m_frame || !m_sampler) throw std::runtime_error("Particle context creation failed");
    } catch (...) {release();throw;}
}
particle_renderer::~particle_renderer() {release();}
void particle_renderer::release() {
    if (rhi::rhi_device::ref().get_backend()==m_backend) {
        for (auto const& g : m_geometry) {m_backend->destroy_buffer(g.vertices);m_backend->destroy_buffer(g.indices);}
        for (auto const& image : m_images) m_backend->destroy_texture(image.handle);
        m_backend->destroy_shader(m_vs);m_backend->destroy_shader(m_ps);m_backend->destroy_buffer(m_frame);m_backend->destroy_sampler(m_sampler);
    }
    m_geometry.clear();m_images.clear();m_vs=m_ps=m_frame=m_sampler=0;
}
void particle_renderer::prepare(std::vector<std::shared_ptr<particle_billboard_batch const>> const& batches) {
    validate_particle_batches(batches);
    size_t geometry_bytes=0,image_bytes=0;
    std::vector<particle_billboard_batch const*> geometry_keys;std::vector<texture_data const*> image_keys;
    for (auto const& g : m_geometry) {geometry_bytes+=g.data->bytes();geometry_keys.push_back(g.data.get());}
    for (auto const& image : m_images) {for (auto const& mip : image.data->mips) image_bytes+=mip.size();image_keys.push_back(image.data.get());}
    for (auto const& batch : batches) {
        if (batch->indices.empty()) continue;
        if (std::none_of(geometry_keys.begin(),geometry_keys.end(),[&](auto key){return same_geometry(*key,*batch);})) {geometry_bytes+=batch->bytes();geometry_keys.push_back(batch.get());}
        if (std::find(image_keys.begin(),image_keys.end(),batch->texture.get())==image_keys.end()) {for (auto const& mip : batch->texture->mips) image_bytes+=mip.size();image_keys.push_back(batch->texture.get());}
    }
    // Current plus previous frame may coexist until collect; repeated prepare cannot grow indefinitely.
    if (geometry_keys.size()>2*particle_frame_batches || image_keys.size()>2*particle_frame_batches
        || geometry_bytes>2*particle_frame_bytes || image_bytes>2*particle_texture_bytes)
        throw std::runtime_error("Particle retained cache exceeds two bounded frame generations; collect is required");
    for (auto const& batch : batches) {
        if (batch->indices.empty()) continue;
        if (std::none_of(m_images.begin(),m_images.end(),[&](auto const& image) {return image.data==batch->texture;})) {
            std::vector<rhi::texture_subresource_data> upload;
            for (u32 mip=0;mip<batch->texture->mips.size();++mip) {auto const& data=batch->texture->mips[mip];upload.push_back({mip,0,data.data(),data.size(),0});}
            auto handle=m_backend->create_texture(batch->texture->description,upload);if (!handle) throw std::runtime_error("Particle texture upload failed");
            try {m_images.push_back({batch->texture,handle});} catch (...) {m_backend->destroy_texture(handle);throw;}
        }
        auto existing=std::find_if(m_geometry.begin(),m_geometry.end(),[&](auto const& g) {return same_geometry(*g.data,*batch);});
        if(existing!=m_geometry.end()) {
            // Immutable GPU bytes can serve equal snapshots while retaining the
            // current CPU snapshot, including its current texture dependency.
            existing->data=batch;continue;
        }
        geometry next;next.data=batch;
        try {
            next.vertices=m_backend->create_buffer(rhi::buffer_type::vertex,batch->vertices.size()*sizeof(particle_vertex),batch->vertices.data());
            next.indices=m_backend->create_buffer(rhi::buffer_type::index,batch->indices.size()*sizeof(u16),batch->indices.data());
            if (!next.vertices || !next.indices) throw std::runtime_error("Particle geometry upload failed");m_geometry.push_back(next);
        } catch (...) {m_backend->destroy_buffer(next.vertices);m_backend->destroy_buffer(next.indices);throw;}
    }
}
void particle_renderer::begin_frame() {for (auto& g : m_geometry) g.used=false;for (auto& image : m_images) image.used=false;}
void particle_renderer::draw(std::vector<std::shared_ptr<particle_billboard_batch const>> const& batches,frame_constants const& frame) {
    validate_particle_batches(batches);
    for (auto const& matrix : {frame.view,frame.projection}) for (unsigned i=0;i<16;++i) if (!std::isfinite(matrix[i])) throw std::runtime_error("Particle camera constants must be finite");
    for (auto const& batch : batches) if (!batch->indices.empty() && (std::none_of(m_geometry.begin(),m_geometry.end(),[&](auto const& g) {return same_geometry(*g.data,*batch);})
        || std::none_of(m_images.begin(),m_images.end(),[&](auto const& image) {return image.data==batch->texture;}))) throw std::runtime_error("Particle snapshot was not prepared");
    auto& b=*m_backend;b.update_buffer(m_frame,&frame,sizeof(frame));b.bind_constant_buffer(0,m_frame);
    b.bind_shader(rhi::shader_stage::vertex,m_vs);b.bind_shader(rhi::shader_stage::pixel,m_ps);b.bind_sampler(0,m_sampler);
    b.set_vertex_layout({sizeof(particle_vertex),{{0,rhi::format::r32g32b32_float,0},{1,rhi::format::r32g32b32a32_float,12},{2,rhi::format::r32g32_float,28}}});
    b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);b.set_depth_bias(0,0);b.set_depth_stencil_state(true,false,rhi::comparison_func::less_equal);
    b.set_stencil_state(false,0,255,0);b.set_topology(rhi::topology::triangle_list);
    for (auto const& batch : batches) {
        if (batch->indices.empty()) continue;
        auto g=std::find_if(m_geometry.begin(),m_geometry.end(),[&](auto const& g) {return same_geometry(*g.data,*batch);});
        auto image=std::find_if(m_images.begin(),m_images.end(),[&](auto const& image) {return image.data==batch->texture;});g->used=image->used=true;
        b.set_blend_state(true,rhi::blend::src_alpha,batch->blend==particle_blend::alpha ? rhi::blend::inv_src_alpha : rhi::blend::one,rhi::blend_op::add);
        b.bind_texture(0,image->handle);b.bind_vertex_buffer(g->vertices,0);b.bind_index_buffer(g->indices,0,rhi::index_format::uint16);b.draw_indexed(u32(batch->indices.size()),0,0);
    }
}
void particle_renderer::collect() {
    for (auto it=m_geometry.begin();it!=m_geometry.end();) {if (it->used) {++it;continue;}m_backend->destroy_buffer(it->vertices);m_backend->destroy_buffer(it->indices);it=m_geometry.erase(it);}
    for (auto it=m_images.begin();it!=m_images.end();) {if (it->used) {++it;continue;}m_backend->destroy_texture(it->handle);it=m_images.erase(it);}
}
} } }
