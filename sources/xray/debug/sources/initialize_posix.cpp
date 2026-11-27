////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux initialization and FPE control for xray::debug
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <cfenv>
#include <signal.h>
#include <xray/debug/extensions.h>
#include "bugtrap.h"
#include "platform.h"
#include "utils.h"

namespace {

bool s_debug_preinitialized = false;
#ifdef DEBUG
bool s_fpe_enabled = false;
#endif

} // namespace

namespace xray {
namespace debug {

namespace platform {

void setup_storage_access_handler( error_mode const mode, void (*storage_access_handler)( int ) )
{
	XRAY_UNREFERENCED_PARAMETER( storage_access_handler );
	setup_unhandled_exception_handler( mode );
}

void change_storage_access_handler( error_mode const mode, void (*storage_access_handler)( int ) )
{
	XRAY_UNREFERENCED_PARAMETER( storage_access_handler );
	change_unhandled_exception_handler( mode );
}

} // namespace platform

void change_bugtrap_usage( error_mode const error_mode_val, bugtrap_usage const bugtrap_usage_val )
{
	bugtrap::change_usage( error_mode_val, bugtrap_usage_val );
	platform::change_storage_access_handler( error_mode_val, nullptr );
	postinitialize();
}

void preinitialize()
{
	if ( s_debug_preinitialized )
		return;

	s_debug_preinitialized = true;

	error_mode const mode = error_mode_verbose;
	bugtrap_usage const usage = no_bugtrap;
	bugtrap::change_usage( mode, usage );
	platform::setup_storage_access_handler( mode, nullptr );
	set_thread_stack_guarantee();

	// Ignore SIGPIPE so socket/pipe EOF errors return EPIPE instead of killing the process.
	::signal( SIGPIPE, SIG_IGN );
}

#ifdef DEBUG
bool is_fpe_enabled()
{
	return s_fpe_enabled;
}

void enable_fpe( bool const value )
{
	if ( s_fpe_enabled == value )
		return;

	s_fpe_enabled = value;
#if defined(__GLIBC__)
	std::feclearexcept( FE_ALL_EXCEPT );
	constexpr int k_mask = FE_INVALID | FE_DIVBYZERO | FE_OVERFLOW;
	if ( s_fpe_enabled )
		::feenableexcept( k_mask );
	else
		::fedisableexcept( k_mask );
#endif
}
#endif // #ifdef DEBUG

} // namespace debug
} // namespace xray
