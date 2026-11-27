#include <xray/editor/world/host_bridge.h>
#include <xray/editor/world/sdk_property_bridge.h>
#include <xray/editor/world/sdk_resource_bridge.h>
#include <xray/editor/world/sdk_scene_bridge.h>

#include <cstddef>
#include <iostream>

using xray::editor::hosting::abi_version;
using xray::editor::hosting::callbacks;
using xray::editor::hosting::surface_descriptor;
using xray::editor::hosting::window_control_request;
using xray::editor::hosting::world_bridge;

static_assert(abi_version == 3, "EditorHost C ABI version mismatch");
static_assert(sizeof(surface_descriptor) == 24, "surface_descriptor struct size mismatch");
static_assert(offsetof(surface_descriptor, handle) == 8);
static_assert(offsetof(surface_descriptor, display) == 16);
static_assert(sizeof(window_control_request) == 56, "window_control_request struct size mismatch");
static_assert(offsetof(window_control_request, surface) == 32);
static_assert(sizeof(callbacks) == 80, "callbacks struct size mismatch");
static_assert(sizeof(world_bridge) == 120, "world_bridge struct size mismatch");
static_assert(offsetof(callbacks, version) == 0);
static_assert(offsetof(callbacks, size) == 4);
static_assert(offsetof(callbacks, context) == 8);
static_assert(offsetof(callbacks, flags) == 16);
static_assert(offsetof(callbacks, tick) == 24);
static_assert(offsetof(callbacks, render) == 32);
static_assert(offsetof(callbacks, preview) == 72);
static_assert(offsetof(world_bridge, context) == 8);
static_assert(offsetof(world_bridge, load) == 16);
static_assert(offsetof(world_bridge, main_surface) == 48);
static_assert(offsetof(world_bridge, view_surface) == 56);
static_assert(offsetof(world_bridge, render) == 72);
static_assert(offsetof(world_bridge, preview_surface) == 96);
static_assert(offsetof(world_bridge, preview_closed) == 104);
static_assert(offsetof(world_bridge, window_control) == 112);

using xray::editor::hosting::sdk_position;
using xray::editor::hosting::sdk_callbacks;
using xray::editor::hosting::sdk_bridge;
static_assert(sizeof(sdk_position) == 40 && offsetof(sdk_position, x) == 24);
static_assert(sizeof(sdk_callbacks) == 40 && offsetof(sdk_callbacks, read) == 24);
static_assert(sizeof(sdk_bridge) == 16 && offsetof(sdk_bridge, diagnostic_step) == 8);

using xray::editor::hosting::sdk_resource_request;
using xray::editor::hosting::sdk_resource_result;
using xray::editor::hosting::sdk_resource_callbacks;
using xray::editor::hosting::sdk_byte_buffer;
using xray::editor::hosting::sdk_model_binding;
using xray::editor::hosting::sdk_model_build;
using xray::editor::hosting::sdk_animation_sample;
using xray::editor::hosting::sdk_collision_primitive;
using xray::editor::hosting::sdk_model_physics;
static_assert(sizeof(sdk_resource_request) == 40 && offsetof(sdk_resource_request, path) == 24);
static_assert(sizeof(sdk_resource_result) == 32 && offsetof(sdk_resource_result, bytes) == 24);
static_assert(sizeof(sdk_resource_callbacks) == 24 && offsetof(sdk_resource_callbacks, request) == 16);
static_assert(sizeof(sdk_byte_buffer) == 24 && offsetof(sdk_byte_buffer, data) == 16);
static_assert(sizeof(sdk_model_binding) == 16 && offsetof(sdk_model_binding, material) == 8);
static_assert(sizeof(sdk_model_build) == 32 && offsetof(sdk_model_build, surfaces) == 24);
static_assert(sizeof(sdk_animation_sample) == 40 && offsetof(sdk_animation_sample, output) == 16);
static_assert(sizeof(sdk_collision_primitive) == 44 && offsetof(sdk_collision_primitive, scale) == 32);
static_assert(sizeof(sdk_model_physics) == 88 && offsetof(sdk_model_physics, primitives) == 80);

using xray::editor::hosting::sdk_scene_object;
using xray::editor::hosting::sdk_scene_request;
using xray::editor::hosting::sdk_scene_callbacks;
using xray::editor::hosting::sdk_viewport_settings;
using xray::editor::hosting::sdk_viewport_snapshot;
using xray::editor::hosting::sdk_gizmo_settings;
using xray::editor::hosting::sdk_gizmo_drag;
using xray::editor::hosting::sdk_output_settings;
using xray::editor::hosting::sdk_output_status;
using xray::editor::hosting::sdk_runtime_settings;
using xray::editor::hosting::sdk_runtime_status;
using xray::editor::hosting::sdk_runtime_physics_status;
using xray::editor::hosting::sdk_world_object_extension;
using xray::editor::hosting::sdk_terrain_cell_upload;
using xray::editor::hosting::sdk_world_scene_status;
static_assert(sizeof(sdk_scene_object) == 72 && offsetof(sdk_scene_object, path) == 16);
static_assert(sizeof(sdk_scene_request) == 48 && offsetof(sdk_scene_request, data) == 32);
static_assert(sizeof(sdk_scene_callbacks) == 24 && offsetof(sdk_scene_callbacks, request) == 16);
static_assert(sizeof(sdk_viewport_settings) == 32);
static_assert(sizeof(sdk_viewport_snapshot) == 144);
static_assert(sizeof(sdk_gizmo_settings) == 232);
static_assert(sizeof(sdk_gizmo_drag) == 328);
static_assert(sizeof(sdk_output_settings) == 120);
static_assert(sizeof(sdk_output_status) == 72);
static_assert(sizeof(sdk_runtime_settings) == 144);
static_assert(sizeof(sdk_runtime_status) == 64);
static_assert(sizeof(sdk_runtime_physics_status) == 32);
static_assert(sizeof(sdk_world_object_extension) == 160);
static_assert(sizeof(sdk_terrain_cell_upload) == 40);
static_assert(sizeof(sdk_world_scene_status) == 48);

int main() {
    std::cout << "PASS: xray_editor_host_abi_contract (abi_version=" << abi_version << ")\n";
    return 0;
}
