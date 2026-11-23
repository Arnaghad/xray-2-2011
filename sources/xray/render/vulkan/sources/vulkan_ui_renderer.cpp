#include "pch.h"
#include "vulkan_ui_renderer.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <cmath>
#include <stdexcept>
namespace xray { namespace render { namespace vulkan {
void validate_ui(std::span<ui::vertex const> vertices,int primitives,int points,ui::draw_options const& options) {
    if ((primitives!=0 && primitives!=1) || points<0 || points>2 || (primitives==1 && points!=0))
        throw std::runtime_error("Unsupported UI primitive/point mode");
    if (vertices.size()>65536 || (!vertices.empty() && (primitives==0 ? vertices.size()%4!=0 : vertices.size()<2)))
        throw std::runtime_error("Invalid UI vertex count (65536 maximum; quads or line strip required)");
    for (auto const& vertex : vertices) {
        for (float value : {vertex.m_position.x,vertex.m_position.y,vertex.m_position.z,vertex.m_position.w,vertex.m_uv.x,vertex.m_uv.y})
            if (!std::isfinite(value)) throw std::runtime_error("UI vertex must be finite");
    }
    if (options.scissor_enabled) {
        auto const& r=options.scissor;
        for (float value : {r.min.x,r.min.y,r.max.x,r.max.y})
            if (!std::isfinite(value) || std::abs(value)>1e6f) throw std::runtime_error("UI scissor exceeds finite pixel bounds");
        if (r.min.x>r.max.x || r.min.y>r.max.y) throw std::runtime_error("UI scissor is inverted");
    }
    if (options.texture && (primitives==1 || points==2)) throw std::runtime_error("UI fill/line mode does not use a texture");
}
ui_batch snapshot_ui(std::span<ui::vertex const> vertices,int primitives,int points,ui::draw_options const& options) {
    validate_ui(vertices,primitives,points,options);
    ui_batch batch; batch.options=options; batch.lines=primitives==1; batch.mode=batch.lines ? 2 : unsigned(points);
    batch.vertices.reserve(vertices.size());
    auto linear=[](float value) { return value<=.04045f ? value/12.92f : std::pow((value+.055f)/1.055f,2.4f); };
    for (auto const& v : vertices) {
        math::color color(v.m_color);
        batch.vertices.push_back({{v.m_position.x,v.m_position.y,0},
            {linear(color.get_Rf()),linear(color.get_Gf()),linear(color.get_Bf()),color.get_Af()},{v.m_uv.x,v.m_uv.y}});
    }
    // Legacy quad order is bottom-left, top-left, bottom-right, top-right.
    for (size_t i=0;i<vertices.size();i+=batch.lines ? 1 : 4) {
        if (batch.lines) { if (i) { batch.indices.push_back(u16(i-1)); batch.indices.push_back(u16(i)); } }
        else for (unsigned offset : {0u,1u,2u,2u,1u,3u}) batch.indices.push_back(u16(i+offset));
    }
    return batch;
}
std::array<u32,4> ui_scissor(ui::draw_options const& options,std::array<float,4> const& viewport,u32 width,u32 height) {
    float x=viewport[0],y=viewport[1],w=viewport[2],h=viewport[3];
    float l=x,t=y,r=x+w,b=y+h;
    if (options.scissor_enabled) {
        l=std::max(l,x+options.scissor.min.x); t=std::max(t,y+options.scissor.min.y);
        r=std::min(r,x+options.scissor.max.x); b=std::min(b,y+options.scissor.max.y);
    }
    auto pixel=[](float p,u32 limit) { return u32(std::clamp(std::ceil(double(p)-.5),0.0,double(limit))); };
    auto left=pixel(l,width),top=pixel(t,height),right=pixel(r,width),bottom=pixel(b,height);
    return {left,top,right>left ? right-left : 0,bottom>top ? bottom-top : 0};
}
ui_renderer::ui_renderer(rhi::rhi_backend& backend) : m_backend(&backend) {
    try {
        rhi::shader_compiler compiler; rhi::shader_compile_options options;
        options.entry="ui_vs"; options.flags={"-fvk-invert-y"};
        auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/ui.hlsl",rhi::shader_stage::vertex,options);
        if (!code.success) throw std::runtime_error(code.error_log);
        m_vs=backend.create_shader(rhi::shader_stage::vertex,code.bytecode.data(),code.bytecode.size()*4,options.entry.c_str());
        m_frame=backend.create_buffer(rhi::buffer_type::constant,sizeof(frame_constants),nullptr);
        rhi::sampler_desc sampler; sampler.address_u=sampler.address_v=rhi::sampler_address::clamp; sampler.max_lod=0;
        m_sampler=backend.create_sampler(sampler);
        if (!m_vs || !m_frame || !m_sampler) throw std::runtime_error("UI context creation failed");
    } catch (...) { release(); throw; }
}
ui_renderer::~ui_renderer() { release(); }
void ui_renderer::release() {
    if (rhi::rhi_device::ref().get_backend()==m_backend) {
        for (auto const& g : m_geometry) { m_backend->destroy_buffer(g.vertices); m_backend->destroy_buffer(g.indices); }
        for (auto const& t : m_textures) m_backend->destroy_texture(t.handle);
        m_backend->destroy_shader(m_vs); for (auto shader : m_ps) m_backend->destroy_shader(shader);
        m_backend->destroy_buffer(m_frame); m_backend->destroy_sampler(m_sampler);
    }
    m_geometry.clear(); m_textures.clear(); m_vs=m_frame=m_sampler=0; m_ps={};
}
void ui_renderer::prepare(std::vector<std::shared_ptr<ui_batch const>> const& batches) {
    for (auto const& batch : batches) {
        if (!m_ps[batch->mode]) {
            rhi::shader_compiler compiler; rhi::shader_compile_options options; options.entry="ui_ps";
            options.defines={"UI_MODE="+std::to_string(batch->mode)};
            auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/ui.hlsl",rhi::shader_stage::pixel,options);
            if (!code.success) throw std::runtime_error(code.error_log);
            auto shader=m_backend->create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,options.entry.c_str());
            if (!shader) throw std::runtime_error("UI pixel shader creation failed");
            m_ps[batch->mode]=shader;
        }
        if (!batch->texture || std::any_of(m_textures.begin(),m_textures.end(),[&](auto const& t) { return t.data==batch->texture; })) continue;
        std::vector<rhi::texture_subresource_data> upload;
        for (u32 level=0;level<batch->texture->mips.size();++level) {
            auto const& mip=batch->texture->mips[level]; upload.push_back({level,0,mip.data(),mip.size(),0});
        }
        auto handle=m_backend->create_texture(batch->texture->description,upload);
        if (!handle) throw std::runtime_error("UI texture upload failed");
        try { m_textures.push_back({batch->texture,handle}); } catch (...) { m_backend->destroy_texture(handle); throw; }
    }
}
void ui_renderer::begin_frame() {
    for (auto& g : m_geometry) g.used=false;
    for (auto& t : m_textures) t.used=false;
}
void ui_renderer::draw(std::vector<std::shared_ptr<ui_batch const>> const& batches,std::array<float,4> const& viewport,u32 width,u32 height) {
    auto& b=*m_backend; if (viewport[2]<=0 || viewport[3]<=0) return;
    frame_constants frame={}; frame.view[0]=1/viewport[2]; frame.view[1]=1/viewport[3]; frame.projection[15]=1;
    b.update_buffer(m_frame,&frame,sizeof(frame)); b.bind_constant_buffer(0,m_frame);
    b.bind_shader(rhi::shader_stage::vertex,m_vs); b.bind_sampler(0,m_sampler);
    b.set_vertex_layout({sizeof(ui_vertex),{{0,rhi::format::r32g32b32_float,0},{1,rhi::format::r32g32b32a32_float,12},{2,rhi::format::r32g32_float,28}}});
    b.set_viewport(viewport[0],viewport[1],viewport[2],viewport[3]);
    b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none); b.set_depth_bias(0,0);
    b.set_depth_stencil_state(false,false,rhi::comparison_func::always); b.set_stencil_state(false,0,255,0);
    b.set_blend_state(true,rhi::blend::src_alpha,rhi::blend::inv_src_alpha,rhi::blend_op::add);
    for (auto const& batch : batches) {
        auto clip=ui_scissor(batch->options,viewport,width,height); if (!clip[2] || !clip[3]) continue;
        auto it=std::find_if(m_geometry.begin(),m_geometry.end(),[&](auto const& g) {
            return g.data->vertices==batch->vertices && g.data->indices==batch->indices;
        });
        if (it==m_geometry.end()) {
            geometry next; auto data=std::make_shared<ui_batch>();
            data->vertices=batch->vertices; data->indices=batch->indices; next.data=std::move(data);
            try {
                next.vertices=b.create_buffer(rhi::buffer_type::vertex,batch->vertices.size()*sizeof(ui_vertex),batch->vertices.data());
                next.indices=b.create_buffer(rhi::buffer_type::index,batch->indices.size()*sizeof(u16),batch->indices.data());
                if (!next.vertices || !next.indices) throw std::runtime_error("UI geometry creation failed");
                m_geometry.push_back(next);
            } catch (...) { b.destroy_buffer(next.vertices); b.destroy_buffer(next.indices); throw; }
            it=std::prev(m_geometry.end());
        }
        it->used=true;
        if (batch->texture) {
            auto t=std::find_if(m_textures.begin(),m_textures.end(),[&](auto const& t) { return t.data==batch->texture; });
            if (t==m_textures.end()) throw std::runtime_error("UI texture was not prepared before recording");
            t->used=true; b.bind_texture(0,t->handle);
        } else b.bind_texture(0,0);
        b.bind_shader(rhi::shader_stage::pixel,m_ps[batch->mode]);
        b.set_scissor(clip[0],clip[1],clip[2],clip[3]); b.set_topology(batch->lines ? rhi::topology::line_list : rhi::topology::triangle_list);
        b.bind_vertex_buffer(it->vertices,0); b.bind_index_buffer(it->indices,0,rhi::index_format::uint16);
        b.draw_indexed(u32(batch->indices.size()),0,0);
    }
}
void ui_renderer::collect() {
    for (auto it=m_geometry.begin();it!=m_geometry.end();) {
        if (it->used) { ++it; continue; }
        m_backend->destroy_buffer(it->vertices); m_backend->destroy_buffer(it->indices); it=m_geometry.erase(it);
    }
    for (auto it=m_textures.begin();it!=m_textures.end();) {
        if (it->used) { ++it; continue; }
        m_backend->destroy_texture(it->handle); it=m_textures.erase(it);
    }
}
} } }
