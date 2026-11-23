#include "pch.h"
#include "vulkan_deferred_lighting.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
deferred_lighting::deferred_lighting(rhi::rhi_backend& backend,u32 samples) : m_backend(&backend),m_samples(samples) {
    try {
        auto compile=[&](pcstr entry) {
            rhi::shader_compiler compiler; rhi::shader_compile_options options; options.entry=entry;
            options.defines={"SAMPLE_COUNT="+std::to_string(m_samples)};
            auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/deferred_lighting.hlsl",rhi::shader_stage::pixel,options);
            if (!code.success) throw std::runtime_error(code.error_log);
            auto handle=backend.create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,entry);
            if (!handle) throw std::runtime_error("Deferred lighting shader creation failed"); return handle;
        };
        m_light=compile("lighting_ps"); m_combine=compile("combine_ps"); m_environment=compile("environment_ps");
        m_constants=backend.create_buffer(rhi::buffer_type::constant,sizeof(lighting_constants),nullptr);
        if (!m_constants) throw std::runtime_error("Deferred lighting constants unavailable");
    } catch (...) { release(); throw; }
}
deferred_lighting::~deferred_lighting() { release(); }
void deferred_lighting::prepare_shadows() {
    if (!m_shadow_light) {
        rhi::shader_compiler compiler; rhi::shader_compile_options options; options.entry="lighting_ps"; options.defines={"USE_SCENE_SHADOW=1"};
        options.defines.push_back("SAMPLE_COUNT="+std::to_string(m_samples));
        auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/deferred_lighting.hlsl",rhi::shader_stage::pixel,options);
        if (!code.success) throw std::runtime_error(code.error_log);
        m_shadow_light=m_backend->create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,"lighting_ps");
        if (!m_shadow_light) throw std::runtime_error("Scene shadow lighting shader unavailable");
    }
    if (!m_shadow_constants) {
        m_shadow_constants=m_backend->create_buffer(rhi::buffer_type::constant,sizeof(shadow_lighting_constants),nullptr);
        if (!m_shadow_constants) throw std::runtime_error("Scene shadow lighting constants unavailable");
    }
}
void deferred_lighting::release() {
    if (rhi::rhi_device::ref().get_backend()==m_backend) {
        for (auto target : m_targets) m_backend->destroy_texture(target);
        m_backend->destroy_shader(m_light); m_backend->destroy_shader(m_combine); m_backend->destroy_shader(m_environment); m_backend->destroy_buffer(m_constants);
        m_backend->destroy_shader(m_ao_environment);
        m_backend->destroy_shader(m_shadow_light); m_backend->destroy_buffer(m_shadow_constants);
    }
    m_targets={}; m_light=m_combine=m_environment=m_constants=0; m_width=m_height=0;
    m_shadow_light=m_shadow_constants=0;
}
rhi::rhi_texture_handle deferred_lighting::render(std::array<rhi::rhi_texture_handle,4> const& gbuffer,
    std::array<rhi::rhi_texture_handle,3> const& materials,gbuffer_camera_data const& camera,
    std::vector<scene_light> const& lights,lighting_environment const& environment,math::rectangle<math::float2> const& rect,u32 width,u32 height,
    rhi::rhi_shader_handle fullscreen_vs,rhi::rhi_sampler_handle nearest,scene_view_mode mode,std::function<shadow_binding(size_t)> const& shadows,rhi::rhi_texture_handle ao) {
    auto& b=*m_backend;
    if(ao && !m_ao_environment){
        if(m_samples!=1)throw std::runtime_error("SSAO environment requires 1x");
        rhi::shader_compiler compiler;rhi::shader_compile_options options;options.entry="environment_ps";options.defines={"SAMPLE_COUNT=1","USE_SSAO=1"};
        auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/deferred_lighting.hlsl",rhi::shader_stage::pixel,options);
        if(!code.success)throw std::runtime_error(code.error_log);m_ao_environment=b.create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,"environment_ps");
        if(!m_ao_environment)throw std::runtime_error("SSAO ambient shader unavailable");
    }
    if (m_width!=width || m_height!=height) {
        std::array<rhi::rhi_texture_handle,3> next={};
        try {
            rhi::texture_desc desc; desc.width=width; desc.height=height;
            desc.samples=m_samples;
            desc.pixel_format=rhi::format::r16g16b16a16_float; desc.usage=rhi::texture_usage::render_target;
            for (auto& target : next) { target=b.create_texture(desc,{}); if (!target) throw std::runtime_error("Deferred target resize failed"); }
        } catch (...) { for (auto target : next) b.destroy_texture(target); throw; }
        for (auto target : m_targets) b.destroy_texture(target);
        m_targets=next; m_width=width; m_height=height;
    }
    lighting_constants constants={}; std::copy(camera.inverse_projection.begin(),camera.inverse_projection.end(),constants.inverse_projection);
    constants.viewport[0]=rect.min.x; constants.viewport[1]=rect.min.y;
    constants.viewport[2]=rect.max.x-rect.min.x; constants.viewport[3]=rect.max.y-rect.min.y;
    constants.light_factors[2]=1;
    constants.fog_parameters[2]=mode==indirect_lighting_mode?1.f:0.f;
    constants.fog_parameters[3]=mode==lighting_view_mode?1.f:0.f;
    std::copy(environment.ambient.begin(),environment.ambient.end(),constants.ambient);
    std::copy(environment.fog_color.begin(),environment.fog_color.end(),constants.fog_color_distance);
    constants.fog_color_distance[3]=environment.fog_distance; constants.fog_parameters[0]=environment.fog_near;
    constants.fog_parameters[1]=environment.fog_enabled ? 1.f : 0.f;
    auto viewport=[&] {
        float x=rect.min.x*width,y=rect.min.y*height,w=constants.viewport[2]*width,h=constants.viewport[3]*height;
        b.set_viewport(x,y,w,h); auto left=u32(std::floor(x)),top=u32(std::floor(y));
        b.set_scissor(left,top,u32(std::ceil(x+w))-left,u32(std::ceil(y+h))-top);
    };
    rhi::render_pass_desc pass;
    for (unsigned i=0;i<2;++i) { b.transition_texture(m_targets[i],rhi::resource_state::render_target); pass.colors.push_back({m_targets[i]}); }
    b.begin_render_pass(pass); viewport();
    auto light_state=[&] {
        b.set_vertex_layout({}); b.set_topology(rhi::topology::triangle_list); b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);
        b.set_depth_bias(0,0);
        b.set_depth_stencil_state(false,false,rhi::comparison_func::always); b.set_stencil_state(false);
        b.bind_shader(rhi::shader_stage::vertex,fullscreen_vs); b.bind_shader(rhi::shader_stage::pixel,m_light);
        b.bind_sampler(0,nearest); b.bind_constant_buffer(1,m_constants);
        for (unsigned i=0;i<4;++i) b.bind_texture(i,gbuffer[i]);
        b.set_blend_state(true,rhi::blend::one,rhi::blend::one,rhi::blend_op::add);
    };
    light_state();
    for (size_t index=0;index<lights.size();++index) {
        auto const& light=lights[index];
        shadow_binding shadow;
        if (light.casts_shadows) {
            if (!shadows || !m_shadow_light || !m_shadow_constants) throw std::runtime_error("Scene shadows were not prepared");
            b.end_render_pass(); shadow=shadows(index);
            if (!shadow.plan || !shadow.plan->count) throw std::runtime_error("Missing scene shadow plan");
            for (unsigned i=0;i<2;++i) { b.transition_texture(m_targets[i],rhi::resource_state::render_target); pass.colors[i].load=rhi::attachment_load::load; }
            b.begin_render_pass(pass); viewport();
        }
        light_state();
        std::copy(light.direction.begin(),light.direction.end(),constants.light_direction);
        constants.light_direction[3]=light.type==scene_light_type::directional ? 0.f : light.type==scene_light_type::point ? 1.f : 2.f;
        std::copy(light.position.begin(),light.position.end(),constants.light_position_range);
        constants.light_position_range[3]=light.range;
        constants.spot_parameters[0]=light.inner_cosine; constants.spot_parameters[1]=light.outer_cosine;
        constants.spot_parameters[2]=light.spot_falloff;
        constants.spot_parameters[3]=light.shadow_depth_bias;
        std::copy(light.color.begin(),light.color.end(),constants.light_color); constants.light_color[3]=light.intensity;
        constants.light_factors[0]=light.diffuse; constants.light_factors[1]=light.specular;
        constants.light_factors[3]=light.attenuation_power;
        if (shadow.plan) {
            shadow_lighting_constants extended={}; extended.lighting=constants;
            for (unsigned i=0;i<4;++i) std::copy(shadow.plan->cascades[i].receiver.begin(),shadow.plan->cascades[i].receiver.end(),extended.matrices[i]);
            if (light.type==scene_light_type::point)
                std::copy(shadow.plan->point_receiver.begin(),shadow.plan->point_receiver.end(),extended.matrices[0]);
            std::copy(shadow.plan->splits.begin(),shadow.plan->splits.end(),extended.splits);
            extended.parameters[0]=float(shadow.plan->count); extended.parameters[1]=float(shadow_atlas_grid(shadow.plan->count));
            extended.parameters[2]=1/(extended.parameters[1]*shadow.plan->resolution); extended.parameters[3]=shadow.plan->transparency;
            b.bind_shader(rhi::shader_stage::pixel,m_shadow_light); b.bind_constant_buffer(1,m_shadow_constants);
            b.bind_texture(5,shadow.texture); b.bind_sampler(1,shadow.sampler);
            b.update_buffer(m_shadow_constants,&extended,sizeof(extended));
        } else b.update_buffer(m_constants,&constants,sizeof(constants));
        b.draw(3,0);
    }
    b.end_render_pass();
    for (unsigned i=0;i<2;++i) b.transition_texture(m_targets[i],rhi::resource_state::shader_resource);
    b.transition_texture(m_targets[2],rhi::resource_state::render_target);
    pass={}; pass.colors.push_back({m_targets[2]}); b.begin_render_pass(pass); viewport();
    b.set_blend_state(false,rhi::blend::one,rhi::blend::zero,rhi::blend_op::add);
    b.bind_shader(rhi::shader_stage::pixel,m_combine); b.bind_constant_buffer(1,m_constants); b.update_buffer(m_constants,&constants,sizeof(constants));
    b.bind_texture(0,materials[0]); b.bind_texture(1,materials[1]); b.bind_texture(2,m_targets[0]); b.bind_texture(3,m_targets[1]);
    b.draw(3,0); b.end_render_pass(); b.transition_texture(m_targets[2],rhi::resource_state::shader_resource);
    if (mode==lit_view_mode || mode==lit_with_histogram_view_mode || mode==indirect_lighting_mode) {
        // Accumulators have been consumed by combine; reuse diffuse storage for the final linear result.
        b.transition_texture(m_targets[0],rhi::resource_state::render_target);
        pass={}; pass.colors.push_back({m_targets[0]}); b.begin_render_pass(pass); viewport();
        b.bind_shader(rhi::shader_stage::pixel,ao ? m_ao_environment : m_environment);
        if(ao)b.bind_texture(4,ao);
        b.bind_texture(0,m_targets[2]); b.bind_texture(1,materials[2]); b.bind_texture(2,materials[0]); b.bind_texture(3,gbuffer[3]);
        b.draw(3,0); b.end_render_pass(); b.transition_texture(m_targets[0],rhi::resource_state::shader_resource);
        return m_targets[0];
    }
    return m_targets[mode==lighting_diffuse_view_mode ? 0 : mode==lighting_specular_view_mode ? 1 : 2];
}
} } }
