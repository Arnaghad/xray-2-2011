////////////////////////////////////////////////////////////////////////////
//	Created 	: 13.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/linkage_helper.h>

//XRAY_INCLUDE_TO_LINKAGE(core_math_convex)		// guarded by #ifndef XRAY_STATIC_LIBRARIES
XRAY_INCLUDE_TO_LINKAGE(core_math_sphere)
XRAY_INCLUDE_TO_LINKAGE(core_geometry_utils)
#if XRAY_PLATFORM_WINDOWS
XRAY_INCLUDE_TO_LINKAGE(core_os_functions_win)
#endif
XRAY_INCLUDE_TO_LINKAGE(core_memory_writer)
XRAY_INCLUDE_TO_LINKAGE(core_text_tree)
XRAY_INCLUDE_TO_LINKAGE(core_profiler)
//XRAY_INCLUDE_TO_LINKAGE(maya_animation_entry_point)	// moved to C#
//XRAY_INCLUDE_TO_LINKAGE(input_entry_point)		// moved to C#
//XRAY_INCLUDE_TO_LINKAGE(rtp_entry_point)		// moved to C#
//XRAY_INCLUDE_TO_LINKAGE(ui_entry_point)			// moved to C#
//XRAY_INCLUDE_TO_LINKAGE(ai_navigation_entry_point)	// moved to C#
//XRAY_INCLUDE_TO_LINKAGE(ai_entry_point)			// moved to C#

#include <xray/render/api.h>
#include <xray/render/engine/api.h>
#include <xray/render/base/memory.h>
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/vulkan/sources/vulkan_backend.h>
#include <xray/render/engine/world_dx11.h>
#include <xray/render/facade/engine_renderer.h>
#include <xray/render/facade/game_renderer.h>
#include <xray/render/facade/editor_renderer.h>
#include <xray/render/facade/debug_renderer.h>
#include <xray/render/facade/base_command.h>
#include <xray/render/vulkan/legacy_capabilities.h>

