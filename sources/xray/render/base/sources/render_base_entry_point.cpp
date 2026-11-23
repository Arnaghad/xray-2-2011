////////////////////////////////////////////////////////////////////////////
//	Created 	: 17.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"

#include <xray/render/base/world.h>
#include <xray/render/api.h>

namespace xray {
namespace render {

typedef render_allocator_type	render_allocator_type;
typedef logic_allocator_type	logic_allocator_type;
typedef editor_allocator_type	editor_allocator_type;

using xray::render::base_world;

static uninitialized_reference<base_world>	s_world;


XRAY_RENDER_API void memory_allocator	(
		render_allocator_type&	render_allocator,
		logic_allocator_type&	logic_allocator,
		editor_allocator_type&	editor_allocator
	)
{
	R_ASSERT			( !g_allocator );
	g_allocator			= &render_allocator;

	R_ASSERT			( !logic::g_allocator );
	logic::g_allocator	= &logic_allocator;

	R_ASSERT			( !editor::g_allocator );
	editor::g_allocator	= &editor_allocator;
}

XRAY_RENDER_API world* create_world	( engine::wrapper& wrapper, HWND window_handle )
{
	XRAY_CONSTRUCT_REFERENCE( s_world, base_world)	( wrapper, window_handle );
	return					( &*s_world );
}

XRAY_RENDER_API void destroy_world	( world*& world )
{
	R_ASSERT				( &*s_world == world );
	XRAY_DESTROY_REFERENCE	( s_world );
	world					= 0;
}

} // namespace render
} // namespace xray
