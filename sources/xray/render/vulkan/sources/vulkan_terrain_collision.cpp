#include "pch.h"
#include <xray/render/vulkan/terrain_collision.h>
#include <xray/collision/api.h>
#include <xray/collision/geometry_instance.h>
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
struct terrain_collision_geometry::storage {
    memory::base_allocator& allocator;
    std::vector<math::float3> vertices;
    std::vector<u32> indices;
    collision::geometry* mesh=nullptr;
    collision::geometry_instance* instance=nullptr;
    math::float3 center;
    explicit storage(memory::base_allocator& value) : allocator(value) {}
    ~storage() {
        collision::delete_geometry_instance(&allocator,instance);
        collision::delete_geometry(&allocator,mesh);
    }
};
terrain_collision_geometry::terrain_collision_geometry(memory::base_allocator& allocator,
    std::shared_ptr<terrain_cell_data const> const& cell) : m_storage(std::make_unique<storage>(allocator)) {
    if (!cell || !cell->mesh || cell->row_size<2 || cell->row_size>256
        || cell->mesh->vertices.size()!=size_t(cell->row_size)*cell->row_size
        || cell->mesh->indices.size()!=size_t(cell->row_size-1)*(cell->row_size-1)*6)
        throw std::runtime_error("Terrain collision requires an accepted 2..256 grid snapshot");
    auto& data=*m_storage;
    data.vertices.reserve(cell->mesh->vertices.size());
    for (auto const& vertex : cell->mesh->vertices) {
        auto const& p=vertex.position;
        if (!std::isfinite(p[0]) || !std::isfinite(p[1]) || !std::isfinite(p[2]))
            throw std::runtime_error("Terrain collision requires finite vertices");
        data.vertices.emplace_back(p[0],p[1],p[2]);
    }
    data.indices.assign(cell->mesh->indices.begin(),cell->mesh->indices.end());
    for (u32 index : data.indices) if (index>=data.vertices.size())
        throw std::runtime_error("Terrain collision index outside grid");
    auto minimum=data.vertices.front(),maximum=minimum;
    for (auto const& p : data.vertices) for (unsigned c=0;c<3;++c) {
        minimum[c]=std::min(minimum[c],p[c]);maximum[c]=std::max(maximum[c],p[c]);
    }
    data.center=(minimum+maximum)*.5f;
    data.mesh=&*collision::new_triangle_mesh_geometry(&allocator,data.vertices.data(),u32(data.vertices.size()),
        data.indices.data(),u32(data.indices.size()));
    data.instance=&*collision::new_triangle_mesh_geometry_instance(&allocator,math::float4x4().identity(),data.mesh,false);
}
terrain_collision_geometry::~terrain_collision_geometry()=default;
collision::geometry_instance* terrain_collision_geometry::instance() const { return m_storage->instance; }
std::span<math::float3 const> terrain_collision_geometry::vertices() const { return m_storage->vertices; }
std::span<u32 const> terrain_collision_geometry::indices() const { return m_storage->indices; }
math::float3 terrain_collision_geometry::center_offset() const { return m_storage->center; }
} } }
