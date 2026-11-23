#include "pch.h"
#include <xray/render/vulkan/sky_resources.h>
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
sky_material_resource::sky_material_resource(std::shared_ptr<scene_registry> state,sky_material_configuration const& config)
    : owner(std::move(state)),data(config) {
    if (!owner || !owner->accepting()) throw std::runtime_error("Sky material session is closed");
    auto* texture=dynamic_cast<texture_resource*>(data.texture.c_ptr());
    if (!texture || texture->owner!=owner || texture->snapshot()->description.type!=rhi::texture_type::type_cube)
        throw std::runtime_error("Sky material requires a cubemap texture from the same active session");
    for (float value : data.color) if (!std::isfinite(value) || value<0 || value>1)
        throw std::runtime_error("Sky color requires finite linear [0,1] channels");
    if (!std::isfinite(data.intensity) || data.intensity<0 || data.intensity>16
        || !std::isfinite(data.rotation) || std::abs(data.rotation)>10000)
        throw std::runtime_error("Invalid sky intensity [0,16] or rotation +/-10000 radians");
}
sky_material_resource::~sky_material_resource() = default;
} } }
