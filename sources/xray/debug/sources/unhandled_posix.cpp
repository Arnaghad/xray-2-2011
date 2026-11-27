////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux signal handling, protected_call, crash reports & fault tracer
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <algorithm>
#include <atomic>
#include <cerrno>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <exception>
#include <execinfo.h>
#include <fcntl.h>
#include <mutex>
#include <setjmp.h>
#include <signal.h>
#include <sys/syscall.h>
#include <time.h>
#include <ucontext.h>
#include <unistd.h>

#include <xray/debug/call_stack.h>
#include <xray/debug/extensions.h>
#include "bugtrap.h"
#include "platform.h"
#include "utils.h"

namespace {

constexpr u64 k_crash_context_magic = 0x5852415943525348ULL; // "XRAYCRSH"
constexpr int k_handled_signals[] = { SIGSEGV, SIGBUS, SIGFPE, SIGILL, SIGABRT };
constexpr std::size_t k_num_handled_signals = sizeof( k_handled_signals ) / sizeof( k_handled_signals[0] );

struct posix_crash_context {
	u64					magic = k_crash_context_magic;
	int					signal_number = 0;
	siginfo_t const*	siginfo = nullptr;
	ucontext_t const*	ucontext = nullptr;
};

xray::debug::error_mode	s_error_mode = xray::debug::error_mode_verbose;
bool					s_show_dialog_for_unhandled_exceptions = true;
struct sigaction		s_previous_actions[k_num_handled_signals] = {};
std::atomic<bool>		s_handlers_installed{ false };
std::atomic<bool>		s_handling_fatal_signal{ false };

thread_local sigjmp_buf*			tl_protected_jmp = nullptr;
thread_local volatile sig_atomic_t	tl_caught_signal = 0;
thread_local volatile std::uintptr_t tl_fault_addr = 0;

void write_all_fd( int const fd, char const* data, std::size_t len )
{
	while ( len > 0 ) {
		ssize_t const written = ::write( fd, data, len );
		if ( written < 0 ) {
			if ( errno == EINTR )
				continue;
			break;
		}
		data += written;
		len -= static_cast<std::size_t>( written );
	}
}

void write_str_fd( int const fd, char const* const text )
{
	if ( text && *text )
		write_all_fd( fd, text, std::strlen( text ) );
}

pcstr signal_name( int const sig )
{
	switch ( sig ) {
		case SIGSEGV: return "SIGSEGV (Segmentation fault)";
		case SIGBUS:  return "SIGBUS (Bus error)";
		case SIGFPE:  return "SIGFPE (Floating point exception)";
		case SIGILL:  return "SIGILL (Illegal instruction)";
		case SIGABRT: return "SIGABRT (Abort)";
		default:      return "UNKNOWN_SIGNAL";
	}
}

int signal_slot_index( int const sig )
{
	for ( std::size_t i = 0; i < k_num_handled_signals; ++i ) {
		if ( k_handled_signals[i] == sig )
			return static_cast<int>( i );
	}
	return -1;
}

xray::debug::date_time current_local_date_time()
{
	xray::debug::date_time dt;
	time_t const now = ::time( nullptr );
	struct tm local_tm = {};
	if ( ::localtime_r( &now, &local_tm ) ) {
		dt.year = static_cast<u16>( local_tm.tm_year + 1900 );
		dt.month = static_cast<u16>( local_tm.tm_mon + 1 );
		dt.day = static_cast<u16>( local_tm.tm_mday );
		dt.hour = static_cast<u16>( local_tm.tm_hour );
		dt.minute = static_cast<u16>( local_tm.tm_min );
		dt.second = static_cast<u16>( local_tm.tm_sec );
	}
	return dt;
}

void fatal_signal_handler( int const sig, siginfo_t* const info, void* const uctx )
{
	if ( tl_protected_jmp != nullptr && xray::debug::debug_engine() && xray::debug::debug_engine()->is_testing() ) {
		tl_caught_signal = sig;
		tl_fault_addr = info ? reinterpret_cast<std::uintptr_t>( info->si_addr ) : 0;
		::siglongjmp( *tl_protected_jmp, 1 );
	}

	if ( s_handling_fatal_signal.exchange( true, std::memory_order_acq_rel ) ) {
		struct sigaction dfl = {};
		dfl.sa_handler = SIG_DFL;
		::sigemptyset( &dfl.sa_mask );
		::sigaction( sig, &dfl, nullptr );
		::raise( sig );
		::_exit( 128 + sig );
	}

	posix_crash_context crash_ctx;
	crash_ctx.signal_number = sig;
	crash_ctx.siginfo = info;
	crash_ctx.ucontext = static_cast<ucontext_t const*>( uctx );

	if ( auto const cb = xray::debug::get_log_callback() ) {
		char msg[256] = {};
		std::snprintf( msg, sizeof( msg ), "Unhandled signal %s (%d) at address %p",
			signal_name( sig ), sig, info ? info->si_addr : nullptr );
		cb( "debug", true, false, msg );
	}

	if ( xray::debug::debug_engine() && xray::debug::debug_engine()->core_initialized() ) {
		xray::debug::platform::prologue_dump_call_stack( reinterpret_cast<_EXCEPTION_POINTERS*>( &crash_ctx ) );

		xray::debug::date_time const dt = current_local_date_time();
		string_path log_file_name = {};
		xray::debug::debug_engine()->generate_debug_file_name( log_file_name, &dt, "error_report", ".log" );
		xray::debug::debug_engine()->flush_log_file( log_file_name );
		xray::debug::platform::save_minidump( dt, reinterpret_cast<_EXCEPTION_POINTERS*>( &crash_ctx ) );
	} else {
		write_str_fd( STDERR_FILENO, "Fatal signal: " );
		write_str_fd( STDERR_FILENO, signal_name( sig ) );
		write_str_fd( STDERR_FILENO, "\n" );
		void* frames[64] = {};
		int const n = ::backtrace( frames, 64 );
		if ( n > 0 )
			::backtrace_symbols_fd( frames, n, STDERR_FILENO );
	}

	if ( xray::debug::is_debugger_present() )
		::raise( SIGTRAP );

	int const idx = signal_slot_index( sig );
	if ( idx >= 0 && ( s_previous_actions[idx].sa_flags & SA_SIGINFO ) == 0 &&
		 s_previous_actions[idx].sa_handler != SIG_DFL &&
		 s_previous_actions[idx].sa_handler != SIG_IGN &&
		 s_previous_actions[idx].sa_handler != nullptr ) {
		s_previous_actions[idx].sa_handler( sig );
	}

	struct sigaction dfl = {};
	dfl.sa_handler = SIG_DFL;
	::sigemptyset( &dfl.sa_mask );
	::sigaction( sig, &dfl, nullptr );
	::raise( sig );
	::_exit( 128 + sig );
}

void install_signal_handlers()
{
	if ( s_handlers_installed.exchange( true, std::memory_order_acq_rel ) )
		return;

	struct sigaction sa = {};
	sa.sa_sigaction = &fatal_signal_handler;
	sa.sa_flags = SA_SIGINFO | SA_ONSTACK;
	::sigemptyset( &sa.sa_mask );

	for ( std::size_t i = 0; i < k_num_handled_signals; ++i )
		::sigaction( k_handled_signals[i], &sa, &s_previous_actions[i] );
}

// --- scoped_fault_tracer state ---
std::mutex			s_tracer_mutex;
int					s_tracer_depth = 0;
char				s_tracer_label[128] = "fault";
struct sigaction	s_tracer_prev_segv = {};
struct sigaction	s_tracer_prev_bus = {};

void fault_tracer_signal_handler( int const sig, siginfo_t* const info, void* const uctx )
{
	write_str_fd( STDERR_FILENO, s_tracer_label );
	write_str_fd( STDERR_FILENO, ": signal=" );
	write_str_fd( STDERR_FILENO, signal_name( sig ) );
	char addr_buf[64] = {};
	std::snprintf( addr_buf, sizeof( addr_buf ), " address=%p\n", info ? info->si_addr : nullptr );
	write_str_fd( STDERR_FILENO, addr_buf );

	void* frames[32] = {};
	int const count = ::backtrace( frames, 32 );
	if ( count > 0 )
		::backtrace_symbols_fd( frames, count, STDERR_FILENO );

	struct sigaction const& prev = ( sig == SIGBUS ) ? s_tracer_prev_bus : s_tracer_prev_segv;
	if ( ( prev.sa_flags & SA_SIGINFO ) != 0 && prev.sa_sigaction && prev.sa_sigaction != &fault_tracer_signal_handler ) {
		prev.sa_sigaction( sig, info, uctx );
		return;
	}
	if ( ( prev.sa_flags & SA_SIGINFO ) == 0 && prev.sa_handler != SIG_DFL && prev.sa_handler != SIG_IGN && prev.sa_handler ) {
		prev.sa_handler( sig );
		return;
	}

	struct sigaction dfl = {};
	dfl.sa_handler = SIG_DFL;
	::sigemptyset( &dfl.sa_mask );
	::sigaction( sig, &dfl, nullptr );
	::raise( sig );
}

} // namespace

