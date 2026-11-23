#include "pch.h"
#include "vulkan_taa.h"
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <cmath>

namespace xray { namespace render { namespace vulkan {
namespace {
float halton(u32 index,u32 base) {
    float value=0,fraction=1;
    while (index) { fraction/=base; value+=fraction*(index%base); index/=base; }
    return value-.5f;
}
math::float4x4 matrix(float const* data) {
    math::float4x4 result;
    for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) result.elements[r][c]=data[c*4+r];
    return result;
}
void store(math::float4x4 const& matrix,float* data) {
    for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) data[r*4+c]=matrix.elements[c][r];
}
}
taa_renderer::taa_renderer(rhi::rhi_backend& backend) : m_backend(&backend) {
    m_constants=backend.create_buffer(rhi::buffer_type::constant,sizeof(temporal_constants),nullptr);
    rhi::sampler_desc sampler; sampler.address_u=sampler.address_v=rhi::sampler_address::clamp;
    sampler.mip_filter=rhi::sampler_filter::nearest; m_sampler=backend.create_sampler(sampler);
    if (!m_constants || !m_sampler) {
        backend.destroy_buffer(m_constants); backend.destroy_sampler(m_sampler);
        throw std::runtime_error("TAA resources unavailable");
    }
}
void taa_renderer::release(history& value) { for (auto target : value.targets) m_backend->destroy_texture(target); value={}; }
taa_renderer::~taa_renderer() {
    if (rhi::rhi_device::ref().get_backend()!=m_backend) return;
    for (auto& [identity,value] : m_history) release(value);
    for (auto shader : m_shaders) m_backend->destroy_shader(shader);
    m_backend->destroy_buffer(m_constants); m_backend->destroy_sampler(m_sampler);
}
taa_renderer::context taa_renderer::begin(key const& identity,u32 width,u32 height,u32 frame,u64 signature,
    frame_constants const& camera,postprocess_settings const& settings) {
    auto found=m_history.find(identity);
    if ((found==m_history.end() || found->second.frame!=frame)
        && std::count_if(m_history.begin(),m_history.end(),[frame](auto const& item) { return item.second.frame==frame; })>=8)
        throw std::runtime_error("TAA supports at most eight active view/output/viewport histories");
    auto& history=m_history[identity];
    bool camera_cut=false,projection_change=false;
    for (unsigned i=0;i<16;++i) {
        camera_cut=camera_cut || std::abs(history.camera.view[i]-camera.view[i])>.5f;
        projection_change=projection_change || history.camera.projection[i]!=camera.projection[i];
    }
    bool reset=!history.valid || history.width!=width || history.height!=height || history.samples!=settings.msaa_samples
        || history.frame+1!=frame || history.signature!=signature || history.reset!=settings.taa_reset_serial || camera_cut || projection_change;
    if (reset) { history.sequence=0; history.valid=false; }
    history.camera=camera; history.frame=frame; history.signature=signature; history.reset=settings.taa_reset_serial;
    context result; result.identity=identity; result.camera=camera;
    auto [view,output,x0,y0,x1,y1]=identity;
    auto& constants=result.constants;
    constants.viewport[0]=x0; constants.viewport[1]=y0; constants.viewport[2]=x1-x0; constants.viewport[3]=y1-y0;
    int left=std::max(0,int(std::ceil(x0*width-.5f))),top=std::max(0,int(std::ceil(y0*height-.5f)));
    int right=std::min(int(width)-1,int(std::ceil(x1*width-.5f))-1),bottom=std::min(int(height)-1,int(std::ceil(y1*height-.5f))-1);
    constants.source_bounds[0]=(left+.5f)/width; constants.source_bounds[1]=(top+.5f)/height;
    constants.source_bounds[2]=(std::max(left,right)+.5f)/width; constants.source_bounds[3]=(std::max(top,bottom)+.5f)/height;
    constants.filter[0]=1.f/width; constants.filter[1]=1.f/height;
    result.jitter_x=2*halton(history.sequence%8+1,2)/((x1-x0)*width);
    result.jitter_y=-2*halton(history.sequence%8+1,3)/((y1-y0)*height);
    auto projection=matrix(camera.projection);
    for (unsigned r=0;r<4;++r) { projection.elements[r][0]+=result.jitter_x*projection.elements[r][3]; projection.elements[r][1]+=result.jitter_y*projection.elements[r][3]; }
    store(projection,result.camera.projection); result.gbuffer=gbuffer_camera(projection);
    auto vp=math::mul4x4(matrix(camera.view),projection); math::float4x4 inverse;
    if (!math::try_invert4x4(vp,inverse)) throw std::runtime_error("TAA requires an invertible camera");
    store(inverse,constants.inverse_view_projection);
    std::copy(history.previous_vp.begin(),history.previous_vp.end(),constants.previous_view_projection);
    constants.temporal[0]=settings.taa_history_weight; constants.temporal[1]=history.valid ? 1.f : 0.f; constants.temporal[2]=.002f;
    store(vp,history.previous_vp.data()); ++history.sequence;
    if (history.width!=width || history.height!=height) {
        std::array<rhi::rhi_texture_handle,4> next={};
        try {
            rhi::texture_desc desc; desc.width=width; desc.height=height; desc.usage=rhi::texture_usage::render_target;
            for (unsigned i=0;i<4;++i) {
                desc.pixel_format=i%2 ? rhi::format::r32_float : rhi::format::r16g16b16a16_float;
                next[i]=m_backend->create_texture(desc,{}); if (!next[i]) throw std::runtime_error("TAA history allocation failed");
            }
        } catch (...) { for (auto target : next) m_backend->destroy_texture(target); throw; }
        for (auto target : history.targets) m_backend->destroy_texture(target);
        history.targets=next; history.width=width; history.height=height; history.index=0;
        history.initialized=false;
    }
    history.samples=settings.msaa_samples;
    return result;
}
rhi::rhi_texture_handle taa_renderer::render(context const& context,rhi::rhi_texture_handle scene,rhi::rhi_texture_handle depth,rhi::rhi_shader_handle vs) {
    auto& h=m_history.at(context.identity); auto& b=*m_backend;
    if (!h.initialized) {
        // First-frame descriptors must not reference undefined history contents.
        for (unsigned i=0;i<4;i+=2) {
            rhi::render_pass_desc pass;
            for (unsigned j=0;j<2;++j) { b.transition_texture(h.targets[i+j],rhi::resource_state::render_target); pass.colors.push_back({h.targets[i+j]}); }
            b.begin_render_pass(pass); b.end_render_pass();
            for (unsigned j=0;j<2;++j) b.transition_texture(h.targets[i+j],rhi::resource_state::shader_resource);
        }
        h.initialized=true;
    }
    unsigned variant=h.samples==1 ? 0 : h.samples==2 ? 1 : h.samples==4 ? 2 : 3;
    if (!m_shaders[variant]) {
        rhi::shader_compiler compiler; rhi::shader_compile_options options; options.entry="taa_ps"; options.defines={"SAMPLE_COUNT="+std::to_string(h.samples)};
        auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/taa.hlsl",rhi::shader_stage::pixel,options);
        if (!code.success) throw std::runtime_error(code.error_log);
        m_shaders[variant]=b.create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,"taa_ps");
        if (!m_shaders[variant]) throw std::runtime_error("TAA shader unavailable");
    }
    unsigned current=h.index*2,previous=(1-h.index)*2;
    rhi::render_pass_desc pass;
    for (unsigned i=0;i<2;++i) { b.transition_texture(h.targets[current+i],rhi::resource_state::render_target); pass.colors.push_back({h.targets[current+i]}); }
    b.begin_render_pass(pass);
    try {
    b.set_vertex_layout({}); b.set_topology(rhi::topology::triangle_list); b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);
    b.set_depth_bias(0,0); b.set_depth_stencil_state(false,false,rhi::comparison_func::always); b.set_stencil_state(false);
    b.set_blend_state(false,rhi::blend::one,rhi::blend::zero,rhi::blend_op::add);
    b.bind_shader(rhi::shader_stage::vertex,vs); b.bind_shader(rhi::shader_stage::pixel,m_shaders[variant]);
    b.bind_constant_buffer(1,m_constants); b.update_buffer(m_constants,&context.constants,sizeof(context.constants)); b.bind_sampler(0,m_sampler);
    b.bind_texture(0,scene); b.bind_texture(1,h.targets[previous]); b.bind_texture(2,h.targets[previous+1]); b.bind_texture(3,depth);
    auto const& c=context.constants;
    b.set_viewport(c.viewport[0]*h.width,c.viewport[1]*h.height,c.viewport[2]*h.width,c.viewport[3]*h.height);
    // These are stored pixel centers; ceil would amplify a floating-point roundtrip into a one-pixel gap.
    int left=int(std::lround(c.source_bounds[0]*h.width-.5f)),top=int(std::lround(c.source_bounds[1]*h.height-.5f));
    int right=int(std::lround(c.source_bounds[2]*h.width-.5f)),bottom=int(std::lround(c.source_bounds[3]*h.height-.5f));
    b.set_scissor(u32(left),u32(top),u32(right-left+1),u32(bottom-top+1));
    b.draw(3,0);
    } catch (...) { b.end_render_pass(); throw; }
    b.end_render_pass();
    for (unsigned i=0;i<2;++i) b.transition_texture(h.targets[current+i],rhi::resource_state::shader_resource);
    h.valid=true; h.index=1-h.index;
    return h.targets[current];
}
void taa_renderer::collect(u32 frame) {
    for (auto it=m_history.begin();it!=m_history.end();) {
        if (it->second.frame==frame) { ++it; continue; }
        release(it->second); it=m_history.erase(it);
    }
}
} } }
