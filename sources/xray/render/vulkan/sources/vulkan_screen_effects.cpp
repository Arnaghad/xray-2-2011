#include "pch.h"
#include "vulkan_screen_effects.h"
#include "material_contract.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <cmath>
#include <stdexcept>
namespace xray { namespace render { namespace vulkan {
namespace {
void state(rhi::rhi_backend& b,rhi::rhi_shader_handle vs,rhi::rhi_shader_handle ps,math::rectangle<math::float2> const& rect,u32 width,u32 height){
    b.set_vertex_layout({});b.set_topology(rhi::topology::triangle_list);b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);b.set_depth_bias(0,0);
    b.set_depth_stencil_state(false,false,rhi::comparison_func::always);b.set_stencil_state(false);b.set_blend_state(false,rhi::blend::one,rhi::blend::zero,rhi::blend_op::add);
    b.bind_shader(rhi::shader_stage::vertex,vs);b.bind_shader(rhi::shader_stage::pixel,ps);
    float x=rect.min.x*width,y=rect.min.y*height,w=(rect.max.x-rect.min.x)*width,h=(rect.max.y-rect.min.y)*height;auto left=u32(std::floor(x)),top=u32(std::floor(y));
    b.set_viewport(x,y,w,h);b.set_scissor(left,top,u32(std::ceil(x+w))-left,u32(std::ceil(y+h))-top);
}
void bounds(float* result,math::rectangle<math::float2> const& rect,u32 width,u32 height){
    result[0]=(std::ceil(rect.min.x*width-.5f)+.5f)/width;result[1]=(std::ceil(rect.min.y*height-.5f)+.5f)/height;
    result[2]=(std::ceil(rect.max.x*width-.5f)-.5f)/width;result[3]=(std::ceil(rect.max.y*height-.5f)-.5f)/height;
}
}
screen_effects::screen_effects(rhi::rhi_backend& backend):m_backend(&backend){try{
    rhi::shader_compiler compiler;
    for(unsigned i=0;i<3;++i){rhi::shader_compile_options options;options.entry=i==0 ? "ssao_ps" : i==1 ? "ssao_filter_ps" : "composite_ps";
        auto code=compiler.compile_file(i==2 ? XRAY_VULKAN_ASSET_SHADER_DIR "/distortion_composite.hlsl" : XRAY_VULKAN_ASSET_SHADER_DIR "/ssao.hlsl",rhi::shader_stage::pixel,options);
        if(!code.success)throw std::runtime_error(code.error_log);m_shaders[i]=backend.create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,options.entry.c_str());
        if(!m_shaders[i])throw std::runtime_error("Screen effect shader unavailable");}
    rhi::sampler_desc sampler;sampler.address_u=sampler.address_v=rhi::sampler_address::clamp;sampler.min_filter=sampler.mag_filter=sampler.mip_filter=rhi::sampler_filter::nearest;m_nearest=backend.create_sampler(sampler);
    sampler.min_filter=sampler.mag_filter=rhi::sampler_filter::linear;m_linear=backend.create_sampler(sampler);
    m_ao=backend.create_buffer(rhi::buffer_type::constant,sizeof(screen_effect_constants),nullptr);m_composite=backend.create_buffer(rhi::buffer_type::constant,sizeof(postprocess_constants),nullptr);
    if(!m_nearest||!m_linear||!m_ao||!m_composite)throw std::runtime_error("Screen effect context unavailable");
}catch(...){release();throw;}}
screen_effects::~screen_effects(){release();}
void screen_effects::release(){if(rhi::rhi_device::ref().get_backend()==m_backend){for(auto t:m_targets)m_backend->destroy_texture(t);for(auto s:m_shaders)m_backend->destroy_shader(s);
    m_backend->destroy_sampler(m_nearest);m_backend->destroy_sampler(m_linear);m_backend->destroy_buffer(m_ao);m_backend->destroy_buffer(m_composite);}m_targets={};m_shaders={};m_nearest=m_linear=m_ao=m_composite=0;}
void screen_effects::resize(u32 width,u32 height){if(m_width==width && m_height==height)return;std::array<rhi::rhi_texture_handle,4> next={};try{
    rhi::texture_desc desc;desc.width=width;desc.height=height;desc.pixel_format=rhi::format::r16g16b16a16_float;desc.usage=rhi::texture_usage::render_target;
    for(auto& t:next){t=m_backend->create_texture(desc,{});if(!t)throw std::runtime_error("Screen effects require sampled/renderable single-sample RGBA16F targets");}
}catch(...){for(auto t:next)m_backend->destroy_texture(t);throw;}for(auto t:m_targets)m_backend->destroy_texture(t);m_targets=next;m_width=width;m_height=height;}
rhi::rhi_texture_handle screen_effects::ssao(rhi::rhi_texture_handle depth,rhi::rhi_texture_handle normal,gbuffer_camera_data const& camera,
    rhi::rhi_shader_handle vs,math::rectangle<math::float2> const& rect,u32 width,u32 height,postprocess_settings const& settings){
    resize(width,height);auto& b=*m_backend;screen_effect_constants constants={};std::copy(camera.inverse_projection.begin(),camera.inverse_projection.end(),constants.inverse_projection);
    constants.viewport[0]=rect.min.x;constants.viewport[1]=rect.min.y;constants.viewport[2]=rect.max.x-rect.min.x;constants.viewport[3]=rect.max.y-rect.min.y;bounds(constants.source_bounds,rect,width,height);
    constants.ao_parameters[0]=float(settings.ssao_pixel_radius);constants.ao_parameters[1]=settings.ssao_world_radius;constants.ao_parameters[2]=settings.ssao_bias;constants.ao_parameters[3]=settings.ssao_strength;
    constants.filter_parameters[0]=float(width);constants.filter_parameters[1]=float(height);constants.filter_parameters[2]=std::min(.1f,settings.ssao_world_radius*.1f);constants.filter_parameters[3]=.8f;
    b.update_buffer(m_ao,&constants,sizeof(constants));
    for(unsigned i=0;i<2;++i){b.transition_texture(m_targets[i],rhi::resource_state::render_target);rhi::render_pass_desc pass;pass.colors.push_back({m_targets[i]});b.begin_render_pass(pass);
        try{state(b,vs,m_shaders[i],rect,width,height);b.bind_constant_buffer(1,m_ao);b.bind_sampler(0,m_nearest);
            b.bind_texture(0,i ? m_targets[0] : depth);b.bind_texture(1,i ? depth : normal);b.bind_texture(2,normal);b.draw(3,0);
        }catch(...){b.end_render_pass();throw;}b.end_render_pass();b.transition_texture(m_targets[i],rhi::resource_state::shader_resource);}
    return m_targets[1];
}
rhi::rhi_texture_handle screen_effects::distort(rhi::rhi_texture_handle hdr,rhi::rhi_texture_handle depth,rhi::rhi_shader_handle vs,
    math::rectangle<math::float2> const& rect,u32 width,u32 height,postprocess_settings const& settings,std::function<void()> const& geometry){
    distortion_field(depth,vs,rect,width,height,geometry);auto& b=*m_backend;
    b.transition_texture(hdr,rhi::resource_state::shader_resource);b.transition_texture(m_targets[3],rhi::resource_state::render_target);
    rhi::render_pass_desc pass;pass.colors.push_back({m_targets[3]});b.begin_render_pass(pass);
    try{state(b,vs,m_shaders[2],rect,width,height);postprocess_constants constants={};bounds(constants.source_bounds,rect,width,height);constants.filter[0]=float(width);constants.filter[1]=float(height);constants.filter[2]=settings.distortion_max_pixels;
        b.update_buffer(m_composite,&constants,sizeof(constants));b.bind_constant_buffer(1,m_composite);b.bind_sampler(0,m_linear);b.bind_texture(0,hdr);b.bind_texture(1,m_targets[2]);b.draw(3,0);
    }catch(...){b.end_render_pass();throw;}b.end_render_pass();b.transition_texture(m_targets[3],rhi::resource_state::shader_resource);return m_targets[3];
}
rhi::rhi_texture_handle screen_effects::distortion_field(rhi::rhi_texture_handle depth,rhi::rhi_shader_handle vs,math::rectangle<math::float2> const& rect,u32 width,u32 height,std::function<void()> const& geometry){
    resize(width,height);auto& b=*m_backend;b.transition_texture(m_targets[2],rhi::resource_state::render_target);b.transition_texture(depth,rhi::resource_state::depth_write);
    rhi::render_pass_desc pass;pass.colors.push_back({m_targets[2]});pass.depth.texture=depth;pass.depth.load=rhi::attachment_load::load;b.begin_render_pass(pass);
    try{state(b,vs,m_shaders[2],rect,width,height);geometry();}catch(...){b.end_render_pass();throw;}b.end_render_pass();
    b.transition_texture(m_targets[2],rhi::resource_state::shader_resource);b.transition_texture(depth,rhi::resource_state::depth_read);return m_targets[2];
}
} } }
