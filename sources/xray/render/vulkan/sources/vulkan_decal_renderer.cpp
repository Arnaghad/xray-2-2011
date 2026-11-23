#include "pch.h"
#include "vulkan_decal_renderer.h"
#include "material_contract.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <cmath>
#include <stdexcept>
namespace xray { namespace render { namespace vulkan {
decal_renderer::decal_renderer(rhi::rhi_backend& backend):m_backend(&backend){try{
    rhi::shader_compiler compiler;rhi::shader_compile_options options;options.entry="decal_ps";
    options.defines={std::string("USE_BINDLESS=")+(backend.supports_bindless()?"1":"0")};
    auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/decals.hlsl",rhi::shader_stage::pixel,options);
    if(!code.success)throw std::runtime_error(code.error_log);
    m_ps=backend.create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,"decal_ps");
    m_constants=backend.create_buffer(rhi::buffer_type::constant,sizeof(decal_constants),nullptr);
    rhi::sampler_desc sampler;sampler.address_u=sampler.address_v=rhi::sampler_address::clamp;sampler.max_lod=0;
    m_sampler=backend.create_sampler(sampler);
    if(!m_ps || !m_constants || !m_sampler)throw std::runtime_error("Decal context creation failed");
}catch(...){release();throw;}}
decal_renderer::~decal_renderer(){release();}
void decal_renderer::release(){if(rhi::rhi_device::ref().get_backend()==m_backend){
    for(auto target:m_scratch)m_backend->destroy_texture(target);
    for(auto const& image:m_images)m_backend->destroy_texture(image.handle);
    m_backend->destroy_shader(m_ps);m_backend->destroy_buffer(m_constants);m_backend->destroy_sampler(m_sampler);}
    m_scratch={};m_images.clear();m_ps=m_constants=m_sampler=0;
}
void decal_renderer::prepare(std::vector<decal_draw> const& draws){
    size_t bytes=0;std::vector<std::shared_ptr<texture_data const>> images;
    for(auto const& entry:m_images)if(auto data=entry.data.lock())images.push_back(std::move(data));
    for(auto const& draw:draws)for(auto const& data:{draw.diffuse,draw.normal,draw.cubemap})
        if(data && std::find(images.begin(),images.end(),data)==images.end())images.push_back(data);
    for(auto const& data:images)for(auto const& mip:data->mips)bytes+=mip.size();
    if(images.size()>128 || bytes>128u*1024u*1024u)throw std::runtime_error("Retained decal images exceed 128 textures / 128 MiB; collect required");
    for(auto const& draw:draws)for(auto const& data:{draw.diffuse,draw.normal,draw.cubemap}){
        if(!data || std::any_of(m_images.begin(),m_images.end(),[&](auto const& image){return image.data.lock()==data;}))continue;
        std::vector<rhi::texture_subresource_data> upload;
        u32 const layers=std::max(1u,data->description.array_layers);
        u32 const mips=std::max(1u,data->description.mip_levels);
        for(u32 l=0;l<layers;++l)for(u32 i=0;i<mips;++i){
            size_t idx=size_t(l)*mips+i;
            upload.push_back({i,l,data->mips[idx].data(),data->mips[idx].size(),0});
        }
        auto handle=m_backend->create_texture(data->description,upload);if(!handle)throw std::runtime_error("Decal texture upload failed");
        try{m_images.push_back({data,handle});}catch(...){m_backend->destroy_texture(handle);throw;}
    }
}
void decal_renderer::collect(){for(auto it=m_images.begin();it!=m_images.end();)if(!it->used || it->data.expired()){
    m_backend->destroy_texture(it->handle);it=m_images.erase(it);}else {it->used=false;++it;}}
void decal_renderer::render(std::vector<decal_draw> const& draws,gbuffer_camera_data const& camera,math::float4x4 const& view,
    math::rectangle<math::float2> const& rect,u32 width,u32 height,rhi::rhi_shader_handle vs,
    rhi::rhi_texture_handle packed,rhi::rhi_texture_handle& normal,rhi::rhi_texture_handle& albedo){
    auto& b=*m_backend;b.end_render_pass();
    if(m_width!=width || m_height!=height){std::array<rhi::rhi_texture_handle,2> next={};try{
        rhi::texture_desc desc;desc.width=width;desc.height=height;desc.pixel_format=rhi::format::r16g16b16a16_float;desc.usage=rhi::texture_usage::render_target;
        for(auto& target:next){target=b.create_texture(desc,{});if(!target)throw std::runtime_error("Decal scratch resize failed");}
    }catch(...){for(auto target:next)b.destroy_texture(target);throw;}
        for(auto target:m_scratch)b.destroy_texture(target);m_scratch=next;m_width=width;m_height=height;}
    math::float4x4 inverse_view;if(!inverse_view.try_invert(view))throw std::runtime_error("Invalid decal camera");
    for(auto const& draw:draws){auto const& p=draw.properties;auto const& material=*static_cast<decal_material_resource*>(p.material.c_ptr());
        math::float4x4 inverse;if(!inverse.try_invert(p.transform))throw std::runtime_error("Invalid decal projector");
        auto local=math::mul4x4(inverse_view,inverse);decal_constants constants={};
        std::copy(camera.inverse_projection.begin(),camera.inverse_projection.end(),constants.inverse_projection);
        for(unsigned r=0;r<4;++r)for(unsigned c=0;c<4;++c)constants.view_to_decal[r*4+c]=local.elements[c][r];
        constants.viewport[0]=rect.min.x;constants.viewport[1]=rect.min.y;constants.viewport[2]=rect.max.x-rect.min.x;constants.viewport[3]=rect.max.y-rect.min.y;
        constants.extents[0]=p.width_height_far_distance.x;constants.extents[1]=p.width_height_far_distance.y;constants.extents[2]=p.width_height_far_distance.z;constants.extents[3]=camera.far_plane;
        std::copy(material.base.tint.begin(),material.base.tint.end(),constants.tint);
        std::copy(material.parameters.normal_multiplier.begin(),material.parameters.normal_multiplier.end(),constants.normal_parameters);
        constants.normal_parameters[3]=(p.projection_on_static_geometry ? 1 : 0)+(p.projection_on_terrain_geometry ? 2 : 0)+(material.parameters.blend_geometry_normals ? 4 : 0);
        u32 cube_idx=0;
        if(draw.cubemap && material.textures.reflection.enabled){
            auto found=std::find_if(m_images.begin(),m_images.end(),[&](auto const& image){return image.data.lock()==draw.cubemap;});
            if(found!=m_images.end()){found->used=true;cube_idx=b.register_bindless_texture(found->handle);}
        }
        float cube_idx_f=0;std::memcpy(&cube_idx_f,&cube_idx,4);
        constants.reflection_parameters[0]=material.textures.reflection.min_angle;
        constants.reflection_parameters[1]=material.textures.reflection.enabled?material.textures.reflection.power:0.f;
        constants.reflection_parameters[2]=cube_idx?1.f:0.f;
        constants.bindless_indices[0]=cube_idx_f;
        for(auto target:{packed,normal,albedo})b.transition_texture(target,rhi::resource_state::shader_resource);
        rhi::render_pass_desc pass;for(auto target:m_scratch){b.transition_texture(target,rhi::resource_state::render_target);pass.colors.push_back({target});}b.begin_render_pass(pass);
        try{
            b.set_vertex_layout({});b.set_topology(rhi::topology::triangle_list);b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);b.set_depth_bias(0,0);
            b.set_depth_stencil_state(false,false,rhi::comparison_func::always);b.set_stencil_state(false);
            b.set_blend_state(false,rhi::blend::one,rhi::blend::zero,rhi::blend_op::add);
            b.bind_shader(rhi::shader_stage::vertex,vs);b.bind_shader(rhi::shader_stage::pixel,m_ps);b.bind_sampler(0,m_sampler);
            b.update_buffer(m_constants,&constants,sizeof(constants));b.bind_constant_buffer(1,m_constants);
            b.bind_texture(0,normal);b.bind_texture(1,albedo);b.bind_texture(4,packed);
            unsigned slot=2;for(auto const& data:{draw.diffuse,draw.normal}){
                auto found=std::find_if(m_images.begin(),m_images.end(),[&](auto const& image){return image.data.lock()==data;});
                if(found==m_images.end())throw std::runtime_error("Decal texture not prepared");found->used=true;b.bind_texture(slot++,found->handle);}
            float x=rect.min.x*width,y=rect.min.y*height,w=constants.viewport[2]*width,h=constants.viewport[3]*height;
            b.set_viewport(x,y,w,h);auto left=u32(std::floor(x)),top=u32(std::floor(y));b.set_scissor(left,top,u32(std::ceil(x+w))-left,u32(std::ceil(y+h))-top);b.draw(3,0);
        }catch(...){b.end_render_pass();throw;}
        b.end_render_pass();std::swap(normal,m_scratch[0]);std::swap(albedo,m_scratch[1]);
    }
}
} } }
