////////////////////////////////////////////////////////////////////////////
//	Created 	: 28.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#ifndef XRAY_RENDER_BUILDING
#   define XRAY_RENDER_BUILDING
#endif

#ifndef XRAY_STATIC_LIBRARIES
#	ifndef XRAY_ENGINE_BUILDING
#		define XRAY_ENGINE_BUILDING
#	endif
#endif // #ifndef XRAY_STATIC_LIBRARIES

#define XRAY_LOG_MODULE_INITIATOR	"render_base"

// Order is important: memory must be included AFTER any possible windows.h inclusion
// but we also need to ensure its macros survive.
#include <xray/extensions.h>

#	include <xray/render/vulkan/render_include.h>

// Hard-redefine engine macros to ensure they survive windows header pollution
#include <xray/render/base/memory.h>

#undef DELETE
#define DELETE( pointer, ... )	XRAY_DELETE_IMPL( ::xray::render::get_render_allocator(), pointer, ##__VA_ARGS__ )
#undef NEW
#define NEW( type )				XRAY_NEW_IMPL( ::xray::render::get_render_allocator(), type )

using xray::math::float2;
using xray::math::float3;
using xray::math::color;

#endif // #ifndef PCH_H_INCLUDED
