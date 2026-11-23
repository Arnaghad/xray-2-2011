#ifndef XRAY_VULKAN_GBUFFER_RESOURCES_H_INCLUDED
#define XRAY_VULKAN_GBUFFER_RESOURCES_H_INCLUDED
#include <xray/render/vulkan/library_linkage.h>
#include <xray/math_float4x4.h>
#include <array>

namespace xray { namespace render { namespace vulkan {
struct gbuffer_camera_data {
    std::array<float,16> inverse_projection={};
    float near_plane = 0, far_plane = 1;
};
XRAY_RENDER_VULKAN_API gbuffer_camera_data gbuffer_camera(math::float4x4 const& projection);
XRAY_RENDER_VULKAN_API void validate_gbuffer_transform(math::float4x4 const&);
} } }
#endif
