#ifndef XRAY_VULKAN_SKY_RESOURCES_H_INCLUDED
#define XRAY_VULKAN_SKY_RESOURCES_H_INCLUDED
#include <xray/render/vulkan/material_resources.h>

namespace xray { namespace render { namespace vulkan {
struct sky_material_configuration {
    resources::unmanaged_resource_ptr texture;
    std::array<float,3> color={1,1,1}; // Linear multiplier; texture views perform sRGB decoding.
    float intensity=1,rotation=0;
};
static_assert(sizeof(sky_material_configuration)<=8*sizeof(pcvoid));
class XRAY_RENDER_VULKAN_API sky_material_resource final : public resources::unmanaged_resource {
public:
    std::shared_ptr<scene_registry> const owner;
    sky_material_configuration const data;
    sky_material_resource(std::shared_ptr<scene_registry>,sky_material_configuration const&);
    ~sky_material_resource() override;
};
} } }
#endif
