#include "pch.h"
#include <xray/physics/collision_shapes.h>
#include <BulletCollision/CollisionShapes/btBvhTriangleMeshShape.h>
#include <BulletCollision/CollisionShapes/btTriangleIndexVertexArray.h>
#include <BulletCollision/CollisionDispatch/btInternalEdgeUtility.h>
#include <cmath>
#include <limits>
#include <stdexcept>
#include <vector>

namespace xray { namespace physics {
bt_collision_shape::bt_collision_shape(btCollisionShape* shape) : m_bt_shape(shape) {}
bt_collision_shape::bt_collision_shape(btCollisionShape* shape,std::shared_ptr<void const> backing_store)
    : m_bt_shape(shape),m_backing_store(std::move(backing_store)) {}
bt_collision_shape::~bt_collision_shape()=default;

void destroy_shape(memory::base_allocator& allocator,bt_collision_shape* shape) {
    if (!shape) return;
    auto* raw=shape->get_bt_shape();
    // Bullet's BVH must be destroyed before the interface and vertex/index buffers.
    XRAY_DELETE_IMPL(allocator,raw);
    XRAY_DELETE_IMPL(allocator,shape);
}

namespace {
struct mesh_storage {
    std::vector<float3> vertices;
    std::vector<u32> indices;
    btTriangleIndexVertexArray interface;
    btTriangleInfoMap edges;
};
bool finite(float3 const& value) {
    return std::isfinite(value.x) && std::isfinite(value.y) && std::isfinite(value.z);
}
}
bt_collision_shape::bt_collision_shape(memory::base_allocator& allocator,
    std::span<float3 const> vertices,std::span<u32 const> indices,float3 const& center) : m_bt_shape(nullptr) {
    if (vertices.size()<3 || vertices.size()>size_t(std::numeric_limits<int>::max())
        || indices.empty() || indices.size()%3 || indices.size()>size_t(std::numeric_limits<int>::max()) || !finite(center))
        throw std::runtime_error("Invalid owned static triangle mesh dimensions/center");
    for (auto const& v : vertices) if (!finite(v) || !finite(v-center))
        throw std::runtime_error("Owned static triangle mesh requires finite centered vertices");
    for (u32 index : indices) if (index>=vertices.size())
        throw std::runtime_error("Owned static triangle mesh index outside vertex buffer");
    for (size_t i=0;i<indices.size();i+=3) {
        auto a=vertices[indices[i+1]]-vertices[indices[i]],b=vertices[indices[i+2]]-vertices[indices[i]];
        double x=double(a.y)*b.z-double(a.z)*b.y,y=double(a.z)*b.x-double(a.x)*b.z,z=double(a.x)*b.y-double(a.y)*b.x;
        if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(z) || (x==0 && y==0 && z==0))
            throw std::runtime_error("Owned static triangle mesh contains a degenerate triangle");
    }
    auto storage=std::make_shared<mesh_storage>();
    storage->vertices.reserve(vertices.size());
    for (auto const& v : vertices) {
        auto local=v-center;storage->vertices.emplace_back(local.x,local.y,-local.z);
    }
    storage->indices.assign(indices.begin(),indices.end());
    for (size_t i=0;i<indices.size();i+=3) std::swap(storage->indices[i+1],storage->indices[i+2]);
    btIndexedMesh mesh;
    mesh.m_numTriangles=int(indices.size()/3);
    mesh.m_triangleIndexBase=reinterpret_cast<unsigned char const*>(storage->indices.data());
    mesh.m_triangleIndexStride=3*sizeof(u32);
    mesh.m_numVertices=int(vertices.size());
    mesh.m_vertexBase=reinterpret_cast<unsigned char const*>(storage->vertices.data());
    mesh.m_vertexStride=sizeof(float3);mesh.m_vertexType=PHY_FLOAT;
    static_assert(sizeof(u32)==sizeof(int));
    storage->interface.addIndexedMesh(mesh,PHY_INTEGER);
    auto* shape=XRAY_NEW_IMPL(allocator,btBvhTriangleMeshShape)(&storage->interface,true);
    shape->setMargin(0);
    shape->recalcLocalAabb();
    try {
        btGenerateInternalEdgeInfo(shape,&storage->edges);
        m_backing_store=storage;m_bt_shape=shape;
    }
    catch (...) { XRAY_DELETE_IMPL(allocator,shape);throw; }
}
bt_collision_shape* create_owned_static_triangle_mesh_shape(memory::base_allocator& allocator,
    std::span<float3 const> vertices,std::span<u32 const> indices,float3 const& center) {
    return XRAY_NEW_IMPL(allocator,bt_collision_shape)(allocator,vertices,indices,center);
}
} }
