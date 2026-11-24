////////////////////////////////////////////////////////////////////////////
//	Created 	: 07.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <windows.h>
#include <stdio.h>
#include <dbghelp.h>
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

void xray::debug::platform::save_minidump		( SYSTEMTIME const& date_time, _EXCEPTION_POINTERS* exception_information )
{
	string_path					temp;
	debug_engine()->generate_debug_file_name	( temp, & date_time, "error_report", ".dmp");

	string_path					minidump_file_name;
	strcpy_s					( minidump_file_name, temp );
	save_minidump				( minidump_file_name, exception_information );
	bugtrap::add_file			( minidump_file_name );
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
