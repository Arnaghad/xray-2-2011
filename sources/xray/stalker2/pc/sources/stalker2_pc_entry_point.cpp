////////////////////////////////////////////////////////////////////////////
//	Created 	: 26.08.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "stalker2_pc_application.h"
#include <xray/os_platform.h>
#include <string>

static xray::uninitialized_reference<stalker2::application>	s_application;

static int s_exit_code					= 0;

struct main_args {
	std::string command_line;
};

struct guard {
	inline	guard						( pcstr const command_line )
	{
		XRAY_CONSTRUCT_REFERENCE		( s_application, stalker2::application );
		s_application->initialize		( command_line );
	}

	inline	~guard						( )
	{
		s_exit_code						= s_application->get_exit_code();
		s_application->finalize			( );
		XRAY_DESTROY_REFERENCE			( s_application );
	}
}; // struct guard

static void	main_protected				( pvoid const raw_args )
{
	auto const* const args				= static_cast<main_args const*>( raw_args );
	guard								guard( args ? args->command_line.c_str() : "" );

	if ( !s_application->get_exit_code() )
		s_application->execute			( );
}

static std::string join_command_line( int const argc, char** const argv )
{
	std::string result;
	for ( int i = 0; i < argc; ++i ) {
		if ( i > 0 )
			result.push_back( ' ' );
		char const* const arg = argv[i] ? argv[i] : "";
		bool const quote = std::string_view( arg ).find_first_of( " \t\"" ) != std::string_view::npos;
		if ( quote )
			result.push_back( '"' );
		result.append( arg );
		if ( quote )
			result.push_back( '"' );
	}
	return result;
}

int main								( int argc, char** argv )
{
	main_args args{ join_command_line( argc, argv ) };
	xray::debug::protected_call			( &main_protected, &args );
	return								( s_exit_code );
}

#if XRAY_PLATFORM_WINDOWS
extern "C" __declspec(dllimport) char* __stdcall GetCommandLineA( void );

struct HINSTANCE__;
typedef HINSTANCE__* HINSTANCE;

int __stdcall WinMain					(
		HINSTANCE hInstance,
		HINSTANCE hPrevInstance,
		pstr	  lpCmdLine,
		int       nCmdShow
	)
{
	XRAY_UNREFERENCED_PARAMETERS		( hInstance, hPrevInstance, nCmdShow, lpCmdLine );

	main_args args{ GetCommandLineA() ? GetCommandLineA() : "" };
	xray::debug::protected_call			( &main_protected, &args );
	return								( s_exit_code );
}
#endif
