#ifndef XRAY_VULKAN_PARTICLE_RESOURCES_H_INCLUDED
#define XRAY_VULKAN_PARTICLE_RESOURCES_H_INCLUDED
#include <xray/render/vulkan/scene_resources.h>
#include <xray/particle/base_particle.h>
#include <xray/particle/particle_data_type.h>
#include <span>
#include <cstddef>

namespace xray { namespace render { namespace vulkan {
struct texture_data;
enum class particle_blend { alpha,additive };
struct particle_material_snapshot {
    std::shared_ptr<texture_data const> texture;
    std::array<float,4> tint;
    particle_blend blend;
};
class XRAY_RENDER_VULKAN_API particle_material_resource final : public resources::unmanaged_resource {
public:
    std::shared_ptr<scene_registry> const owner;
    resources::unmanaged_resource_ptr const source;
    particle_material_snapshot const data;
    particle_material_resource(std::shared_ptr<scene_registry>,resources::unmanaged_resource_ptr);
};
struct particle_vertex {
    std::array<float,3> position;
    std::array<float,4> color;
    std::array<float,2> uv;
    bool operator==(particle_vertex const&) const=default;
};
static_assert(sizeof(particle_vertex)==36 && offsetof(particle_vertex,color)==12 && offsetof(particle_vertex,uv)==28);
struct particle_billboard_options {
    math::float4x4 transform=math::float4x4().identity();
    math::float4 color=math::float4(1,1,1,1);
    particle::enum_particle_screen_alignment alignment=particle::particle_screen_alignment_rectangle;
    particle::enum_particle_locked_axis locked_axis=particle::particle_locked_axis_none;
    particle_blend blend=particle_blend::alpha;
    bool world_space=false,subuv=false;
};
struct particle_billboard_batch {
    std::vector<particle_vertex> vertices;
    std::vector<u16> indices;
    std::shared_ptr<texture_data const> texture;
    particle_blend blend=particle_blend::alpha;
    size_t bytes() const {return vertices.size()*sizeof(particle_vertex)+indices.size()*sizeof(u16);}
};
constexpr size_t particle_batch_limit=4096,particle_frame_batches=64,particle_frame_bytes=8u*1024u*1024u;
constexpr size_t particle_texture_bytes=64u*1024u*1024u;
// Copies existing simulation output, never ticks or retains a live particle/list.
// Camera/emitter transforms are rigid affine; sizes are full extents, rotation is radians.
XRAY_RENDER_VULKAN_API std::shared_ptr<particle_billboard_batch const> snapshot_billboards(
    std::span<particle::base_particle const>,particle_billboard_options const&,math::float4x4 const& view,
    std::shared_ptr<texture_data const>);
XRAY_RENDER_VULKAN_API std::shared_ptr<particle_billboard_batch const> snapshot_billboards(
    particle::particle_list_type const&,particle_billboard_options const&,math::float4x4 const& view,
    std::shared_ptr<texture_data const>);
XRAY_RENDER_VULKAN_API void validate_particle_batches(std::span<std::shared_ptr<particle_billboard_batch const> const>);
XRAY_RENDER_VULKAN_API void validate_particle_texture(texture_data const&);
// Shared preflight for queued instance placement and camera-dependent snapshots.
XRAY_RENDER_VULKAN_API void validate_particle_transform(math::float4x4 const&);
} } }
#endif
