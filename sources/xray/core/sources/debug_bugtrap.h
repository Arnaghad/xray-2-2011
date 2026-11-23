////////////////////////////////////////////////////////////////////////////
//	Created 	: 09.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef CORE_DEBUG_BUGTRAP_H_INCLUDED
#define CORE_DEBUG_BUGTRAP_H_INCLUDED

#if XRAY_COMPILER_DOESNT_SUPPORT_ENUM_FORWARD_DECLARATIONS
#	include <xray/core.h>
#endif // #if XRAY_COMPILER_DOESNT_SUPPORT_ENUM_FORWARD_DECLARATIONS

namespace xray {
namespace core {
namespace debug {

// Import enum types and values from xray::debug namespace
using xray::debug::error_mode;
using xray::debug::error_mode_silent;
using xray::debug::error_mode_verbose;
using xray::debug::bugtrap_usage;
using xray::debug::no_bugtrap;
using xray::debug::native_bugtrap;
using xray::debug::managed_bugtrap;

namespace bugtrap {

bool initialized		( );

void change_usage		( error_mode error_mode, bugtrap_usage bugtrap_usage );
void initialize			( );
void finalize			( );

bool on_thread_spawn	( );

void add_file			( pcstr full_path_file_name );

} // namespace bugtrap
} // namespace debug
} // namespace core
} // namespace xray

#endif // #ifndef CORE_DEBUG_BUGTRAP_H_INCLUDED