void set_show_dialog_for_unhandled_exceptions( bool const show )
{
	s_show_dialog_for_unhandled_exceptions = show;
}

bool show_dialog_for_unhandled_exceptions()
{
	return s_show_dialog_for_unhandled_exceptions &&
		( xray::debug::debug_engine() && !xray::debug::debug_engine()->terminate_on_error() );
}

namespace xray {
namespace debug {

void set_thread_stack_guarantee()
{
	constexpr std::size_t k_alt_stack_bytes = 64 * 1024;
	alignas( 16 ) thread_local unsigned char tl_alt_stack[k_alt_stack_bytes] = {};
	thread_local bool tl_installed = false;

	if ( tl_installed )
		return;

	stack_t ss = {};
	ss.ss_sp = tl_alt_stack;
	ss.ss_size = k_alt_stack_bytes;
	ss.ss_flags = 0;
	if ( ::sigaltstack( &ss, nullptr ) == 0 )
		tl_installed = true;
}

void protected_call( protected_function_type* const function_to_call, pvoid const argument )
{
	if ( !function_to_call )
		return;

	set_thread_stack_guarantee();

	sigjmp_buf* const prev_jmp = tl_protected_jmp;
	sigjmp_buf local_jmp;
	tl_caught_signal = 0;
	tl_fault_addr = 0;
	tl_protected_jmp = &local_jmp;

	if ( sigsetjmp( local_jmp, 1 ) == 0 ) {
		try {
			( *function_to_call )( argument );
		} catch ( std::exception const& ex ) {
			tl_protected_jmp = prev_jmp;
			if ( debug_engine() && debug_engine()->is_testing() ) {
				debug_engine()->on_testing_exception( xray::assert_untyped, ex.what(), nullptr, false );
				return;
			}
			FATAL( "unhandled C++ exception: %s", ex.what() );
		} catch ( ... ) {
			tl_protected_jmp = prev_jmp;
			if ( debug_engine() && debug_engine()->is_testing() ) {
				debug_engine()->on_testing_exception( xray::assert_untyped, "unknown C++ exception", nullptr, false );
				return;
			}
			FATAL( "unhandled unknown C++ exception" );
		}
		tl_protected_jmp = prev_jmp;
	} else {
		tl_protected_jmp = prev_jmp;
		int const sig = static_cast<int>( tl_caught_signal );
		std::uintptr_t const addr = tl_fault_addr;
		if ( debug_engine() && debug_engine()->is_testing() ) {
			char desc[256] = {};
			std::snprintf( desc, sizeof( desc ), "caught signal %s (%d) at 0x%zx",
				signal_name( sig ), sig, static_cast<std::size_t>( addr ) );
			debug_engine()->on_testing_exception( xray::assert_untyped, desc, nullptr, false );
		}
	}
}

bool write_crash_report( pcstr const output_file_name, void* const platform_context )
{
	if ( !output_file_name || !*output_file_name )
		return false;

	int const fd = ::open( output_file_name, O_WRONLY | O_CREAT | O_TRUNC, 0644 );
	if ( fd < 0 )
		return false;

	date_time const dt = current_local_date_time();
	char exe_path[1024] = {};
	ssize_t const exe_len = ::readlink( "/proc/self/exe", exe_path, sizeof( exe_path ) - 1 );
	if ( exe_len > 0 )
		exe_path[exe_len] = '\0';
	else
		std::snprintf( exe_path, sizeof( exe_path ), "<unknown>" );

	char header[2048] = {};
	int const hdr_len = std::snprintf(
		header,
		sizeof( header ),
		"XRAY_CRASH_REPORT v1\n"
		"timestamp=%04u-%02u-%02u %02u:%02u:%02u\n"
		"pid=%ld\n"
		"tid=%u\n"
		"executable=%s\n",
		static_cast<unsigned>( dt.year ),
		static_cast<unsigned>( dt.month ),
		static_cast<unsigned>( dt.day ),
		static_cast<unsigned>( dt.hour ),
		static_cast<unsigned>( dt.minute ),
		static_cast<unsigned>( dt.second ),
		static_cast<long>( ::getpid() ),
		current_thread_id(),
		exe_path
	);
	if ( hdr_len > 0 )
		write_all_fd( fd, header, static_cast<std::size_t>( std::min<int>( hdr_len, sizeof( header ) - 1 ) ) );

	auto const* crash_ctx = static_cast<posix_crash_context const*>( platform_context );
	if ( crash_ctx && crash_ctx->magic == k_crash_context_magic ) {
		char sig_buf[512] = {};
		int const sig_len = std::snprintf(
			sig_buf,
			sizeof( sig_buf ),
			"\n[SIGNAL]\nsignal=%d (%s)\nfault_address=%p\n",
			crash_ctx->signal_number,
			signal_name( crash_ctx->signal_number ),
			crash_ctx->siginfo ? crash_ctx->siginfo->si_addr : nullptr
		);
		if ( sig_len > 0 )
			write_all_fd( fd, sig_buf, static_cast<std::size_t>( std::min<int>( sig_len, sizeof( sig_buf ) - 1 ) ) );

#if defined(__x86_64__)
		if ( crash_ctx->ucontext ) {
			auto const& gregs = crash_ctx->ucontext->uc_mcontext.gregs;
			char reg_buf[1024] = {};
			int const reg_len = std::snprintf(
				reg_buf,
				sizeof( reg_buf ),
				"\n[REGISTERS_X86_64]\n"
				"RIP=0x%016llx RSP=0x%016llx RBP=0x%016llx\n"
				"RAX=0x%016llx RBX=0x%016llx RCX=0x%016llx RDX=0x%016llx\n"
				"RSI=0x%016llx RDI=0x%016llx R8 =0x%016llx R9 =0x%016llx\n"
				"R10=0x%016llx R11=0x%016llx R12=0x%016llx R13=0x%016llx\n"
				"R14=0x%016llx R15=0x%016llx EFLAGS=0x%016llx\n",
				static_cast<unsigned long long>( gregs[REG_RIP] ),
				static_cast<unsigned long long>( gregs[REG_RSP] ),
				static_cast<unsigned long long>( gregs[REG_RBP] ),
				static_cast<unsigned long long>( gregs[REG_RAX] ),
				static_cast<unsigned long long>( gregs[REG_RBX] ),
				static_cast<unsigned long long>( gregs[REG_RCX] ),
				static_cast<unsigned long long>( gregs[REG_RDX] ),
				static_cast<unsigned long long>( gregs[REG_RSI] ),
				static_cast<unsigned long long>( gregs[REG_RDI] ),
				static_cast<unsigned long long>( gregs[REG_R8] ),
				static_cast<unsigned long long>( gregs[REG_R9] ),
				static_cast<unsigned long long>( gregs[REG_R10] ),
				static_cast<unsigned long long>( gregs[REG_R11] ),
				static_cast<unsigned long long>( gregs[REG_R12] ),
				static_cast<unsigned long long>( gregs[REG_R13] ),
				static_cast<unsigned long long>( gregs[REG_R14] ),
				static_cast<unsigned long long>( gregs[REG_R15] ),
				static_cast<unsigned long long>( gregs[REG_EFL] )
			);
			if ( reg_len > 0 )
				write_all_fd( fd, reg_buf, static_cast<std::size_t>( std::min<int>( reg_len, sizeof( reg_buf ) - 1 ) ) );
		}
#endif
	}

	write_str_fd( fd, "\n[CALL_STACK]\n" );
	call_stack::iterate(
		nullptr,
		nullptr,
		[fd]( u32 const id, u32 const /*total*/, pcstr const mod, pcstr const file, int const line, pcstr const fn, size_t const addr ) {
			char frame_buf[1024] = {};
			int const len = ( line > 0 )
				? std::snprintf( frame_buf, sizeof( frame_buf ), "#%02u %s:%d %s (%s) [0x%zx]\n",
					id, file ? file : "?", line, fn ? fn : "?", mod ? mod : "?", addr )
				: std::snprintf( frame_buf, sizeof( frame_buf ), "#%02u %s!%s [0x%zx]\n",
					id, mod ? mod : "?", fn ? fn : "?", addr );
			if ( len > 0 )
				write_all_fd( fd, frame_buf, static_cast<std::size_t>( std::min<int>( len, sizeof( frame_buf ) - 1 ) ) );
			return true;
		},
		false
	);

	write_str_fd( fd, "\n[MEMORY_MAPS]\n" );
	int const maps_fd = ::open( "/proc/self/maps", O_RDONLY );
	if ( maps_fd >= 0 ) {
		char maps_buf[4096];
		std::size_t remaining_budget = 64 * 1024;
		while ( remaining_budget > 0 ) {
			ssize_t const n = ::read( maps_fd, maps_buf, std::min<std::size_t>( sizeof( maps_buf ), remaining_budget ) );
			if ( n <= 0 )
				break;
			write_all_fd( fd, maps_buf, static_cast<std::size_t>( n ) );
			remaining_budget -= static_cast<std::size_t>( n );
		}
		::close( maps_fd );
	} else {
		write_str_fd( fd, "<unavailable>\n" );
	}

	::close( fd );
	return true;
}

scoped_fault_tracer::scoped_fault_tracer( pcstr const label, bool const enabled )
{
	if ( !enabled )
		return;

	std::lock_guard<std::mutex> lock( s_tracer_mutex );
	if ( label && *label )
		std::snprintf( s_tracer_label, sizeof( s_tracer_label ), "%s", label );

	if ( s_tracer_depth == 0 ) {
		struct sigaction sa = {};
		sa.sa_sigaction = &fault_tracer_signal_handler;
		sa.sa_flags = SA_SIGINFO | SA_ONSTACK;
		::sigemptyset( &sa.sa_mask );
		::sigaction( SIGSEGV, &sa, &s_tracer_prev_segv );
		::sigaction( SIGBUS, &sa, &s_tracer_prev_bus );
	}
	++s_tracer_depth;
	m_handle = this;
}

scoped_fault_tracer::~scoped_fault_tracer()
{
	if ( !m_handle )
		return;

	std::lock_guard<std::mutex> lock( s_tracer_mutex );
	if ( s_tracer_depth > 0 ) {
		--s_tracer_depth;
		if ( s_tracer_depth == 0 ) {
			::sigaction( SIGSEGV, &s_tracer_prev_segv, nullptr );
			::sigaction( SIGBUS, &s_tracer_prev_bus, nullptr );
		}
	}
	m_handle = nullptr;
}

namespace platform {

void setup_unhandled_exception_handler( error_mode const mode )
{
	s_error_mode = mode;
	set_thread_stack_guarantee();
	install_signal_handlers();
}

void change_unhandled_exception_handler( error_mode const mode )
{
	s_error_mode = mode;
}

void format_message()
{
	int const err = errno;
	if ( !err )
		return;

	char msg[512] = {};
	if ( format_system_error( err, msg, sizeof( msg ) ) ) {
		if ( log_callback const cb = get_log_callback() ) {
			string1024 formatted = {};
			std::snprintf( formatted, sizeof( formatted ), "%d: %s", err, msg );
			cb( "debug", true, false, formatted );
		}
	}
}

void prologue_dump_call_stack( _EXCEPTION_POINTERS* const exception_information )
{
	if ( !error_after_dialog() )
		dump_call_stack( "debug", true, 0, 0, exception_information );
}

void save_minidump( date_time const& time_stamp, _EXCEPTION_POINTERS* const exception_information )
{
	if ( !debug_engine() )
		return;

	string_path report_file = {};
	debug_engine()->generate_debug_file_name( report_file, &time_stamp, "error_report", ".crash.txt" );
	debug_engine()->create_folder_r( report_file, false );
	write_crash_report( report_file, exception_information );
}

} // namespace platform

namespace bugtrap {

void setup_unhandled_exception_handler()
{
}

} // namespace bugtrap

} // namespace debug
} // namespace xray
