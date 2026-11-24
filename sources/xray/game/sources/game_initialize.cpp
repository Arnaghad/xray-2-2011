////////////////////////////////////////////////////////////////////////////
//	Created		: 29.07.2010
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "game.h"
#include <xray/physics/api.h>
#include <xray/input/api.h>
#include <xray/animation/api.h>
#include <xray/rtp/api.h>
#include <xray/ui/api.h>
#include <xray/ai_navigation/api.h>
#include <xray/ai/api.h>
#include <xray/render/world.h>
#include <xray/render/facade/game_renderer.h>
#include <xray/render/facade/debug_renderer.h>
#include <xray/render/base/debug_renderer.h>
#include <xray/game_test_suite.h>
#include <xray/collision/api.h>
#include <xray/sound/world.h>
#include <xray/sound/world_user.h>
#include <xray/physics/world.h>

namespace stalker2 {

void game::initialize_physics		( )
{
	ASSERT							( !m_initialized );

	ASSERT							( !m_physics_world );
#ifdef MASTER_GOLD
	xray::memory::base_allocator* allocator = g_allocator;
#else
	xray::memory::base_allocator* allocator = memory::g_crt_allocator;
#endif
#if defined(XRAY_GAME_PHYSICS_USE_BULLET) && XRAY_GAME_PHYSICS_USE_BULLET
	physics::set_memory_allocator	( allocator );
	m_physics_world					= physics::create_world_bt( allocator, *this );
#else
	m_physics_world					= physics::create_world( allocator, *this );
#endif
	ASSERT							( m_physics_world );
	m_physics_world->initialize		( );

	//m_physics_world->create_test_primitives( );
}

void game::initialize_input			( )
{
	ASSERT							( !m_initialized );

	ASSERT							( !m_input_world );
	m_input_world					= input::create_world( *this, m_engine.get_main_window_handle() );
	ASSERT							( m_input_world );
}

class debug_renderer_adapter : public xray::render::debug::base_renderer
{
public:
	debug_renderer_adapter(xray::render::debug::renderer* facade, xray::render::scene_ptr const& scene)
		: m_facade(facade), m_scene(scene)
	{
	}
	virtual void tick() override {}
	virtual void draw_line(float3 const& start, float3 const& end, xray::math::color color) override
	{
		m_facade->draw_line(m_scene, start, end, color);
	}
	virtual void draw_line(float3 const& start, float3 const& end, u32 color) override
	{
		m_facade->draw_line(m_scene, start, end, xray::math::color(color));
	}
	virtual void draw_lines(xray::vectora<xray::render::vertex_colored> const& vertices, xray::vectora<u16> const& indices) override
	{
		m_facade->draw_lines(m_scene, vertices, indices);
	}
	virtual void draw_obb(float4x4 const& matrix, float3 const& size, xray::math::color color) override
	{
		m_facade->draw_obb(m_scene, matrix, size, color);
	}
	virtual void draw_rectangle(float4x4 const& matrix, float3 const& size, xray::math::color color) override
	{
		m_facade->draw_rectangle(m_scene, matrix, size, color);
	}
	virtual void draw_aabb(float3 const& center, float3 const& size, xray::math::color color) override
	{
		m_facade->draw_aabb(m_scene, center, size, color);
	}
	virtual void draw_ellipse(float4x4 const& matrix, xray::math::color color) override
	{
		m_facade->draw_ellipse(m_scene, matrix, color);
	}
	virtual void draw_ellipsoid(float4x4 const& matrix, xray::math::color color) override
	{
		m_facade->draw_ellipsoid(m_scene, matrix, color);
	}
	virtual void draw_ellipsoid(float4x4 const& matrix, float3 const& size, xray::math::color color) override
	{
		m_facade->draw_ellipsoid(m_scene, matrix, size, color);
	}
	virtual void draw_sphere(float3 const& center, float const& radius, xray::math::color color) override
	{
		m_facade->draw_sphere(m_scene, center, radius, color);
	}
	virtual void draw_cone(float4x4 const& matrix, float3 const& size, xray::math::color color) override
	{
		m_facade->draw_cone(m_scene, matrix, size, color);
	}
	virtual void draw_arrow(float3 const& start_point, float3 const& end_point, xray::math::color arrow_color, xray::math::color cone_color) override
	{
		m_facade->draw_arrow(m_scene, start_point, end_point, arrow_color, cone_color);
	}
	virtual void draw_arrow(float3 const& start_point, float3 const& end_point, xray::math::color color) override
	{
		m_facade->draw_arrow(m_scene, start_point, end_point, color);
	}
	virtual void draw_triangle(float3 const& p0, float3 const& p1, float3 const& p2, xray::math::color color) override
	{
		m_facade->draw_triangle(m_scene, p0, p1, p2, color);
	}
	virtual void draw_triangle(float3 const& p0, float3 const& p1, float3 const& p2, u32 color) override
	{
		m_facade->draw_triangle(m_scene, p0, p1, p2, xray::math::color(color));
	}
	virtual void draw_triangle(xray::render::vertex_colored const& v0, xray::render::vertex_colored const& v1, xray::render::vertex_colored const& v2) override
	{
		m_facade->draw_triangle(m_scene, v0, v1, v2);
	}
	virtual void draw_triangle(xray::render::vertex_colored const (&vertices)[3]) override
	{
		m_facade->draw_triangle(m_scene, vertices);
	}
	virtual void draw_triangles(xray::vectora<xray::render::vertex_colored> const& vertices) override
	{
		m_facade->draw_triangles(m_scene, vertices);
	}
	virtual void draw_triangles(xray::vectora<xray::render::vertex_colored> const& vertices, xray::vectora<u16> const& indices) override
	{
		m_facade->draw_triangles(m_scene, vertices, indices);
	}
	virtual void draw_rectangle_solid(float4x4 const& matrix, float3 const& size, xray::math::color color) override
	{
		m_facade->draw_rectangle_solid(m_scene, matrix, size, color);
	}
	virtual void draw_cube_solid(float4x4 const& matrix, float3 const& size, xray::math::color color) override
	{
		m_facade->draw_cube_solid(m_scene, matrix, size, color);
	}
	virtual void draw_cone_solid(float4x4 const& matrix, float3 const& size, xray::math::color color) override
	{
		m_facade->draw_cone_solid(m_scene, matrix, size, color);
	}
	virtual void draw_cylinder_solid(float4x4 const& matrix, float3 const& size, xray::math::color color) override
	{
		m_facade->draw_cylinder_solid(m_scene, matrix, size, color);
	}
	virtual void draw_ellipsoid_solid(float4x4 const& matrix, float3 const& size, xray::math::color color) override
	{
		m_facade->draw_ellipsoid_solid(m_scene, matrix, size, color);
	}
private:
	xray::render::debug::renderer*	m_facade;
	xray::render::scene_ptr const&	m_scene;
};

void game::initialize_animation		( )
{
	ASSERT							( !m_initialized );

	ASSERT							( !m_animation_world );
	m_debug_adapter					= NEW(debug_renderer_adapter)( &m_renderer.debug(), m_scene );
	m_animation_world				= xray::animation::create_world( *this, m_scene, &m_renderer.scene(), m_debug_adapter );
	ASSERT							( m_animation_world );
}

void game::initialize_rtp			( )
{
	ASSERT							( !m_initialized );
	ASSERT							( m_animation_world );
	ASSERT							( !m_rtp_world );
	m_rtp_world						= xray::rtp::create_world( *this, *m_animation_world, m_scene, &m_renderer, m_engine.get_resources_path() );

	game_test_suite::singleton()->set_rtp_world( m_rtp_world );

	ASSERT							( m_rtp_world );
}

void game::initialize_ui			( )
{
	ASSERT							( !m_initialized );

	ASSERT							( !m_ui_world );
	m_ui_world						= xray::ui::create_world( *this, m_renderer.ui(), *g_allocator, m_scene_view );
	ASSERT							( m_ui_world );
}

void game::initialize_ai_navigation	( )
{
	ASSERT							( !m_initialized );

	ASSERT							( !m_ai_navigation_world );
	m_ai_navigation_world			= xray::ai::navigation::create_world( *this, m_scene, m_renderer.debug() );
	ASSERT							( m_ai_navigation_world );
}

void game::initialize_ai			( )
{
	ASSERT							( !m_initialized );

	ASSERT							( !m_spatial_tree );
	m_spatial_tree					= &*xray::collision::new_space_partitioning_tree( g_allocator, 1.f, 1024 );
	ASSERT							( m_spatial_tree );
	
	ASSERT							( !m_ai_world );
	m_ai_world						= xray::ai::create_world( *this );
	ASSERT							( m_ai_world );
}

void game::initialize_modules		( )
{
	initialize_physics				( );
	initialize_input				( );
	initialize_animation			( );
	initialize_rtp					( );
	initialize_ui					( );
	initialize_ai_navigation		( );
	initialize_ai					( );
}

void game::deinitialize_modules		( )
{
	xray::ai::destroy_world				( m_ai_world );
	xray::ai::navigation::destroy_world	( m_ai_navigation_world );
	xray::ui::destroy_world				( m_ui_world );
	xray::rtp::destroy_world				( m_rtp_world );
	xray::animation::destroy_world		( m_animation_world );
	xray::input::destroy_world			( m_input_world );
	debug_renderer_adapter* debug_adapter_ptr = static_cast<debug_renderer_adapter*>( m_debug_adapter );
	DELETE							( debug_adapter_ptr );
	m_debug_adapter					= nullptr;
#ifdef MASTER_GOLD
	xray::physics::destroy_world			( g_allocator, m_physics_world );
#else
	xray::physics::destroy_world			( xray::memory::g_crt_allocator, m_physics_world );
#endif

}

} // namespace stalker2