namespace xray {
namespace render {


namespace engine {

world* create_world() { 
	return XRAY_NEW_IMPL(get_render_allocator(), world)();
}
void destroy(world*& w) { XRAY_DELETE_IMPL(get_render_allocator(), w); w = nullptr; }

void world::set_model_ghost_mode(render::render_model_instance_ptr const&, bool) { XRAY_REJECT_VULKAN_LEGACY(set_model_ghost_mode); }







void world::draw_debug_lines(colored_vertices_type const&, colored_indices_type const&) { XRAY_REJECT_VULKAN_LEGACY(draw_debug_lines); }
void world::draw_debug_triangles(colored_vertices_type const&, colored_indices_type const&) { XRAY_REJECT_VULKAN_LEGACY(draw_debug_triangles); }
void world::draw_editor_lines(colored_vertices_type const&, colored_indices_type const&) { XRAY_REJECT_VULKAN_LEGACY(draw_editor_lines); }
void world::draw_editor_triangles(colored_vertices_type const&, colored_indices_type const&) { XRAY_REJECT_VULKAN_LEGACY(draw_editor_triangles); }


void world::set_slomo(scene_ptr const&, float) { XRAY_REJECT_VULKAN_LEGACY(set_slomo); }
void world::set_particles_render_mode(scene_view_ptr, xray::particle::enum_particle_render_mode) { XRAY_REJECT_VULKAN_LEGACY(set_particles_render_mode); }
void world::set_post_process(scene_view_ptr, resources::unmanaged_resource_ptr) { XRAY_REJECT_VULKAN_LEGACY(set_post_process); }
void world::toggle_render_stage(enum_render_stage_type, bool) { XRAY_REJECT_VULKAN_LEGACY(toggle_render_stage); }
particle::world& world::particle_world(scene_ptr const&) { XRAY_REJECT_VULKAN_LEGACY(particle_world); }
void world::reload_shaders() { XRAY_REJECT_VULKAN_LEGACY(reload_shaders); }
void world::draw_render_statistics(xray::ui::world*) { XRAY_REJECT_VULKAN_LEGACY(draw_render_statistics); }

void world::set_renderer_configuration(fs_new::virtual_path_string const&, bool) { XRAY_REJECT_VULKAN_LEGACY(set_renderer_configuration); }
void world::apply_render_options_changes() { XRAY_REJECT_VULKAN_LEGACY(apply_render_options_changes); }
void world::draw_text(pcstr, xray::math::float2 const&, xray::ui::font* const, xray::math::color const&) { XRAY_REJECT_VULKAN_LEGACY(draw_text); }
void world::draw_screen_lines(scene_ptr const&, float2 const*, u32, math::color const&, float, u32) { XRAY_REJECT_VULKAN_LEGACY(draw_screen_lines); }
void world::draw_3D_screen_lines(scene_ptr const&, float3 const*, u32, math::color const&, float, u32, bool) { XRAY_REJECT_VULKAN_LEGACY(draw_3D_screen_lines); }
void world::draw_3D_screen_point(scene_ptr const&, float3 const&, math::color, float, bool) { XRAY_REJECT_VULKAN_LEGACY(draw_3D_screen_point); }
void world::setup_grid_render_mode(u32) { XRAY_REJECT_VULKAN_LEGACY(setup_grid_render_mode); }
void world::remove_grid_render_mode() { XRAY_REJECT_VULKAN_LEGACY(remove_grid_render_mode); }
void world::setup_rotation_control_modes(bool) { XRAY_REJECT_VULKAN_LEGACY(setup_rotation_control_modes); }
void world::clear_zbuffer(float) { XRAY_REJECT_VULKAN_LEGACY(clear_zbuffer); }
void world::draw_terrain_debug() { XRAY_REJECT_VULKAN_LEGACY(draw_terrain_debug); }
void world::set_selection_parameters(math::float4 const&, float) { XRAY_REJECT_VULKAN_LEGACY(set_selection_parameters); }
void world::update_system_model(render::render_model_instance_ptr const&, math::float4x4 const&) { XRAY_REJECT_VULKAN_LEGACY(update_system_model); }

#ifndef MASTER_GOLD
void world::update_preview_particle_system(particle::particle_system**, scene_ptr const&, configs::lua_config_value) { XRAY_REJECT_VULKAN_LEGACY(update_preview_particle_system); }
void world::update_preview_particle_system_transform(particle::particle_system**, scene_ptr const&, math::float4x4 const&) { XRAY_REJECT_VULKAN_LEGACY(update_preview_particle_system_transform); }
void world::add_preview_particle_system(particle::particle_system**, scene_ptr const&, configs::lua_config_value, math::float4x4 const&) { XRAY_REJECT_VULKAN_LEGACY(add_preview_particle_system); }
void world::remove_preview_particle_system(particle::particle_system**, scene_ptr const&) { XRAY_REJECT_VULKAN_LEGACY(remove_preview_particle_system); }
void world::restart_preview_particle_system(particle::particle_system**, scene_ptr const&) { XRAY_REJECT_VULKAN_LEGACY(restart_preview_particle_system); }
void world::set_looping_preview_particle_system(particle::particle_system**, scene_ptr const&, bool) { XRAY_REJECT_VULKAN_LEGACY(set_looping_preview_particle_system); }
void world::show_preview_particle_system(particle::particle_system**, scene_ptr const&) { XRAY_REJECT_VULKAN_LEGACY(show_preview_particle_system); }
void world::gather_preview_particle_system_statistics(particle::particle_system**, scene_ptr const&, particle::preview_particle_emitter_info*) { XRAY_REJECT_VULKAN_LEGACY(gather_preview_particle_system_statistics); }
u32 world::get_num_preview_particle_system_emitters(particle::particle_system**, scene_ptr const&) const { XRAY_REJECT_VULKAN_LEGACY(get_num_preview_particle_system_emitters); }
void world::select_particle_system_instance(scene_ptr const&, particle::particle_system_instance_ptr const&, bool) { XRAY_REJECT_VULKAN_LEGACY(select_particle_system_instance); }
void world::select_speedtree_instance(scene_ptr const&, render::speedtree_instance_ptr const&, bool) { XRAY_REJECT_VULKAN_LEGACY(select_speedtree_instance); }
void world::enable_terrain_debug_mode(bool) { XRAY_REJECT_VULKAN_LEGACY(enable_terrain_debug_mode); }
void world::apply_material_changes(fs_new::virtual_path_string const&, xray::configs::lua_config_value const&) { XRAY_REJECT_VULKAN_LEGACY(apply_material_changes); }
#endif

pcstr world::type() { return "vulkan"; }

void world::reset_renderer(bool) { XRAY_REJECT_VULKAN_LEGACY(reset_renderer); }
void world::on_renderer_config_loaded(bool, resources::queries_result&) { XRAY_REJECT_VULKAN_LEGACY(on_renderer_config_loaded); }
void world::on_console_commands_config_loaded(bool, resources::queries_result&) { XRAY_REJECT_VULKAN_LEGACY(on_console_commands_config_loaded); }
void world::load_console_commands_config_query(pcstr, bool) { XRAY_REJECT_VULKAN_LEGACY(load_console_commands_config_query); }


} // namespace engine

} // namespace render
} // namespace xray

#include <xray/render/base/sources/platform_api.h>

namespace xray {
namespace render {
namespace dx9 {
render::platform* create_render_platform(xray::render::engine::wrapper&, xray::platform::native_surface_handle) { XRAY_REJECT_VULKAN_LEGACY(dx9_create_render_platform); }
} // namespace dx9
namespace dx10 {
render::platform* create_render_platform(xray::render::engine::wrapper&, xray::platform::native_surface_handle) {
    XRAY_REJECT_VULKAN_LEGACY(dx10_create_render_platform);
}
} // namespace dx10
} // namespace render
} // namespace xray

