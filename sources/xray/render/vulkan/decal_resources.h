#ifndef XRAY_VULKAN_DECAL_RESOURCES_H_INCLUDED
#define XRAY_VULKAN_DECAL_RESOURCES_H_INCLUDED
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/facade/decal_properties.h>
namespace xray { namespace render { namespace vulkan {
class XRAY_RENDER_VULKAN_API decal_material_resource final : public resources::unmanaged_resource {
public:
    std::shared_ptr<scene_registry> const owner;
    material_configuration const base;
    surface_material_textures const textures;
    decal_material_parameters const parameters;
    std::string const virtual_path;
    decal_material_resource(std::shared_ptr<scene_registry>,material_configuration const&,surface_material_textures const&,decal_material_parameters const&,std::string virtual_path={});
};
struct scene_decal { u32 id; decal_properties properties; };
struct decal_draw {
    decal_properties properties;
    std::shared_ptr<texture_data const> diffuse,normal,cubemap;
};
XRAY_RENDER_VULKAN_API void validate_decal(decal_properties const&,std::shared_ptr<scene_registry> const&);
} } }
#endif
