#include "pch.h"
#include "vulkan_gbuffer_renderer.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <cmath>
#include <stdexcept>
#include <xray/render/vulkan/editor_view_modes.h>

namespace xray { namespace render { namespace vulkan {
namespace {
rhi::rhi_shader_handle compile_shader(rhi::rhi_backend& backend,rhi::shader_stage stage,char const* entry) {
    rhi::shader_compiler compiler; rhi::shader_compile_options options; options.entry=entry;
    auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/gbuffer_preview.hlsl",stage,options);
    if (!code.success) throw std::runtime_error(code.error_log);
    auto handle=backend.create_shader(stage,code.bytecode.data(),code.bytecode.size()*4,entry);
    if (!handle) throw std::runtime_error("GBuffer preview shader creation failed");
    return handle;
}
}
gbuffer_renderer::gbuffer_renderer(rhi::rhi_backend& backend) : m_backend(&backend) {
    try {
        m_vs=compile_shader(backend,rhi::shader_stage::vertex,"preview_vs");
        m_ps=compile_shader(backend,rhi::shader_stage::pixel,"preview_ps");
        rhi::sampler_desc sampler; sampler.min_filter=sampler.mag_filter=sampler.mip_filter=rhi::sampler_filter::nearest;
        sampler.address_u=sampler.address_v=rhi::sampler_address::clamp;
        m_sampler=backend.create_sampler(sampler);
        m_constants=backend.create_buffer(rhi::buffer_type::constant,sizeof(mapped_surface_object_constants),nullptr);
        if (!m_sampler || !m_constants) throw std::runtime_error("GBuffer preview resources unavailable");
    } catch (...) { release(); throw; }
}
gbuffer_renderer::~gbuffer_renderer() { release(); }
void gbuffer_renderer::prepare(scene_view_mode mode,bool shadows) {
    if (lighting_mode(mode) && !m_lighting) m_lighting=std::make_unique<deferred_lighting>(*m_backend);
    if (shadows) m_lighting->prepare_shadows();
    if (mode==gbuffer_stencil_view_mode && !m_stencil_ps)
        m_stencil_ps=compile_shader(*m_backend,rhi::shader_stage::pixel,"stencil_ps");
}
void gbuffer_renderer::release() {
    m_effects.reset();
    m_decals.reset();
    m_fxaa.reset();
    m_bloom.reset();
    m_sky.reset();
    m_lighting.reset();
    if (rhi::rhi_device::ref().get_backend()==m_backend) {
        for (auto t : m_targets) m_backend->destroy_texture(t);
        for (auto t : m_material_targets) m_backend->destroy_texture(t);
        m_backend->destroy_texture(m_resolved); m_resolved=0;
        m_backend->destroy_shader(m_vs); m_backend->destroy_shader(m_ps); m_backend->destroy_shader(m_stencil_ps);
        m_backend->destroy_sampler(m_sampler); m_backend->destroy_buffer(m_constants);
    }
    m_targets={}; m_material_targets={}; m_vs=m_ps=m_stencil_ps=m_sampler=m_constants=0; m_width=m_height=0;
}
void gbuffer_renderer::prepare_sky(std::shared_ptr<texture_data const> const& data) {
    if (!m_sky) m_sky=std::make_unique<sky_renderer>(*m_backend);
    m_sky->prepare(data);
}
void gbuffer_renderer::begin(u32 width,u32 height,u32 samples) {
    m_decals_applied=false;
    auto& b=*m_backend;
    if (m_width!=width || m_height!=height || m_samples!=samples) {
        std::array<rhi::rhi_texture_handle,4> next={};
        try {
            std::array<rhi::format,4> formats={rhi::format::r16g16b16a16_unorm,rhi::format::r16g16b16a16_float,
                rhi::format::r8g8b8a8_unorm,rhi::format::d24_s8_unorm};
            for (unsigned i=0;i<4;++i) {
                rhi::texture_desc desc; desc.width=width; desc.height=height; desc.pixel_format=formats[i];
                desc.samples=samples;
                desc.usage=i==3 ? rhi::texture_usage::depth_stencil : rhi::texture_usage::render_target;
                next[i]=b.create_texture(desc,{}); if (!next[i]) throw std::runtime_error("GBuffer target resize failed");
            }
        } catch (...) { for (auto t : next) b.destroy_texture(t); throw; }
        for (auto t : m_targets) b.destroy_texture(t);
        for (auto t : m_material_targets) b.destroy_texture(t);
        m_material_targets={};
        b.destroy_texture(m_resolved); m_resolved=0;
        if (m_samples!=samples) m_lighting.reset();
        m_samples=samples;
        m_targets=next; m_width=width; m_height=height;
    }
    b.end_render_pass();
    rhi::render_pass_desc pass;
    for (unsigned i=0;i<3;++i) {
        b.transition_texture(m_targets[i],rhi::resource_state::render_target);
        pass.colors.push_back({m_targets[i]});
    }
    pass.colors[0].clear[0]=1; pass.colors[0].clear[2]=pass.colors[0].clear[3]=1;
    pass.colors[1].clear[3]=1;
    b.transition_texture(m_targets[3],rhi::resource_state::depth_write); pass.depth.texture=m_targets[3];
    b.begin_render_pass(pass);
}
void gbuffer_renderer::begin_material() {
    auto& b=*m_backend; b.end_render_pass();
    if (!m_material_targets[0]) {
        std::array<rhi::rhi_texture_handle,3> next={};
        try {
            rhi::texture_desc desc; desc.width=m_width; desc.height=m_height;
            desc.samples=m_samples;
            desc.pixel_format=rhi::format::r16g16b16a16_float; desc.usage=rhi::texture_usage::render_target;
            for (auto& target : next) {
                target=b.create_texture(desc,{}); if (!target) throw std::runtime_error("Material target creation failed");
            }
        } catch (...) { for (auto target : next) b.destroy_texture(target); throw; }
        m_material_targets=next;
    }
    rhi::render_pass_desc pass;
    for (auto target : m_material_targets) {
        b.transition_texture(target,rhi::resource_state::render_target); pass.colors.push_back({target});
    }
    b.transition_texture(m_targets[3],rhi::resource_state::depth_write);
    pass.depth.texture=m_targets[3]; pass.depth.load=rhi::attachment_load::load;
    b.begin_render_pass(pass);
}
void gbuffer_renderer::prepare_decals(std::vector<decal_draw> const& draws){
    if(!m_decals)m_decals=std::make_unique<decal_renderer>(*m_backend);m_decals->prepare(draws);
}
void gbuffer_renderer::draw_decals(std::vector<decal_draw> const& draws,gbuffer_camera_data const& camera,math::float4x4 const& view,
    math::rectangle<math::float2> const& rect,u32 width,u32 height){
    if(m_samples!=1)throw std::runtime_error("Decals require single-sample GBuffer");
    m_decals->render(draws,camera,view,rect,width,height,m_vs,m_targets[0],m_targets[1],m_material_targets[0]);
    m_decals_applied=true;
}
void gbuffer_renderer::finish(scene_view_mode mode,gbuffer_camera_data const& camera,math::rectangle<math::float2> const& rect,u32 width,u32 height,
    std::vector<scene_light> const& lights,lighting_environment const& environment,std::function<shadow_binding(size_t)> const& shadows,sky_draw const& sky,std::function<void(rhi::rhi_texture_handle,u32)> const& forward,postprocess_settings const& postprocess,
    std::function<rhi::rhi_texture_handle(rhi::rhi_texture_handle,rhi::rhi_texture_handle,rhi::rhi_shader_handle)> const& temporal,std::function<void()> const& distortion) {
    auto& b=*m_backend; if(!m_decals_applied)b.end_render_pass();
    if (mode==gbuffer_stencil_view_mode) {
        // Reuse the packed-depth color target only in this diagnostic mode.
        // LOAD the scene's actual stencil; never synthesize a mask from color/depth.
        b.transition_texture(m_targets[0],rhi::resource_state::render_target);
        b.transition_texture(m_targets[3],rhi::resource_state::depth_write);
        rhi::render_pass_desc mask; mask.colors.push_back({m_targets[0]});
        mask.depth.texture=m_targets[3]; mask.depth.load=rhi::attachment_load::load;
        b.begin_render_pass(mask);
        b.set_vertex_layout({}); b.set_topology(rhi::topology::triangle_list);
        b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);
        b.set_depth_stencil_state(false,false,rhi::comparison_func::always);
        b.set_stencil_state(true,0x82,255,0,rhi::comparison_func::equal);
        b.set_blend_state(false,rhi::blend::one,rhi::blend::zero,rhi::blend_op::add);
        b.bind_shader(rhi::shader_stage::vertex,m_vs); b.bind_shader(rhi::shader_stage::pixel,m_stencil_ps);
        b.bind_texture(0,0);
        b.draw(3,0); b.end_render_pass();
    }
    for (unsigned i=0;i<4;++i) b.transition_texture(m_targets[i],i==3 ? rhi::resource_state::depth_read : rhi::resource_state::shader_resource);
    if (mode==material_albedo_view_mode || mode==material_specular_view_mode || editor_material_mode(mode) || lighting_mode(mode))
        for (auto target : m_material_targets) b.transition_texture(target,rhi::resource_state::shader_resource);
    if (lighting_mode(mode)) {
        if (!m_lighting) m_lighting=std::make_unique<deferred_lighting>(b,m_samples);
        if (std::any_of(lights.begin(),lights.end(),[](auto const& light) { return light.casts_shadows; })) m_lighting->prepare_shadows();
    }
    bool effects=mode==lit_view_mode && postprocess.enabled;
    auto ao=((effects && postprocess.ssao_enabled) || mode==ambient_occlusion_only_view_mode || mode==unlit_with_ao_view_mode) ? m_effects->ssao(m_targets[3],m_targets[1],camera,m_vs,rect,width,height,postprocess) : 0;
    auto distortion_image=mode==distortion_only_view_mode ? m_effects->distortion_field(m_targets[3],m_vs,rect,width,height,distortion?distortion:std::function<void()>{[] {}}) : 0;
    auto lit=lighting_mode(mode) ? m_lighting->render(m_targets,m_material_targets,camera,lights,environment,rect,width,height,m_vs,m_sampler,mode,shadows,ao) : 0;
    if (mode==lit_view_mode && sky.texture) m_sky->draw(lit,m_targets[3],sky,rect,width,height);
    if ((mode==lit_view_mode || mode==lit_with_histogram_view_mode) && forward) {
        b.transition_texture(lit,rhi::resource_state::render_target);
        rhi::render_pass_desc pass; pass.colors.push_back({lit}); pass.colors[0].load=rhi::attachment_load::load;
        pass.depth.texture=m_targets[3]; pass.depth.load=rhi::attachment_load::load; pass.depth.store=rhi::attachment_store::store;
        b.begin_render_pass(pass);
        float x=rect.min.x*width,y=rect.min.y*height,w=(rect.max.x-rect.min.x)*width,h=(rect.max.y-rect.min.y)*height;
        b.set_viewport(x,y,w,h); auto left=u32(std::floor(x)),top=u32(std::floor(y));
        b.set_scissor(left,top,u32(std::ceil(x+w))-left,u32(std::ceil(y+h))-top);
        forward(m_targets[3],m_samples); b.end_render_pass();
        b.transition_texture(lit,rhi::resource_state::shader_resource);
    }
    if(effects && postprocess.distortion_enabled && distortion)lit=m_effects->distort(lit,m_targets[3],m_vs,rect,width,height,postprocess,distortion);
    if (m_samples>1 && mode==lit_view_mode) {
        if (!m_resolved) {
            rhi::texture_desc desc; desc.width=width; desc.height=height;
            desc.pixel_format=rhi::format::r16g16b16a16_float; desc.usage=rhi::texture_usage::render_target;
            m_resolved=b.create_texture(desc,{}); if (!m_resolved) throw std::runtime_error("MSAA resolve target unavailable");
        }
        b.resolve_texture(lit,m_resolved); lit=m_resolved;
    }
    if (temporal) lit=temporal(lit,m_targets[3],m_vs);
    if (mode==lit_view_mode && postprocess.enabled && postprocess.bloom_enabled && postprocess.bloom_strength>0) {
        if (!m_bloom) m_bloom=std::make_unique<bloom_renderer>(b);
        lit=m_bloom->render(lit,m_vs,rect,width,height,postprocess);
    }
    bool fxaa=mode==lit_view_mode && postprocess.enabled && postprocess.fxaa_enabled;
    if (fxaa) {
        if (!m_fxaa) m_fxaa=std::make_unique<fxaa_renderer>(b);
        lit=m_fxaa->render(lit,m_vs,rect,width,height,postprocess);
    }
    b.begin_presentation_pass(true);
    b.set_vertex_layout({}); b.set_topology(rhi::topology::triangle_list);
    b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);
    b.set_depth_stencil_state(false,false,rhi::comparison_func::always); b.set_stencil_state(false);
    b.set_blend_state(false,rhi::blend::one,rhi::blend::zero,rhi::blend_op::add);
    b.bind_shader(rhi::shader_stage::vertex,m_vs); b.bind_shader(rhi::shader_stage::pixel,m_ps); b.bind_sampler(0,m_sampler);
    auto preview=[&](unsigned channel,float x,float y,float w,float h) {
        mapped_surface_object_constants extended={};auto& constants=extended.scalar.object;std::copy(camera.inverse_projection.begin(),camera.inverse_projection.end(),constants.world);
        constants.tint[0]=rect.max.x-rect.min.x; constants.tint[1]=rect.max.y-rect.min.y;
        constants.tint[2]=std::exp2(postprocess.exposure_ev);
        constants.tint[3]=(mode==lit_view_mode || mode==lit_with_histogram_view_mode) && postprocess.enabled && !fxaa ? 1.f : 0.f;
        constants.parameters[0]=float(channel); constants.parameters[1]=camera.far_plane;
        constants.parameters[2]=rect.min.x; constants.parameters[3]=rect.min.y;
        b.update_buffer(m_constants,&extended,sizeof(extended)); b.bind_constant_buffer(1,m_constants);
        unsigned target=channel==4 ? 3 : channel==5 || channel==6 ? 0 : channel==7 ? 1 : channel;
        auto image=channel==10 || channel==18 ? lit : channel==11 ? m_material_targets[1] : channel==12 ? m_targets[0] : channel==13 ? m_material_targets[2]
            : channel==14 ? ao : channel==15 ? m_material_targets[0] : channel==16 ? distortion_image : channel>=8 ? m_material_targets[channel-8] : m_targets[target];
        b.bind_texture(0,image);b.bind_texture(1,channel==15?ao:image);
        b.set_viewport(x,y,w,h);
        auto left=u32(std::floor(x)),top=u32(std::floor(y));
        b.set_scissor(left,top,u32(std::ceil(x+w))-left,u32(std::ceil(y+h))-top);
        b.draw(3,0);
    };
    float x=rect.min.x*width,y=rect.min.y*height,w=(rect.max.x-rect.min.x)*width,h=(rect.max.y-rect.min.y)*height;
    if (mode==gbuffer_overview_view_mode) {
        for (unsigned i=0;i<4;++i) preview(i,x+(i%2)*w/2,y+(i/2)*h/2,w/2,h/2);
    } else {
        unsigned channel=mode==normals_view_mode ? 1 : mode==gbuffer_tangents_view_mode ? 2 : mode==gbuffer_hardware_depth_view_mode ? 3
            : mode==gbuffer_reconstructed_position_view_mode ? 4 : mode==gbuffer_stencil_view_mode ? 5
            : mode==gbuffer_roughness_view_mode ? 6 : mode==gbuffer_fresnel_view_mode ? 7
            : mode==material_albedo_view_mode ? 8 : mode==material_specular_view_mode ? 9 : lighting_mode(mode) ? 10 : 0;
        if(mode==specular_intencity_view_mode)channel=11;
        else if(mode==specular_power_view_mode)channel=12;
        else if(mode==emissive_only_view_mode)channel=13;
        else if(mode==ambient_occlusion_only_view_mode)channel=14;
        else if(mode==unlit_with_ao_view_mode)channel=15;
        else if(mode==distortion_only_view_mode)channel=16;
        else if(mode==lit_with_histogram_view_mode)channel=18;
        preview(channel,x,y,w,h);
    }
}
} } }
