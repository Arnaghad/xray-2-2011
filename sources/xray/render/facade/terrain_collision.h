#ifndef XRAY_RENDER_TERRAIN_COLLISION_H_INCLUDED
#define XRAY_RENDER_TERRAIN_COLLISION_H_INCLUDED

namespace xray {
namespace collision { class geometry_instance; }
namespace physics { class bt_collision_shape; }
namespace render {
class render_model_instance;
// Non-resource interface: the queried engine dependency has exactly one unmanaged base.
class terrain_collision_access {
public:
    virtual render_model_instance* source_instance() const=0;
    virtual collision::geometry_instance* geometry_instance() const=0;
    virtual physics::bt_collision_shape* physics_shape()=0;
    virtual math::float3 center_offset() const=0;
protected:
    virtual ~terrain_collision_access()=default;
};
} }
#endif
