#include "pch.h"
#include "vulkan_scene_runtime.h"
#include <xray/render/vulkan/decal_resources.h>
#include <cmath>
#include <stdexcept>
namespace xray { namespace render { namespace vulkan {
decal_material_resource::decal_material_resource(std::shared_ptr<scene_registry> state,material_configuration const& config,
    surface_material_textures const& maps,decal_material_parameters const& settings,std::string identity)
    :owner(std::move(state)),base(config),textures(maps),parameters(settings),virtual_path(std::move(identity)){
    if(!owner || !owner->accepting() || !settings.enabled)throw std::runtime_error("Closed or invalid decal material");
    for(auto const& value:{base.texture,textures.normal}){
        auto* texture=dynamic_cast<texture_resource*>(value.c_ptr());
        if(!texture || texture->owner!=owner)throw std::runtime_error("Missing/foreign decal texture");
        auto data=value==textures.normal ? texture->linear_snapshot() : texture->snapshot();
        if(data->description.type!=rhi::texture_type::type_2d || (data->description.pixel_format!=rhi::format::r8g8b8a8_unorm
            && data->description.pixel_format!=rhi::format::r8g8b8a8_srgb))throw std::runtime_error("Decals require RGBA8 textures");
    }
    if (textures.cubemap) {
        auto* cube = dynamic_cast<texture_resource*>(textures.cubemap.c_ptr());
        if (!cube || cube->owner != owner || cube->snapshot()->description.type != rhi::texture_type::type_cube)
            throw std::runtime_error("Decal reflection requires a owned cubemap texture");
    }
}
void validate_decal(decal_properties const& p,std::shared_ptr<scene_registry> const& owner){
    if(!owner || !owner->accepting() || !owner->on_owner_thread())throw std::runtime_error("Decal session/owner thread invalid");
    auto* material=dynamic_cast<decal_material_resource*>(p.material.c_ptr());
    if(!material || material->owner!=owner)throw std::runtime_error("Decal requires this world's named decal material");
    auto const& m=p.transform;
    for(auto const& row:m.elements)for(float v:row)if(!std::isfinite(v)||std::abs(v)>1e6)throw std::runtime_error("Invalid decal transform");
    if(m.i.w || m.j.w || m.k.w || m.c.w!=1)throw std::runtime_error("Decal transform must be affine");
    for(unsigned a=0;a<3;++a)for(unsigned b=0;b<3;++b){double d=0;for(unsigned c=0;c<3;++c)d+=double(m.elements[a][c])*m.elements[b][c];
        if(std::abs(d-(a==b ? 1. : 0.))>1e-4)throw std::runtime_error("Decal transform must be rigid");}
    if(((m.i.xyz()^m.j.xyz())|m.k.xyz())<0)throw std::runtime_error("Reflected decal transform");
    for(float v:{p.width_height_far_distance.x,p.width_height_far_distance.y,p.width_height_far_distance.z})
        if(!std::isfinite(v)||v<.001f||v>128)throw std::runtime_error("Decal half extents must be in [0.001,128]");
    if(p.alpha_angle!=-1 || p.clip_angle!=-1)throw std::runtime_error("Decal angle fading/clipping is unsupported");
    if(p.projection_on_speedtree_geometry || p.projection_on_skeleton_geometry || p.projection_on_particle_geometry
        || (!p.projection_on_static_geometry && !p.projection_on_terrain_geometry))throw std::runtime_error("Decals support terrain/static receivers only");
}
void scene_runtime::decal_add(scene_ptr const& target,u32 id,decal_properties const& p){
    auto& list=scene(target).models->decals;validate_decal(p,m_owner);
    if(list.size()>=16 || std::any_of(list.begin(),list.end(),[&](auto const& d){return d.id==id;}))throw std::runtime_error("Duplicate decal ID or scene exceeds 16 decals");
    list.push_back({id,p});++scene(target).temporal_revision;
}
void scene_runtime::decal_update(scene_ptr const& target,u32 id,decal_properties const& p){
    auto& list=scene(target).models->decals;validate_decal(p,m_owner);
    auto found=std::find_if(list.begin(),list.end(),[&](auto const& d){return d.id==id;});
    if(found==list.end())throw std::runtime_error("Unknown decal ID");found->properties=p;++scene(target).temporal_revision;
}
void scene_runtime::decal_remove(scene_ptr const& target,u32 id){
    if(!m_owner->on_owner_thread())throw std::runtime_error("Decal removal requires owner thread");
    auto& list=scene(target).models->decals;auto found=std::find_if(list.begin(),list.end(),[&](auto const& d){return d.id==id;});
    if(found==list.end())throw std::runtime_error("Unknown decal ID");list.erase(found);++scene(target).temporal_revision;
}
} } }
