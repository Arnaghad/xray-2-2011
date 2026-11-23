////////////////////////////////////////////////////////////////////////////
//	Created 	: 20.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "network_world.h"

#if 1
#	include "io_service.h"
#endif // #if 1

#ifndef MASTER_GOLD
static xray::command_line::key	s_no_network_key	("no_network", "", "", "disable network");
#endif // #ifndef MASTER_GOLD

using xray::network::network_world;

network_world::network_world		( xray::network::engine& engine ) :
	m_engine					( engine )
{
#ifndef MASTER_GOLD
	if ( !s_no_network_key.is_set() )
#endif // #ifndef MASTER_GOLD
#if 1
		m_ioservice				= XN_NEW(lowlevel::io_service);	
#else // #if 1
		(void)0;
#endif // #if 1
}

network_world::~network_world		( )
{
#if 1
	XN_DELETE					( m_ioservice );
#endif // #if 1
}

void network_world::tick			( u32 const logic_frame_id )
{
	XRAY_UNREFERENCED_PARAMETER	( logic_frame_id );
#ifndef MASTER_GOLD
	if ( !s_no_network_key.is_set() )
#endif // #ifndef MASTER_GOLD
#if 1
		m_ioservice->tick		( );
#else // #if 1
		(void)0;
#endif // #if 1
}

void network_world::clear_resources	( )
{
}
