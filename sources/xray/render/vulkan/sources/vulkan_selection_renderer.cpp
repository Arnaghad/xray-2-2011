#include "pch.h"
#include "vulkan_selection_renderer.h"
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <cmath>
#include <stdexcept>
namespace xray::render::vulkan {
selection_renderer::selection_renderer(rhi::rhi_backend& value):backend(&value) {
    try {
        rhi::shader_compiler compiler;rhi::shader_compile_options options;
        for(auto [stage,entry,target]:{std::tuple{rhi::shader_stage::vertex,"selection_vs",&vertex},std::tuple{rhi::shader_stage::pixel,"selection_ps",&pixel}}) {
            options.entry=entry;auto shader=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/editor_selection.hlsl",stage,options);
            if(!shader.success)throw std::runtime_error(shader.error_log);
            *target=backend->create_shader(stage,shader.bytecode.data(),shader.bytecode.size()*4,entry);if(!*target)throw std::runtime_error("Selection compositor shader unavailable");
        }
        constants=backend->create_buffer(rhi::buffer_type::constant,sizeof(object_constants),nullptr);if(!constants)throw std::runtime_error("Selection constants unavailable");
    }catch(...){backend->destroy_shader(vertex);backend->destroy_shader(pixel);backend->destroy_buffer(constants);throw;}
}
selection_renderer::~selection_renderer(){if(rhi::rhi_device::ref().get_backend()==backend){backend->destroy_texture(mask);backend->destroy_shader(vertex);backend->destroy_shader(pixel);backend->destroy_buffer(constants);}}
void selection_renderer::draw(std::vector<model_draw> const& selected,static_renderer& models,frame_constants const& camera,editor_selection_settings const& settings,math::rectangle<math::float2> const& rect,u32 w,u32 h) {
    auto& b=*backend;
    if(width!=w || height!=h) {
        rhi::texture_desc desc;desc.width=w;desc.height=h;desc.pixel_format=rhi::format::r8g8b8a8_unorm;desc.usage=rhi::texture_usage::render_target;
        auto next=b.create_texture(desc,{});if(!next)throw std::runtime_error("Selection mask unavailable");b.destroy_texture(mask);mask=next;width=w;height=h;
    }
    b.end_render_pass();b.transition_texture(mask,rhi::resource_state::render_target);
    rhi::render_pass_desc pass;pass.colors.push_back({mask});b.begin_render_pass(pass);
    float x=rect.min.x*w,y=rect.min.y*h,vw=(rect.max.x-rect.min.x)*w,vh=(rect.max.y-rect.min.y)*h;
    auto viewport=[&]{b.set_viewport(x,y,vw,vh);auto left=u32(std::floor(x)),top=u32(std::floor(y));b.set_scissor(left,top,u32(std::ceil(x+vw))-left,u32(std::ceil(y+vh))-top);};
    viewport();models.draw_mask(selected,camera);b.end_render_pass();b.transition_texture(mask,rhi::resource_state::shader_resource);b.begin_presentation_pass(true);viewport();
    b.set_vertex_layout({});b.set_topology(rhi::topology::triangle_list);b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);
    b.set_depth_stencil_state(false,false,rhi::comparison_func::always);b.set_stencil_state(false);b.set_blend_state(true,rhi::blend::src_alpha,rhi::blend::inv_src_alpha,rhi::blend_op::add);
    b.bind_shader(rhi::shader_stage::vertex,vertex);b.bind_shader(rhi::shader_stage::pixel,pixel);b.bind_texture(0,mask);
    object_constants data={};math::color color(settings.color);
    data.tint[0]=color.get_Rf();data.tint[1]=color.get_Gf();data.tint[2]=color.get_Bf();data.tint[3]=color.get_Af()*std::sqrt(std::abs(std::cos(settings.time*settings.rate)));
    data.parameters[0]=float(w);data.parameters[1]=float(h);data.parameters[2]=settings.width;
    b.update_buffer(constants,&data,sizeof(data));b.bind_constant_buffer(1,constants);b.draw(3,0);
}
}
