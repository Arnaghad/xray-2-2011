#pragma once
#include <xray/render/facade/model.h>
#include <xray/math_float4x4.h>
#include <vector>
namespace xray::render {
// A complete authored-order snapshot of a scene's static instances, including
// hidden members. One validated command publishes membership and all states.
struct model_scene_edit {render_model_instance_ptr model;math::float4x4 transform;bool visible,selected;};
}
