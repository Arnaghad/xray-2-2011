////////////////////////////////////////////////////////////////////////////
//	Created 	: 20.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#ifndef XRAY_NETWORK_BUILDING
#define XRAY_NETWORK_BUILDING
#endif

#ifndef XRAY_STATIC_LIBRARIES
#	ifndef XRAY_ENGINE_BUILDING
#		define XRAY_ENGINE_BUILDING
#	endif
#endif // #ifndef XRAY_STATIC_LIBRARIES

#define XRAY_USE_STDIOSTREAM
#define XRAY_NO_SIGNALS_H


#include <xray/os_platform.h>
#if XRAY_PLATFORM_WINDOWS
#	include <xray/os_preinclude.h>
#	undef NONET
// Asio's Windows file API converts UTF-8 paths through the NLS declarations.
#	undef NONLS
#endif

#define XRAY_LOG_MODULE_INITIATOR	"network"
#include <xray/extensions.h>

#include "network_memory.h"

#if 1
#	ifndef _WIN32_WINNT
#		define _WIN32_WINNT 0x0601
#	endif
#	pragma push_macro("malloc")
#	pragma push_macro("calloc")
#	pragma push_macro("realloc")
#	pragma push_macro("free")
#	undef malloc
#	undef calloc
#	undef realloc
#	undef free
#	include "boost_api.h"
#	pragma pop_macro("free")
#	pragma pop_macro("realloc")
#	pragma pop_macro("calloc")
#	pragma pop_macro("malloc")
#endif // #if 1

#endif // #ifndef PCH_H_INCLUDED
