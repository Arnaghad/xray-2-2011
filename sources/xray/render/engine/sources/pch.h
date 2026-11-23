////////////////////////////////////////////////////////////////////////////
//	Created 	: 28.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#ifndef XRAY_STATIC_LIBRARIES
#	ifndef XRAY_ENGINE_BUILDING
#		define XRAY_ENGINE_BUILDING
#	endif
#endif // #ifndef XRAY_STATIC_LIBRARIES

#include <xray/macro_platform.h>

#	define USE_DX10	0
#	if USE_DX10
#		define XRAY_LOG_MODULE_INITIATOR	"render_dx10"
#	else // #if USE_DX10
#		define XRAY_LOG_MODULE_INITIATOR	"render_dx11"
#	endif // #if USE_DX10

#include <xray/extensions.h>

#define XRAY_RENDER_ENGINE_BUILDING

	// to disable warnings on sinf/cosf in speedtree SDK
#	pragma warning( push )
#	pragma warning( disable : 4995 )
#		include <speedtree/core/core.h>
#		include <speedtree/forest/forest.h>
#	pragma warning( pop )

#	include <speedtree/core/array.h>
#	include <speedtree/core/memory.h>
#	include <speedtree/forest/grass.h>

#include <xray/render/vulkan/render_include.h>
#include <xray/render/core/memory.h>

#endif // #ifndef PCH_H_INCLUDED
