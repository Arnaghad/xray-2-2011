////////////////////////////////////////////////////////////////////////////
//	Created 	: 26.08.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "stalker2_pc_application.h"
#include <xray/engine/api.h>

using stalker2::application;

void application::initialize( )
{
	initialize						( "" );
}

void application::initialize( pcstr const command_line )
{
	m_exit_code						= 0;
	m_command_line					= command_line ? command_line : "";

	preinitialize					( );
	xray::engine::initialize		( );
	postinitialize					( );
}

void application::finalize	( )
{
	m_splash_screen.reset			( );
	xray::engine::finalize			( );
}

void application::execute	( )
{
	xray::engine::execute			( );
	m_exit_code						= xray::engine::get_exit_code();
}
