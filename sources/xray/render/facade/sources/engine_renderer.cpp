////////////////////////////////////////////////////////////////////////////
//	Created 	: 17.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/render/facade/engine_renderer.h>
#include <xray/render/engine/world.h>

using xray::render::engine::renderer;

renderer::renderer					( xray::render::engine::world& engine_world ) :
	m_render_engine_world			( engine_world )
{
}

#if defined(USE_VULKAN_RENDERER)
bool renderer::initialize(platform::native_surface_handle surface)
{
	return m_render_engine_world.initialize(surface);
}
bool renderer::initialize_game(platform::native_surface_handle surface) { return m_render_engine_world.initialize_game(surface); }
bool renderer::finalize_game_resources() { return m_render_engine_world.finalize_game_resources(); }
bool renderer::initialized() const { return m_render_engine_world.initialized(); }
bool renderer::initialize_geometry_smoke() { return m_render_engine_world.initialize_geometry_smoke(); }
bool renderer::initialize_material_smoke() { return m_render_engine_world.initialize_material_smoke(); }
u32 renderer::presented_frame_id() const { return m_render_engine_world.presented_frame_id(); }
#else
void renderer::initialize			( )
{
	m_render_engine_world.initialize( );
}
#endif

u32 renderer::frame_id				( )
{
	return							( m_render_engine_world.frame_id( ) );
}
