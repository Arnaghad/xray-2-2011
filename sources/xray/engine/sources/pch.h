////////////////////////////////////////////////////////////////////////////
//	Created 	: 26.08.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#include <vector>
#include <list>
#include <map>
#include <set>
#include <deque>
#include <functional>
#include <string>
#include <memory>
#include <iterator>

#include <xray/macro_extensions.h>
#include <xray/type_extensions.h>

struct tagMSG;
typedef tagMSG* LPMSG;
#define XRAY_ENGINE_LIBRARY_BUILDING

#define XRAY_LOG_MODULE_INITIATOR	"engine"
#include <xray/extensions.h>
#include <xray/command_line_extensions.h>
#include "engine_memory.h"

#endif // #ifndef PCH_H_INCLUDED
