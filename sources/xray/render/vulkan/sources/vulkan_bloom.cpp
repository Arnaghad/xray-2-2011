#include "pch.h"
#include "vulkan_bloom.h"
#include "material_contract.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <algorithm>
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
bloom_renderer::bloom_renderer(rhi::rhi_backend& backend) : m_backend(&backend) {
    try {
        rhi::shader_compiler compiler;
        std::array<pcstr,3> entries={"extract_ps","blur_ps","composite_ps"};
        for (size_t i=0;i<entries.size();++i) {
            rhi::shader_compile_options options; options.entry=entries[i];
            auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/bloom.hlsl",rhi::shader_stage::pixel,options);
            if (!code.success) throw std::runtime_error(code.error_log);
            m_shaders[i]=backend.create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,entries[i]);
            if (!m_shaders[i]) throw std::runtime_error("Bloom shader creation failed");
        }
        rhi::sampler_desc sampler; sampler.min_filter=sampler.mag_filter=rhi::sampler_filter::linear;
        sampler.mip_filter=rhi::sampler_filter::nearest; sampler.address_u=sampler.address_v=rhi::sampler_address::clamp;
        m_sampler=backend.create_sampler(sampler);
        m_constants=backend.create_buffer(rhi::buffer_type::constant,sizeof(postprocess_constants),nullptr);
        if (!m_sampler || !m_constants) throw std::runtime_error("Bloom resources unavailable");
    } catch (...) { release(); throw; }
}
bloom_renderer::~bloom_renderer() { release(); }
void bloom_renderer::release() {
    if (rhi::rhi_device::ref().get_backend()==m_backend) {
        for (auto target : m_targets) m_backend->destroy_texture(target);
        for (auto shader : m_shaders) m_backend->destroy_shader(shader);
        m_backend->destroy_sampler(m_sampler); m_backend->destroy_buffer(m_constants);
    }
    m_targets={}; m_shaders={}; m_sampler=m_constants=0; m_width=m_height=0;
}
rhi::rhi_texture_handle bloom_renderer::render(rhi::rhi_texture_handle scene,rhi::rhi_shader_handle vs,
    math::rectangle<math::float2> const& rect,u32 width,u32 height,postprocess_settings const& settings) {
    auto& b=*m_backend;
    // Clamp to covered pixel centers, not the output edge or a neighboring queued viewport.
    int left=std::max(0,int(std::ceil(rect.min.x*width-.5f))),top=std::max(0,int(std::ceil(rect.min.y*height-.5f)));
    int right=std::min(int(width)-1,int(std::ceil(rect.max.x*width-.5f))-1);
    int bottom=std::min(int(height)-1,int(std::ceil(rect.max.y*height-.5f))-1);
    if (right<left || bottom<top) return scene;
    if (m_width!=width || m_height!=height) {
        std::array<rhi::rhi_texture_handle,3> next={};
        try {
            rhi::texture_desc desc; desc.width=width; desc.height=height;
            desc.pixel_format=rhi::format::r16g16b16a16_float; desc.usage=rhi::texture_usage::render_target;
            for (auto& target : next) { target=b.create_texture(desc,{}); if (!target) throw std::runtime_error("Bloom target resize failed"); }
        } catch (...) { for (auto target : next) b.destroy_texture(target); throw; }
        for (auto target : m_targets) b.destroy_texture(target);
        m_targets=next; m_width=width; m_height=height;
    }
    postprocess_constants constants={};
    constants.viewport[0]=rect.min.x; constants.viewport[1]=rect.min.y;
    constants.viewport[2]=rect.max.x-rect.min.x; constants.viewport[3]=rect.max.y-rect.min.y;
    constants.source_bounds[0]=(left+.5f)/width; constants.source_bounds[1]=(top+.5f)/height;
    constants.source_bounds[2]=(right+.5f)/width; constants.source_bounds[3]=(bottom+.5f)/height;
    constants.bloom[0]=settings.bloom_threshold; constants.bloom[1]=settings.bloom_strength;
    auto pass=[&](unsigned shader,rhi::rhi_texture_handle target,rhi::rhi_texture_handle source,rhi::rhi_texture_handle glow=0) {
        b.transition_texture(target,rhi::resource_state::render_target);
        rhi::render_pass_desc desc; desc.colors.push_back({target}); b.begin_render_pass(desc);
        try {
            b.set_vertex_layout({}); b.set_topology(rhi::topology::triangle_list);
            b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none); b.set_depth_bias(0,0);
            b.set_depth_stencil_state(false,false,rhi::comparison_func::always); b.set_stencil_state(false);
            b.set_blend_state(false,rhi::blend::one,rhi::blend::zero,rhi::blend_op::add);
            b.bind_shader(rhi::shader_stage::vertex,vs); b.bind_shader(rhi::shader_stage::pixel,m_shaders[shader]);
            b.bind_sampler(0,m_sampler); b.bind_constant_buffer(1,m_constants); b.update_buffer(m_constants,&constants,sizeof(constants));
            b.bind_texture(0,source); b.bind_texture(1,glow);
            b.set_viewport(rect.min.x*width,rect.min.y*height,constants.viewport[2]*width,constants.viewport[3]*height);
            b.set_scissor(u32(left),u32(top),u32(right-left+1),u32(bottom-top+1));
            b.draw(3,0);
        } catch (...) { b.end_render_pass(); throw; }
        b.end_render_pass(); b.transition_texture(target,rhi::resource_state::shader_resource);
    };
    pass(0,m_targets[0],scene);
    constants.filter[0]=settings.bloom_radius/(4*width); pass(1,m_targets[1],m_targets[0]);
    constants.filter[0]=0; constants.filter[1]=settings.bloom_radius/(4*height); pass(1,m_targets[0],m_targets[1]);
    pass(2,m_targets[2],scene,m_targets[0]);
    return m_targets[2];
}
} } }
