#ifndef XRAY_VULKAN_TERRAIN_COLLISION_H_INCLUDED
#define XRAY_VULKAN_TERRAIN_COLLISION_H_INCLUDED
#include <xray/render/vulkan/terrain_resources.h>

namespace xray { namespace collision { class geometry_instance; } }
namespace xray { namespace render { namespace vulkan {
// Immutable accepted-mesh snapshot in xray_terrain_collision, not the renderer DLL.
// No scene/session or texture ownership; allocator must outlive this object.
class terrain_collision_geometry final {
    struct storage;
    std::unique_ptr<storage> m_storage;
public:
    terrain_collision_geometry(memory::base_allocator&,std::shared_ptr<terrain_cell_data const> const&);
    ~terrain_collision_geometry();
    terrain_collision_geometry(terrain_collision_geometry const&)=delete;
    terrain_collision_geometry& operator=(terrain_collision_geometry const&)=delete;
    collision::geometry_instance* instance() const;
    std::span<math::float3 const> vertices() const;
    std::span<u32 const> indices() const;
    math::float3 center_offset() const;
};
} } }
#endif
