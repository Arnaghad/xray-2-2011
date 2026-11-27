////////////////////////////////////////////////////////////////////////////
//	Created		: 14.07.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_EDITOR_BASE_API_H_INCLUDED
#define XRAY_EDITOR_BASE_API_H_INCLUDED

#include <xray/macro_compiler.h>

#ifndef XRAY_EDITOR_BASE_API
#	ifndef XRAY_STATIC_LIBRARIES
#		ifdef XRAY_EDITOR_BASE_BUILDING
#			define XRAY_EDITOR_BASE_API		XRAY_DLL_EXPORT
#		else // #ifdef XRAY_EDITOR_BASE_BUILDING
#			define XRAY_EDITOR_BASE_API		XRAY_DLL_IMPORT
#		endif // #ifdef XRAY_EDITOR_BASE_BUILDING
#	else // #ifndef XRAY_STATIC_LIBRARIES
#		define XRAY_EDITOR_BASE_API
#	endif // #ifndef XRAY_STATIC_LIBRARIES
#endif // #ifndef XRAY_EDITOR_BASE_API

namespace xray {
namespace editor_base {
	typedef memory::mimalloc_allocator_type allocator_type;
} // namespace editor_base
} // namespace xray


#endif // #ifndef XRAY_EDITOR_BASE_API_H_INCLUDED
