////////////////////////////////////////////////////////////////////////////
//	Created		: 22.11.2010
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#ifndef XRAY_AI_BUILDING
#define XRAY_AI_BUILDING
#endif

#ifndef XRAY_STATIC_LIBRARIES
#	ifndef XRAY_ENGINE_BUILDING
#		define XRAY_ENGINE_BUILDING
#	endif
#endif // #ifndef XRAY_STATIC_LIBRARIES

#define XRAY_LOG_MODULE_INITIATOR	"ai"
#include <xray/extensions.h>

#include "ai_memory.h"

#endif // #ifndef PCH_H_INCLUDED
