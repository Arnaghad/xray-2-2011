////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux diagnostics, error dialogs, and atomics for xray::debug
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <dlfcn.h>
#include <fstream>
#include <sched.h>
#include <signal.h>
#include <string>
#include <sys/syscall.h>
#include <time.h>
#include <unistd.h>

#include <xray/debug/call_stack.h>
#include <xray/debug/extensions.h>
#include "call_stack.h"
#include "platform.h"
#include "utils.h"

namespace {

// GNU strerror_r returns char*; XSI strerror_r returns int.
inline pcstr pick_strerror_result( char* const gnu_res, char* const /*buf*/, int const /*err*/ )
{
	return gnu_res;
}

inline pcstr pick_strerror_result( int const xsi_rc, char* const buf, int const err )
{
	if ( xsi_rc == 0 )
		return buf;
	std::snprintf( buf, 64, "errno %d", err );
	return buf;
}

bool s_error_after_dialog = false;

} // namespace

namespace xray {
namespace debug {

atomic32 interlocked_increment( atomic32& value )
{
	return __atomic_add_fetch( &value, 1, __ATOMIC_SEQ_CST );
}

atomic32 interlocked_decrement( atomic32& value )
{
	return __atomic_sub_fetch( &value, 1, __ATOMIC_SEQ_CST );
}

atomic32 interlocked_exchange( atomic32& target, atomic32 const value )
{
	return __atomic_exchange_n( &target, value, __ATOMIC_SEQ_CST );
}

atomic32 interlocked_compare_exchange( atomic32& target, atomic32 value, atomic32 comparand )
{
	__atomic_compare_exchange_n( &target, &comparand, value, false, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST );
	return comparand;
}

u32 current_thread_id()
{
	return static_cast<u32>( ::syscall( SYS_gettid ) );
}

void yield( u32 const milliseconds )
{
	if ( milliseconds ) {
		struct timespec req = {};
		req.tv_sec = static_cast<time_t>( milliseconds / 1000u );
		req.tv_nsec = static_cast<long>( ( milliseconds % 1000u ) * 1000000UL );
		while ( ::nanosleep( &req, &req ) == -1 && errno == EINTR ) {}
		return;
	}
	::sched_yield();
}

bool is_debugger_present()
{
	std::ifstream status_file( "/proc/self/status" );
	if ( !status_file.is_open() )
		return false;

	std::string line;
	while ( std::getline( status_file, line ) ) {
		if ( line.compare( 0, 10, "TracerPid:" ) == 0 ) {
			char const* ptr = line.c_str() + 10;
			while ( *ptr == ' ' || *ptr == '\t' )
				++ptr;
			long const pid = std::strtol( ptr, nullptr, 10 );
			return pid > 0;
		}
	}
	return false;
}

void debug_break()
{
	if ( is_debugger_present() ) {
		::raise( SIGTRAP );
		return;
	}
	__builtin_trap();
}

void output( pcstr const message )
{
	if ( !message || !*message )
		return;

	char const* disabled = std::getenv( "XRAY_DISABLE_DEBUG_OUTPUT" );
	if ( disabled && disabled[0] == '1' && disabled[1] == '\0' )
		return;

	char const* ptr = message;
	size_t remaining = std::strlen( message );
	while ( remaining > 0 ) {
		ssize_t const written = ::write( STDERR_FILENO, ptr, remaining );
		if ( written < 0 ) {
			if ( errno == EINTR )
				continue;
			break;
		}
		ptr += written;
		remaining -= static_cast<size_t>( written );
	}
}

bool format_system_error( int const error_code, char* const buffer, u32 const buffer_size )
{
	if ( !buffer || buffer_size == 0 )
		return false;

	char temp[512] = {};
	pcstr const resolved = pick_strerror_result( ::strerror_r( error_code, temp, sizeof( temp ) ), temp, error_code );
	std::snprintf( buffer, buffer_size, "%s", resolved ? resolved : "Unknown error" );
	return true;
}

void show_error_dialog( pcstr const title, pcstr const message )
{
	pcstr const safe_title = ( title && *title ) ? title : "Error";
	pcstr const safe_msg = ( message && *message ) ? message : "(no message)";

	std::fprintf( stderr, "\n[%s] %s\n", safe_title, safe_msg );
	std::fflush( stderr );

	if ( debug_engine() && debug_engine()->terminate_on_error() )
		return;

	char const* const display = std::getenv( "DISPLAY" );
	char const* const wayland = std::getenv( "WAYLAND_DISPLAY" );
	if ( ( !display || !*display ) && ( !wayland || !*wayland ) )
		return;

	using sdl_was_init_fn = u32 (*)( u32 flags );
	using sdl_show_box_fn = bool (*)( u32 flags, pcstr title, pcstr message, void* window );

	auto* const sdl_was_init = reinterpret_cast<sdl_was_init_fn>( ::dlsym( RTLD_DEFAULT, "SDL_WasInit" ) );
	auto* const sdl_show_box = reinterpret_cast<sdl_show_box_fn>( ::dlsym( RTLD_DEFAULT, "SDL_ShowSimpleMessageBox" ) );
	constexpr u32 sdl_init_video = 0x00000020u;
	constexpr u32 sdl_messagebox_error = 0x00000010u;

	if ( sdl_was_init && sdl_show_box && ( sdl_was_init( sdl_init_video ) & sdl_init_video ) != 0 )
		sdl_show_box( sdl_messagebox_error, safe_title, safe_msg, nullptr );
}

void debug_message_box( pcstr const message )
{
	show_error_dialog( "Debug Break", message );
}

int on_math_error( struct ::_exception* const exception )
{
	XRAY_UNREFERENCED_PARAMETER( exception );
	log_callback const log = get_log_callback();
	if ( log )
		log( "debug", true, false, "math runtime error" );
	return 0;
}

namespace {

struct dump_helper {
	string512	m_initiator;
	bool		m_use_error_verbosity;
	u32			m_num_first_to_ignore;
	u32			m_num_last_to_ignore;

