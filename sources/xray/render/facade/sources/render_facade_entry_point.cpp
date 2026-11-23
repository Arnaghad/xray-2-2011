////////////////////////////////////////////////////////////////////////////
//	Created 	: 17.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/render/world.h>
#include <xray/render/api.h>
#include <xray/render/core/memory.h>
#include <xray/render/vulkan/api.h>

typedef xray::render::render_allocator_type	render_allocator_type;
typedef xray::render::logic_allocator_type	logic_allocator_type;
typedef xray::render::editor_allocator_type	editor_allocator_type;

using xray::render::world;

static xray::uninitialized_reference<world>	s_world;

namespace xray { namespace render {
render_allocator_type* g_allocator = nullptr;
namespace logic { logic_allocator_type* g_allocator = nullptr; }
namespace editor { editor_allocator_type* g_allocator = nullptr; }

void set_memory_allocator(render_allocator_type& render_allocator,
    logic_allocator_type& logic_allocator, editor_allocator_type& editor_allocator)
{
    R_ASSERT(!g_allocator);
    R_ASSERT(!logic::g_allocator);
    R_ASSERT(!editor::g_allocator);
    g_allocator = &render_allocator;
    logic::g_allocator = &logic_allocator;
    editor::g_allocator = &editor_allocator;
    vulkan::set_memory_allocator(render_allocator);
}

void destroy_world(world*& instance)
{
    R_ASSERT(&*s_world == instance);
    XRAY_DESTROY_REFERENCE(s_world);
    instance = nullptr;
    vulkan::reset_memory_allocator();
    g_allocator = nullptr;
    logic::g_allocator = nullptr;
    editor::g_allocator = nullptr;
}
} }

xray::render::world* xray::render::create_world	(
		xray::memory::base_allocator& logic_allocator,
		xray::memory::base_allocator* editor_allocator
	)
{
	R_ASSERT( g_allocator && logic::g_allocator && editor::g_allocator );
	XRAY_CONSTRUCT_REFERENCE( s_world, world )	( logic_allocator, editor_allocator );
	return					( &*s_world );
}
