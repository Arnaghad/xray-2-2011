////////////////////////////////////////////////////////////////////////////
//	Created 	: 13.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_BASE_RENDER_ALLOCATOR_H_INCLUDED
#define XRAY_RENDER_BASE_RENDER_ALLOCATOR_H_INCLUDED

#include <xray/render/api.h>

namespace xray {
namespace memory {
	class mimalloc_allocator;
	typedef mimalloc_allocator mimalloc_allocator_type;
} // namespace memory
namespace render {
	typedef memory::mimalloc_allocator_type render_allocator_type;
	extern XRAY_RENDER_API render_allocator_type* g_allocator;

	inline render_allocator_type& get_render_allocator() {
		return *g_allocator;
	}
} // namespace render
} // namespace xray

#define USER_ALLOCATOR (::xray::render::get_render_allocator())

#include <xray/render/base/api.h>

namespace xray {
namespace render {

#include <xray/std_containers.h>
#include <xray/unique_ptr.h>

} // namespace render
} // namespace xray

#undef USER_ALLOCATOR

#undef NEW
#define NEW( type )								XRAY_NEW_IMPL(		::xray::render::get_render_allocator(), type )

#undef NEW_ARRAY
#define NEW_ARRAY(type, count)					XRAY_NEW_ARRAY_IMPL(::xray::render::get_render_allocator(), type, count)

#undef DELETE_ARRAY
#define DELETE_ARRAY(pointer)					XRAY_DELETE_ARRAY_IMPL(::xray::render::get_render_allocator(), pointer )

#undef DELETE
#define DELETE( pointer, ... )					XRAY_DELETE_IMPL(	::xray::render::get_render_allocator(), pointer, ##__VA_ARGS__ )

#undef DELETET
#define DELETET( pointer )						XRAY_DELETE_IMPL(	::xray::render::get_render_allocator(), pointer )

#undef MALLOC
#define MALLOC( size, description )				XRAY_MALLOC_IMPL(	::xray::render::get_render_allocator(), size, description )

#undef REALLOC
#define REALLOC( pointer, size, description )	XRAY_REALLOC_IMPL(	::xray::render::get_render_allocator(), pointer, size, description )

#undef FREE
#define FREE( pointer )							XRAY_FREE_IMPL(		::xray::render::get_render_allocator(), pointer )

#undef ALLOC
#define ALLOC( type, count )					XRAY_ALLOC_IMPL(	::xray::render::get_render_allocator(), type, count )

#endif // #ifndef XRAY_RENDER_BASE_RENDER_ALLOCATOR_H_INCLUDED
