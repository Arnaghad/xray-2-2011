////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux no-op BugTrap stub (CS_USE_BUG_TRAP=0)
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <cstdio>
#include <xray/debug/extensions.h>
#include "bugtrap.h"
#include "utils.h"

void set_show_dialog_for_unhandled_exceptions( bool show );

namespace xray {
namespace debug {
namespace bugtrap {

bool initialized()
{
	return true;
}

void change_usage( error_mode const error_mode, bugtrap_usage const bugtrap_usage )
{
	XRAY_UNREFERENCED_PARAMETERS( error_mode, bugtrap_usage );
}

void initialize()
{
}

void finalize()
{
}

bool on_thread_spawn()
{
	return true;
}

void add_file( pcstr const full_path_file_name )
{
	XRAY_UNREFERENCED_PARAMETER( full_path_file_name );
}

} // namespace bugtrap

void on_error( pcstr const message )
{
	if ( message && *message ) {
		log_callback const cb = get_log_callback();
		if ( cb )
			cb( "debug:diagnostics", true, false, message );
		else
			std::fprintf( stderr, "%s\n", message );
	}

	set_show_dialog_for_unhandled_exceptions( false );
}

} // namespace debug
} // namespace xray
