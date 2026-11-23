////////////////////////////////////////////////////////////////////////////
//	Created 	: 06.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef DEBUG_H_INCLUDED
#define DEBUG_H_INCLUDED

namespace xray {
namespace core {
namespace debug {

using xray::debug::error_mode;
using xray::debug::error_mode_silent;
using xray::debug::error_mode_verbose;
using xray::debug::bugtrap_usage;
using xray::debug::no_bugtrap;
using xray::debug::native_bugtrap;
using xray::debug::managed_bugtrap;

void preinitialize			( );
void initialize				( core::engine * engine );
void postinitialize			( );
void finalize				( );


void on_thread_spawn		( );
void on_error				( pcstr message );

core::engine *  get_core_engine ( );

} // namespace debug
} // namespace core
} // namespace xray

#endif // #ifndef DEBUG_H_INCLUDED
