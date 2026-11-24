////////////////////////////////////////////////////////////////////////////
//	Created 	: 06.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <windows.h>
#include <xray/debug/extensions.h>
#include "utils.h"
#include "bugtrap.h"

#include <string>
#include <vector>

using xray::debug::bugtrap_usage;
using xray::debug::error_mode;

static error_mode				s_error_mode		= xray::debug::error_mode_verbose;
static bugtrap_usage			s_bugtrap_usage		= xray::debug::no_bugtrap;
static bool						s_initialized		= false;
static std::vector<std::string>	s_attached_files;

bool xray::debug::bugtrap::initialized		( )
{
	return							( s_initialized );
}

void xray::debug::bugtrap::change_usage		( error_mode error_mode, bugtrap_usage bugtrap_usage )
{
	s_bugtrap_usage					= bugtrap_usage;
	s_error_mode					= error_mode;
}

void xray::debug::bugtrap::initialize		( )
{
	if ( s_initialized )
		return;

	xray::debug::bugtrap::setup_unhandled_exception_handler( );
	s_initialized					= true;
}

void xray::debug::bugtrap::finalize			( )
{
	s_attached_files.clear			( );
	s_initialized					= false;
}

bool xray::debug::bugtrap::on_thread_spawn	( )
{
	return							( true );
}

void xray::debug::bugtrap::add_file			( pcstr full_path_file_name )
{
	if ( !full_path_file_name || !*full_path_file_name )
		return;

	s_attached_files.emplace_back	( full_path_file_name );
	xray::debug::log_callback const log_callback = xray::debug::get_log_callback();
	if ( log_callback ) {
		string512 message;
		sprintf_s					( message, "diagnostics artifact attached: %s", full_path_file_name );
		log_callback				( "debug:diagnostics", false, false, message );
	}
}

void set_show_dialog_for_unhandled_exceptions	( bool show );

void xray::debug::on_error					( pcstr message )
{
	if ( !bugtrap::initialized( ) )
		bugtrap::initialize			( );

	if ( message && *message ) {
		log_callback const cb		= get_log_callback();
		if ( cb )
			cb						( "debug:diagnostics", true, false, message );
	}

	set_show_dialog_for_unhandled_exceptions	( false );
}
