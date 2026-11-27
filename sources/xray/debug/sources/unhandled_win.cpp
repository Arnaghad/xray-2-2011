////////////////////////////////////////////////////////////////////////////
//	Created 	: 07.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <windows.h>
#include <stdio.h>
#include <dbghelp.h>
#include <iostream>
#include <xray/debug/extensions.h>
#include <xray/debug/call_stack.h>
#include <xray/os_raii_win.h>
#include "platform.h"
#include "utils.h"
#include "bugtrap.h"

#ifndef INVALID_HANDLE_VALUE
#define INVALID_HANDLE_VALUE ((HANDLE)(LONG_PTR)-1)
#endif

bool xray::debug::write_local_minidump			( pcstr const output_file_name, _EXCEPTION_POINTERS* const exception_information )
{
	if ( !output_file_name || !*output_file_name )
		return						false;

	xray::os::unique_handle minidump_handle(
		CreateFileA( output_file_name, GENERIC_WRITE, FILE_SHARE_WRITE, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr )
	);
	if ( !minidump_handle.valid() )
		return						false;

	_MINIDUMP_EXCEPTION_INFORMATION info = {};
	PMINIDUMP_EXCEPTION_INFORMATION info_ptr = nullptr;
	if ( exception_information ) {
		info.ThreadId				= ::GetCurrentThreadId();
		info.ExceptionPointers		= exception_information;
		info.ClientPointers			= FALSE;
		info_ptr					= &info;
	}

	BOOL const ok = MiniDumpWriteDump(
		GetCurrentProcess(),
		GetCurrentProcessId(),
		minidump_handle.get(),
		MINIDUMP_TYPE(
			MiniDumpWithDataSegs |
			MiniDumpWithIndirectlyReferencedMemory |
			MiniDumpWithThreadInfo
		),
		info_ptr,
		nullptr,
		nullptr
	);
	return							ok != FALSE;
}

static void save_minidump						( pcstr const output_file_name, _EXCEPTION_POINTERS* const exception_information )
{
	if ( xray::debug::debug_engine() )
		xray::debug::debug_engine()->create_folder_r( output_file_name, false );

	xray::debug::write_local_minidump( output_file_name, exception_information );
}

void xray::debug::platform::format_message	( )
{
    DWORD const error_code		= GetLastError( ); 

	if ( !error_code )
		return;

    pstr						message = 0;
    FormatMessage				(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM,
        0,
        error_code,
        MAKELANGID ( LANG_NEUTRAL, SUBLANG_DEFAULT ),
        (pstr)&message,
        0,
		0
	);

	log_callback const log			=	get_log_callback();
	if ( log )
	{
		string4096				error_message;
		sprintf_s				(error_message, "%d: %s", error_code, message);
		log						( "debug", true, false, error_message);
	}
    LocalFree					( message );
}

void xray::debug::platform::save_minidump		( date_time const& time_stamp, _EXCEPTION_POINTERS* exception_information )
{
	string_path					temp;
	debug_engine()->generate_debug_file_name	( temp, &time_stamp, "error_report", ".dmp");

	string_path					minidump_file_name;
	strcpy_s					( minidump_file_name, temp );
	save_minidump				( minidump_file_name, exception_information );
	bugtrap::add_file			( minidump_file_name );
}

bool xray::debug::write_crash_report			( pcstr const output_file_name, void* const platform_context )
{
	return write_local_minidump( output_file_name, static_cast<_EXCEPTION_POINTERS*>( platform_context ) );
}

namespace {

char s_win_fault_tracer_label[128] = "fault";

LONG CALLBACK win_fault_tracer_handler( EXCEPTION_POINTERS* const exception )
{
	if ( !exception || !exception->ExceptionRecord || exception->ExceptionRecord->ExceptionCode != EXCEPTION_ACCESS_VIOLATION )
		return EXCEPTION_CONTINUE_SEARCH;

	std::cerr << s_win_fault_tracer_label << ": address=" << exception->ExceptionRecord->ExceptionAddress << '\n';
	void* frames[32]{};
	auto const count = CaptureStackBackTrace( 0, 32, frames, nullptr );
	for ( unsigned i = 0; i < count; ++i ) {
		HMODULE module = nullptr;
		char path[MAX_PATH]{};
		GetModuleHandleExA(
			GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
			static_cast<char const*>( frames[i] ),
			&module
		);
		if ( module )
			GetModuleFileNameA( module, path, MAX_PATH );
		std::cerr << "fault frame: " << frames[i] << " module=" << path
			<< " offset=" << reinterpret_cast<void*>( reinterpret_cast<uintptr_t>( frames[i] ) - reinterpret_cast<uintptr_t>( module ) ) << '\n';
	}
	return EXCEPTION_CONTINUE_SEARCH;
}

} // namespace

xray::debug::scoped_fault_tracer::scoped_fault_tracer( pcstr const label, bool const enabled )
{
	if ( !enabled )
		return;
	if ( label && *label )
		std::snprintf( s_win_fault_tracer_label, sizeof( s_win_fault_tracer_label ), "%s", label );
	m_handle = AddVectoredExceptionHandler( 1, &win_fault_tracer_handler );
}

xray::debug::scoped_fault_tracer::~scoped_fault_tracer()
{
	if ( m_handle ) {
		RemoveVectoredExceptionHandler( m_handle );
		m_handle = nullptr;
	}
}

static inline u32 execute_handler_filter( ... )
{
	return						EXCEPTION_EXECUTE_HANDLER;
}

void xray::debug::platform::prologue_dump_call_stack	( _EXCEPTION_POINTERS* exception_information )
{
#if defined(_MSC_VER)
	__try {
		if ( !xray::debug::platform::error_after_dialog( ) )
			xray::debug::dump_call_stack	( "debug", true, 0, 0, exception_information );
	} __except ( execute_handler_filter( GetExceptionCode( ), GetExceptionInformation( ) ) ) {
		(void)0;
	}
#else
	if ( !xray::debug::platform::error_after_dialog( ) )
		xray::debug::dump_call_stack	( "debug", true, 0, 0, exception_information );
#endif
}

void xray::debug::set_thread_stack_guarantee			( )
{
	unsigned long				stack_size = 32*1024;

	typedef BOOL ( __stdcall * SetThreadStackGuarantee_type ) (PULONG);
	static SetThreadStackGuarantee_type set_thread_stack_guarantee = 0;

	static bool initialized			= false;
	if ( !initialized ) {
		initialized					= true;
		HMODULE const kernel32		= LoadLibrary("kernel32.dll");
		R_ASSERT					(kernel32);
		set_thread_stack_guarantee	= ( SetThreadStackGuarantee_type )GetProcAddress( kernel32, "SetThreadStackGuarantee" );
	}

	if ( !set_thread_stack_guarantee )
		return;

	BOOL const result				= set_thread_stack_guarantee ( &stack_size );
	XRAY_UNREFERENCED_PARAMETER		( result );
	R_ASSERT						( result );
}