	dump_helper( pcstr const initiator, bool const use_error_verbosity, u32 const first_ignore, u32 const last_ignore )
		: m_use_error_verbosity( use_error_verbosity )
		, m_num_first_to_ignore( first_ignore )
		, m_num_last_to_ignore( last_ignore )
	{
		strings_copy( m_initiator, initiator ? initiator : "debug" );
		strings_concat( m_initiator, ":" );
	}

	bool predicate(
		u32 const call_stack_id,
		u32 const num_call_stack_lines,
		pcstr const module_name,
		pcstr const file_name,
		int const line_number,
		pcstr const function,
		size_t const address
	)
	{
		if ( call_stack_id < m_num_first_to_ignore )
			return true;

		if ( m_num_last_to_ignore <= num_call_stack_lines && call_stack_id >= num_call_stack_lines - m_num_last_to_ignore )
			return true;

		string4096 line;
		if ( line_number > 0 )
			std::snprintf( line, sizeof( line ), "%-60s(%-3d) : %-70s : %-36s : 0x%016zx",
				file_name ? file_name : "?",
				line_number,
				function ? function : "?",
				module_name ? module_name : "?",
				address );
		else
			std::snprintf( line, sizeof( line ), "%-60s       : %-70s : 0x%016zx",
				module_name ? module_name : "?",
				function ? function : "?",
				address );

		log_callback const cb = get_log_callback();
		if ( cb )
			cb( m_initiator, m_use_error_verbosity, true, line );

		return true;
	}
};

} // namespace

void dump_call_stack(
	pcstr const initiator,
	bool const use_error_verbosity,
	u32 const num_first_to_ignore,
	u32 const num_last_to_ignore,
	_EXCEPTION_POINTERS* const pointers,
	void* call_stack[]
)
{
	dump_helper helper( initiator, use_error_verbosity, num_first_to_ignore, num_last_to_ignore );
	call_stack::iterate(
		pointers,
		call_stack,
		[&helper]( u32 id, u32 total, pcstr mod, pcstr file, int line, pcstr fn, size_t addr ) {
			return helper.predicate( id, total, mod, file, line, fn, addr );
		},
		true
	);
}

namespace platform {

bool error_after_dialog()
{
	return s_error_after_dialog;
}

void on_error(
	bool* const do_debug_break,
	char* const message,
	u32 const message_size,
	bool* const ignore_always,
	_EXCEPTION_POINTERS* const exception_information,
	error_type_enum const error_type
)
{
	XRAY_UNREFERENCED_PARAMETERS( message_size, ignore_always, exception_information );

	if ( error_type == error_type_unhandled_exception ) {
		if ( do_debug_break )
			*do_debug_break = false;
		return;
	}

	if ( do_debug_break )
		*do_debug_break = is_debugger_present();

	if ( !is_debugger_present() && ( !debug_engine() || !debug_engine()->terminate_on_error() ) ) {
		show_error_dialog( "Fatal Error", message );
		s_error_after_dialog = true;
	}

	xray::debug::on_error( message );
}

void on_error_message_box(
	bool* const do_debug_break,
	char* const message,
	u32 const message_size,
	bool* const ignore_always,
	_EXCEPTION_POINTERS* const exception_information,
	error_type_enum const error_type
)
{
	XRAY_UNREFERENCED_PARAMETERS( message_size, ignore_always, exception_information, error_type );

	if ( do_debug_break )
		*do_debug_break = false;

	show_error_dialog( "Fatal Error", message );
	xray::debug::on_error( message );
}

void terminate( pcstr const message, int const error_code )
{
	if ( message && *message ) {
		if ( debug_engine() && debug_engine()->terminate_on_error() ) {
			std::fprintf( stderr, "%s\n", message );
			std::fflush( stderr );
		} else {
			show_error_dialog( "Error", message );
		}
	}

	std::fflush( nullptr );
	::_exit( error_code );
}

pstr fill_format_message( int const error_code )
{
	constexpr u32 k_buf_size = 512;
	char* buf = new char[k_buf_size];
	buf[0] = '\0';
	format_system_error( error_code, buf, k_buf_size );
	return buf;
}

void free_format_message( pstr const buffer )
{
	delete[] buffer;
}

pcstr get_graphics_api_error_description( int const error_code )
{
	XRAY_UNREFERENCED_PARAMETER( error_code );
	return nullptr;
}

} // namespace platform
} // namespace debug
} // namespace xray
