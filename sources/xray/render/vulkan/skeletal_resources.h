#ifndef XRAY_VULKAN_SKELETAL_RESOURCES_H_INCLUDED
#define XRAY_VULKAN_SKELETAL_RESOURCES_H_INCLUDED
#include <xray/render/vulkan/model_resources.h>
#include <span>
namespace xray { namespace render { namespace vulkan {
constexpr u32 skeletal_bone_limit=128;
struct skeletal_vertex {
    math::float3 position,normal,tangent,binormal;
    math::float2 uv;
    std::array<u16,4> bones;
    std::array<float,4> weights;
};
struct bone_bounds {
    std::array<float,3> minimum{}, maximum{};
    bool used=false;
};
struct skeletal_mesh_data {
    std::vector<skeletal_vertex> vertices;
    std::vector<u16> indices;
    std::vector<bone_bounds> bone_aabbs;
};
struct skeletal_bind_pose {
    std::vector<math::float4x4> inverse;
    std::vector<std::string> names;
};
XRAY_RENDER_VULKAN_API void validate_skeleton_pose(math::float4x4 const*,u32);
XRAY_RENDER_VULKAN_API skeletal_bind_pose load_skeletal_bind_pose(const_buffer);
XRAY_RENDER_VULKAN_API std::vector<math::float4x4> load_bind_pose(const_buffer);
XRAY_RENDER_VULKAN_API std::shared_ptr<skeletal_mesh_data const> load_skeletal_mesh(const_buffer,u32 bones);
XRAY_RENDER_VULKAN_API std::shared_ptr<static_mesh_data const> skin_skeletal_mesh(skeletal_mesh_data const&,std::span<math::float4x4 const> palette);
} } }
#endif
