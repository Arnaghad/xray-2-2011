////////////////////////////////////////////////////////////////////////////
//	Created 	: 31.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef EDITOR_WORLD_H_INCLUDED
#define EDITOR_WORLD_H_INCLUDED

#include <xray/editor/world/world.h>
#	include <xray/ui/engine.h>
#	include <xray/render/world.h>
#	include <xray/physics/engine.h>
#	include <xray/ai_navigation/api.h>
#	include <xray/ai_navigation/engine.h>
#include <xray/sound/sound.h>

namespace xray {

namespace collision { struct space_partitioning_tree; class geometry_instance; }

namespace ui{struct world;}
namespace physics{struct world;}
namespace sound{class sound_debug_stats;}

namespace editor {

class editor_world :
	public xray::editor::world,
	private ui::engine,
	private physics::engine,
	private ai::navigation::engine,
	private xray::noncopyable
{
public:
		editor_world			( xray::editor::engine& engine );
		virtual ~editor_world	( );

public:
	virtual	void				load					( ) override;
	virtual	void				run						( ) override;
	virtual	void				clear_resources			( ) override;
	virtual	HWND				main_handle				( ) override;
	virtual	HWND				view_handle				( ) override;
	virtual HWND				camera_view_handle		( );

	virtual	void				tick					( );
	virtual	bool				close_query				( );
	virtual	void				on_window_closing		( );
	virtual	void				editor_mode				( bool beditor_mode ) override;
	virtual	bool				get_editor_mode			( ) const { return m_editor_mode; }
	virtual void				render					( u32 frame_id, math::float4x4 view_matrix ) override;
	virtual int					exit_code				( ) const override;
	virtual void				message_out				( pcstr message ) override;
	virtual void				set_navmesh_info		( pcstr text ) const override;

private:
	xray::editor::engine&				m_engine;
	xray::ui::world*					m_ui_world;
	physics::world*						m_physics_world;
	ai::navigation::world*				m_ai_navigation_world;

	render::scene_ptr*					m_scene;
	render::scene_view_ptr*				m_scene_view;
	render::render_output_window_ptr*	m_view_output_window;
	render::render_output_window_ptr*	m_camera_view_output_window;
	sound::sound_scene_ptr*				m_sound_scene;

	bool								m_editor_mode;
	bool								m_paused;

	u32									m_frame_id;
	bool								m_exit_query;
}; // class editor_world

} // namespace editor
} // namespace xray

#endif // #ifndef EDITOR_WORLD_H_INCLUDED
