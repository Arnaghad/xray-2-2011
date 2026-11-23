#pragma once
#include <xray/render/facade/scene_renderer.h>
#include <xray/render/facade/lighting_environment.h>
#include <xray/render/facade/postprocess_settings.h>
#include <xray/physics/model_physics.h>
namespace xray::render {
struct editor_runtime_body {render_model_instance_ptr model;math::float4x4 transform;physics::model_physics_parameters parameters;};
struct editor_runtime_terrain_body {std::vector<math::float3> vertices;std::vector<u32> indices;math::float4x4 transform=math::float4x4().identity();};
// Typed, process-local editor -> logic-owner handoff. The scene contains cloned
// runtime model instances; only immutable cooked asset data is shared.
struct editor_runtime_request {
    unsigned operation=0; // begin, update, stop, status, single step
    scene_ptr scene;
    scene_view_ptr view;
    render_output_window_ptr output;
    math::float4x4 camera=math::float4x4().identity(),projection=math::float4x4().identity();
    lighting_environment environment;
    postprocess_settings postprocess;
    bool simulate=false,paused=false;
    unsigned objects=0,state=0,logic_thread=0;
    u64 ticks=0,frames=0;
    double seconds=0;
    std::vector<editor_runtime_body> bodies;
    std::vector<editor_runtime_terrain_body> terrain_bodies;
    unsigned physics_bodies=0,physics_dynamic=0;
    float physics_minimum_y=0,physics_maximum_y=0;
};
}
