#include "pch.h"
#include "vulkan_sky_renderer.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
sky_renderer::sky_renderer(rhi::rhi_backend& backend) : m_backend(&backend) {
    try {
        rhi::shader_compiler compiler;
        for (auto stage : {rhi::shader_stage::vertex,rhi::shader_stage::pixel}) {
            rhi::shader_compile_options options; options.entry=stage==rhi::shader_stage::vertex ? "sky_vs" : "sky_ps";
            auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/skybox.hlsl",stage,options);
            if (!code.success) throw std::runtime_error(code.error_log);
            auto handle=backend.create_shader(stage,code.bytecode.data(),code.bytecode.size()*4,options.entry.c_str());
            if (!handle) throw std::runtime_error("Sky shader unavailable");
            (stage==rhi::shader_stage::vertex ? m_vs : m_ps)=handle;
        }
        m_constants=backend.create_buffer(rhi::buffer_type::constant,sizeof(object_constants),nullptr);
        rhi::sampler_desc sampler; sampler.address_u=sampler.address_v=rhi::sampler_address::clamp;
        m_sampler=backend.create_sampler(sampler);
        if (!m_constants || !m_sampler) throw std::runtime_error("Sky constants/sampler unavailable");
    } catch (...) { release(); throw; }
}
sky_renderer::~sky_renderer() { release(); }
void sky_renderer::release() {
    if (rhi::rhi_device::ref().get_backend()==m_backend) {
        for (auto const& entry : m_textures) m_backend->destroy_texture(entry.handle);
        m_backend->destroy_shader(m_vs); m_backend->destroy_shader(m_ps);
        m_backend->destroy_buffer(m_constants); m_backend->destroy_sampler(m_sampler);
    }
    m_textures.clear(); m_vs=m_ps=m_constants=m_sampler=0;
}
void sky_renderer::collect() {
    for (auto it=m_textures.begin();it!=m_textures.end();) {
        if (!it->data.expired()) { ++it; continue; }
        m_backend->destroy_texture(it->handle); it=m_textures.erase(it);
    }
}
void sky_renderer::prepare(std::shared_ptr<texture_data const> const& data) {
    collect();
    if (std::any_of(m_textures.begin(),m_textures.end(),[&](auto const& item) { return item.data.lock()==data; })) return;
    std::vector<rhi::texture_subresource_data> upload;
    for (u32 layer=0;layer<data->description.array_layers;++layer) for (u32 mip=0;mip<data->description.mip_levels;++mip) {
        auto const& bytes=data->mips.at(size_t(layer)*data->description.mip_levels+mip);
        upload.push_back({mip,layer,bytes.data(),bytes.size(),0});
    }
    auto handle=m_backend->create_texture(data->description,upload);
    if (!handle) throw std::runtime_error("Sky cubemap upload failed");
    try { m_textures.push_back({data,handle}); } catch (...) { m_backend->destroy_texture(handle); throw; }
}
void sky_renderer::draw(rhi::rhi_texture_handle color,rhi::rhi_texture_handle depth,sky_draw const& sky,
    math::rectangle<math::float2> const& rect,u32 width,u32 height) {
    auto entry=std::find_if(m_textures.begin(),m_textures.end(),[&](auto const& item) { return item.data.lock()==sky.texture; });
    if (entry==m_textures.end()) throw std::runtime_error("Queued sky texture was not prepared");
    auto& b=*m_backend;
    b.transition_texture(color,rhi::resource_state::render_target); b.transition_texture(depth,rhi::resource_state::depth_write);
    rhi::render_pass_desc pass; pass.colors.push_back({color}); pass.colors[0].load=rhi::attachment_load::load;
    pass.depth.texture=depth; pass.depth.load=rhi::attachment_load::load; b.begin_render_pass(pass);
    float x=rect.min.x*width,y=rect.min.y*height,w=(rect.max.x-rect.min.x)*width,h=(rect.max.y-rect.min.y)*height;
    b.set_viewport(x,y,w,h); auto left=u32(std::floor(x)),top=u32(std::floor(y));
    b.set_scissor(left,top,u32(std::ceil(x+w))-left,u32(std::ceil(y+h))-top);
    b.set_vertex_layout({}); b.set_topology(rhi::topology::triangle_list);
    b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none); b.set_depth_bias(0,0);
    b.set_depth_stencil_state(true,false,rhi::comparison_func::less_equal);
    b.set_stencil_state(true,0,255,0,rhi::comparison_func::equal);
    b.set_blend_state(false,rhi::blend::one,rhi::blend::zero,rhi::blend_op::add);
    b.bind_shader(rhi::shader_stage::vertex,m_vs); b.bind_shader(rhi::shader_stage::pixel,m_ps);
    b.update_buffer(m_constants,&sky.constants,sizeof(sky.constants)); b.bind_constant_buffer(1,m_constants);
    b.bind_texture(0,entry->handle); b.bind_sampler(0,m_sampler); b.draw(3,0); b.end_render_pass();
    b.transition_texture(color,rhi::resource_state::shader_resource); b.transition_texture(depth,rhi::resource_state::depth_read);
}
} } }
