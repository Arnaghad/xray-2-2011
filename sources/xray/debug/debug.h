////////////////////////////////////////////////////////////////////////////
//	Created 	: 06.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_DEBUG_H_INCLUDED
#define XRAY_DEBUG_H_INCLUDED

#include <xray/macro_platform.h>
#include <xray/macro_compiler.h>
#include <xray/debug/api.h>

struct _exception;
struct _EXCEPTION_POINTERS;

namespace xray {

enum assert_enum : int;
enum process_error_enum : int;

namespace logging {
	enum verbosity : int;
} // namespace logging

namespace debug {

struct engine;

enum error_mode : int {
	error_mode_silent,
	error_mode_verbose,
}; // enum error_mode

enum bugtrap_usage : int {
	no_bugtrap,
	native_bugtrap,
	managed_bugtrap,
}; // enum error_mode

XRAY_DEBUG_API	void			preinitialize		( );
XRAY_DEBUG_API	void			initialize			( debug::engine * engine );
XRAY_DEBUG_API	void			finalize			( );

XRAY_DEBUG_API	void XRAY_CDECL	on_error			( bool* do_debug_break, process_error_enum process_error, bool* ignore_always, assert_enum assert_type, pcstr reason, pcstr expression, pcstr file, pcstr function, u32 line, pcstr format, ... );
XRAY_DEBUG_API	void			on_error			( bool* do_debug_break, process_error_enum process_error, bool* ignore_always, assert_enum assert_type, pcstr reason, pcstr expression, pcstr file, pcstr function, u32 line );

XRAY_DEBUG_API	void XRAY_CDECL	on_error			( bool* do_debug_break, process_error_enum process_error, bool* ignore_always, int error_code, pcstr expression, pcstr file, pcstr function, u32 line, pcstr format, ... );
XRAY_DEBUG_API	void			on_error			( bool* do_debug_break, process_error_enum process_error, bool* ignore_always, int error_code, pcstr expression, pcstr file, pcstr function, u32 line );

XRAY_DEBUG_API	void XRAY_CDECL	terminate			( pcstr format, ... );
XRAY_DEBUG_API	void XRAY_CDECL	terminate			( u32 exit_code, pcstr format, ... );

XRAY_DEBUG_API	void			output				( pcstr message );
XRAY_DEBUG_API	void XRAY_CDECL	printf				( pcstr format, ... );

XRAY_DEBUG_API	void			change_bugtrap_usage( debug::error_mode, debug::bugtrap_usage );

XRAY_DEBUG_API	void			dump_call_stack		( pcstr						initiator, 
													  bool						use_error_verbosity, 
													  u32 						num_first_to_ignore, 
													  u32 						num_last_to_ignore, 
													  _EXCEPTION_POINTERS*		pointers, 
													  void*						call_stack[] = 0);

XRAY_DEBUG_API	void			log_call_stack		( pcstr header );

#ifdef _WIN32
XRAY_DEBUG_API	bool			write_local_minidump( pcstr output_file_name, _EXCEPTION_POINTERS* exception_information );
#endif
XRAY_DEBUG_API	bool			write_crash_report	( pcstr output_file_name, void* platform_context = nullptr );

typedef void protected_function_type				( pvoid );
XRAY_DEBUG_API	void			protected_call		( protected_function_type* function_to_call, pvoid arguments );

XRAY_DEBUG_API	int				on_math_error		( struct ::_exception* exception );

XRAY_DEBUG_API	bool			is_debugger_present	( );
XRAY_DEBUG_API	void			debug_break			( );
XRAY_DEBUG_API	void			show_error_dialog	( pcstr title, pcstr message );
XRAY_DEBUG_API	bool			format_system_error	( int error_code, char* buffer, u32 buffer_size );

class XRAY_DEBUG_API scoped_fault_tracer
{
public:
	explicit scoped_fault_tracer( pcstr label, bool enabled = true );
	~scoped_fault_tracer();

	scoped_fault_tracer( scoped_fault_tracer const& ) = delete;
	scoped_fault_tracer& operator=( scoped_fault_tracer const& ) = delete;

	bool active() const noexcept { return m_handle != nullptr; }

private:
	pvoid m_handle = nullptr;
};

#if XRAY_PLATFORM_WINDOWS
	// Явне визначення типів Windows API, щоб уникнути підключення <windows.h>
	typedef unsigned long DWORD;

	XRAY_DEBUG_API	int		unhandled_exception_filter	( DWORD const exception_code, 
													 _EXCEPTION_POINTERS* const exception_information );
#endif

XRAY_DEBUG_API	void			debug_message_box	( pcstr message );

#ifdef DEBUG
XRAY_DEBUG_API	bool			is_fpe_enabled		( );
XRAY_DEBUG_API	void			enable_fpe			( bool value );
#endif // #ifdef DEBUG

} // namespace debug
} // namespace xray

#endif // #ifndef XRAY_DEBUG_H_INCLUDED
