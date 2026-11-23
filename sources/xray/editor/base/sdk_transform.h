#pragma once
#include <xray/render/facade/editor_renderer.h>
#include <xray/editor/world/sdk_scene_bridge.h>
namespace xray::editor_base {
// Executes the existing native controls; it never owns managed history.
__declspec(dllexport) void sdk_transform(memory::mimalloc_allocator_type&,render::scene_ptr const&,
    render::scene_view_ptr const&,render::render_output_window_ptr const&,editor::hosting::sdk_scene_transform&);
__declspec(dllexport) void sdk_transform_in_frame(memory::mimalloc_allocator_type&,render::scene_ptr const&,
    render::scene_view_ptr const&,render::render_output_window_ptr const&,editor::hosting::sdk_scene_transform&,math::float4x4 const&);
}
