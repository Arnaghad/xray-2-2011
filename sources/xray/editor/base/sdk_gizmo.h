#pragma once
#include <xray/editor/base/api.h>
#include <xray/render/facade/editor_renderer.h>
#include <xray/editor/world/sdk_scene_bridge.h>
#include <vector>
namespace xray::editor_base {
// Native draw port of the XYZ arrows/rings/scale boxes. Draw and picking consume
// this same triangle mesh; transform evaluation remains in transform_control_*.
struct sdk_gizmo_triangle {math::float3 a,b,c;u32 axis;};
XRAY_EDITOR_BASE_API void sdk_gizmo_build(editor::hosting::sdk_gizmo_settings const&,std::vector<sdk_gizmo_triangle>&);
XRAY_EDITOR_BASE_API editor::hosting::sdk_gizmo_hit sdk_gizmo_pick(editor::hosting::sdk_gizmo_settings const&,
    std::vector<sdk_gizmo_triangle> const&,editor::hosting::sdk_scene_ray const&);
XRAY_EDITOR_BASE_API editor::hosting::sdk_gizmo_delta sdk_gizmo_drag(editor::hosting::sdk_gizmo_drag const&);
XRAY_EDITOR_BASE_API editor::hosting::sdk_gizmo_delta_extended sdk_gizmo_drag_extended(editor::hosting::sdk_gizmo_drag const&);
}
