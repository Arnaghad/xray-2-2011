////////////////////////////////////////////////////////////////////////////
//	Created 	: 20.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "network_world.h"
#include "io_service.h"

#ifndef MASTER_GOLD
static xray::command_line::key	s_no_network_key		("no_network", "", "", "disable network");
#endif // #ifndef MASTER_GOLD
static xray::command_line::key	s_enable_network_key	("enable_network", "", "network", "enable network io_service at startup");

using xray::network::network_world;

network_world::network_world		( xray::network::engine& engine ) :
	m_engine					( engine ),
	m_ioservice					( nullptr )
{
	bool enabled				= s_enable_network_key.is_set();
#ifndef MASTER_GOLD
	if ( s_no_network_key.is_set() )
		enabled					= false;
#endif // #ifndef MASTER_GOLD
	if ( enabled )
		m_ioservice				= XN_NEW(lowlevel::io_service);
}

network_world::~network_world		( )
{
	if ( m_ioservice ) {
		XN_DELETE				( m_ioservice );
		m_ioservice				= nullptr;
	}
}

void network_world::tick			( u32 const logic_frame_id )
{
	XRAY_UNREFERENCED_PARAMETER	( logic_frame_id );
	if ( m_ioservice )
		m_ioservice->tick		( );
}

void network_world::clear_resources	( )
{
}
