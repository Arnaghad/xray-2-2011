#pragma once
#include <xray/physics/model_physics.h>
#include <memory>
#include <span>
namespace xray::physics {
// Independent Bullet world owned by the calling editor/game thread.
class editor_physics_scene {
    struct implementation;
    std::unique_ptr<implementation> state;
public:
    editor_physics_scene();
    ~editor_physics_scene();
    editor_physics_scene(editor_physics_scene const&)=delete;
    editor_physics_scene& operator=(editor_physics_scene const&)=delete;
    unsigned add(model_physics_parameters const&,math::float4x4 const&);
    unsigned add_static_triangles(std::span<math::float3 const> vertices,std::span<u32 const> indices,math::float4x4 const& transform,float friction=0.8f,float restitution=0.1f);
    void step(float seconds);
    math::float4x4 transform(unsigned body) const;
    math::aabb bounds(unsigned body) const;
    unsigned size() const;
};
}
