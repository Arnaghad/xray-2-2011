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

#define XRAY_LOG_MODULE_INITIATOR	"render_facade"
#include <xray/extensions.h>

// Suppress redefinition warnings from Windows headers (winuser.h defines DELETE as 0x00010000 etc.)
#ifdef DELETE
#   undef DELETE
#endif
#ifdef NEW
#   undef NEW
#endif
#ifdef MALLOC
#   undef MALLOC
#endif
#ifdef FREE
#   undef FREE
#endif

#include <xray/render/base/render_allocator.h>
#include "logic_allocator.h"
#include "editor_allocator.h"

#endif // #ifndef PCH_H_INCLUDED
