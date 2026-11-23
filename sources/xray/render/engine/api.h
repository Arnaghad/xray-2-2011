////////////////////////////////////////////////////////////////////////////
//	Created		: 24.12.2010
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_ENGINE_API_H_INCLUDED
#define XRAY_RENDER_ENGINE_API_H_INCLUDED

#ifndef XRAY_RENDER_ENGINE_API
#   if defined(XRAY_RENDER_SHARED)
#       if defined(XRAY_RENDER_BUILDING)
#           define XRAY_RENDER_ENGINE_API __declspec(dllexport)
#       else
#           define XRAY_RENDER_ENGINE_API __declspec(dllimport)
#       endif
#	elif defined(XRAY_STATIC_LIBRARIES)
#		define XRAY_RENDER_ENGINE_API
#	else // #ifdef XRAY_STATIC_LIBRARIES
#		ifdef XRAY_RENDER_BUILDING
#			define XRAY_RENDER_ENGINE_API		XRAY_DLL_EXPORT
#		else // #ifdef XRAY_RENDER_BUILDING
#			define XRAY_RENDER_ENGINE_API			XRAY_DLL_IMPORT
#		endif // #ifdef XRAY_RENDER_BUILDING
#	endif // #ifdef XRAY_STATIC_LIBRARIES
#endif // #ifndef XRAY_RENDER_ENGINE_API

namespace xray {
namespace render {
namespace engine {

class world;

XRAY_RENDER_ENGINE_API world*	create_world	( );
XRAY_RENDER_ENGINE_API void		destroy			( world*& instance );

} // namespace engine
} // namespace render
} // namespace xray

#endif // #ifndef XRAY_RENDER_ENGINE_API_H_INCLUDED

