#include "pch.h"
#include "editor_world.h"

#include <xray/render/facade/editor_renderer.h>
#include <xray/physics/api.h>
#include <xray/physics/world.h>

namespace xray {
namespace editor {

editor_world::editor_world(xray::editor::engine& engine) :
	m_engine(engine),
	m_editor_mode(true),
	m_paused(false),
	m_frame_id(0),
	m_exit_query(false)
{
	m_scene = new render::scene_ptr();
	m_scene_view = new render::scene_view_ptr();
	m_view_output_window = new render::render_output_window_ptr();
	m_camera_view_output_window = new render::render_output_window_ptr();
	m_sound_scene = new sound::sound_scene_ptr();

	physics::set_memory_allocator(memory::g_crt_allocator);
	m_physics_world = physics::create_world_bt(memory::g_crt_allocator, *this);
	m_physics_world->initialize();
}

editor_world::~editor_world()
{
	if (m_physics_world) {
		physics::destroy_world(memory::g_crt_allocator, m_physics_world);
		m_physics_world = nullptr;
	}
	delete m_sound_scene;
	delete m_camera_view_output_window;
	delete m_view_output_window;
	delete m_scene_view;
	delete m_scene;
}

void editor_world::load()
{
}

void editor_world::run()
{
}

void editor_world::clear_resources()
{
}

HWND editor_world::main_handle() { return NULL; }
HWND editor_world::view_handle() { return NULL; }
HWND editor_world::camera_view_handle() { return NULL; }

void editor_world::tick()
{
	m_physics_world->tick();
}

bool editor_world::close_query() { return true; }
void editor_world::on_window_closing() {}
void editor_world::editor_mode(bool beditor_mode)
{
	m_editor_mode = beditor_mode;
}

void editor_world::render(u32, math::float4x4) {}
int editor_world::exit_code() const { return 0; }
void editor_world::message_out(pcstr) {}
void editor_world::set_navmesh_info(pcstr) const {}

} // namespace editor
} // namespace xray
