////////////////////////////////////////////////////////////////////////////
//	Created		: 15.08.2011
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#ifndef XRAY_DEBUG_BUILDING
#define XRAY_DEBUG_BUILDING
#endif

#ifndef XRAY_STATIC_LIBRARIES
#	ifndef XRAY_ENGINE_BUILDING
#		define XRAY_ENGINE_BUILDING
#	endif
#endif // #ifndef XRAY_STATIC_LIBRARIES

#include <xray/macro_extensions.h>
#include <xray/type_extensions.h>
#include <xray/noncopyable.h>
#include <functional>
#include <xray/debug/log_callback.h>

#endif // #ifndef PCH_H_INCLUDED
