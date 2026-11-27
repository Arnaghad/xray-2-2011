#pragma once
#include <xray/editor/base/api.h>
#include <xray/render/facade/editor_renderer.h>
#include <xray/editor/world/sdk_scene_bridge.h>
namespace xray::editor_base {
// Executes the existing native controls; it never owns managed history.
XRAY_EDITOR_BASE_API void sdk_transform(memory::mimalloc_allocator_type&,render::scene_ptr const&,
    render::scene_view_ptr const&,render::render_output_window_ptr const&,editor::hosting::sdk_scene_transform&);
XRAY_EDITOR_BASE_API void sdk_transform_in_frame(memory::mimalloc_allocator_type&,render::scene_ptr const&,
    render::scene_view_ptr const&,render::render_output_window_ptr const&,editor::hosting::sdk_scene_transform&,math::float4x4 const&);
}
