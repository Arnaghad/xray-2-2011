////////////////////////////////////////////////////////////////////////////
//	Created		: 02.06.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#ifndef XRAY_EDITOR_BASE_BUILDING
#	define XRAY_EDITOR_BASE_BUILDING
#endif

#define XRAY_LOG_MODULE_INITIATOR	"editor_base"
#include <xray/editor/base/editor_extensions.h>

// Include render facade before memory macros to ensure editor macros take precedence
#include <xray/render/facade/editor_renderer.h>

#include "editor_base_memory.h"

#include <xray/editor/base/unmanaged_string.h>

#endif // #ifndef PCH_H_INCLUDED
