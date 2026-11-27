////////////////////////////////////////////////////////////////////////////
//	Created 	: 21.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "receiver_mouse.h"
#include <xray/input/handler.h>

using xray::input::receiver::mouse;
using xray::input::handler;
using xray::input::world;

mouse::mouse			( xray::platform::native_surface_handle const window_handle, world& input_world )
:	m_window_handle		( window_handle ),
	m_world				( input_world ),
	m_pending_x			( 0 ),
	m_pending_y			( 0 ),
	m_pending_z			( 0 ),
	m_live_buttons		( 0 ),
	m_active			( true )
{
	memory::zero		( &m_current_state,  sizeof(m_current_state)  );
	memory::zero		( &m_previous_state, sizeof(m_previous_state) );
}

mouse::~mouse			( )
{
}

xray::input::mouse::state const& mouse::get_state	( ) const
{
	return				( m_current_state );
}

void mouse::on_activate	( )
{
	m_active			= true;
}

void mouse::on_deactivate ( )
{
	m_active			= false;
	m_pending_x			= 0;
	m_pending_y			= 0;
	m_pending_z			= 0;
	m_live_buttons		= 0;
	memory::zero		( &m_current_state,  sizeof(m_current_state)  );
	memory::zero		( &m_previous_state, sizeof(m_previous_state) );
}

void mouse::on_motion		( float const dx, float const dy )
{
	if ( !m_active )
		return;

	m_pending_x			+= static_cast<long>( dx >= 0.f ? dx + 0.5f : dx - 0.5f );
	m_pending_y			+= static_cast<long>( dy >= 0.f ? dy + 0.5f : dy - 0.5f );
}

void mouse::on_wheel		( float const dz )
{
	if ( !m_active )
		return;

	m_pending_z			+= static_cast<long>( dz >= 0.f ? dz + 0.5f : dz - 0.5f );
}

void mouse::on_button		( xray::input::mouse_button const btn, bool const down )
{
	if ( !m_active )
		return;

	if ( down )
		m_live_buttons	|= static_cast<u8>( btn );
	else
		m_live_buttons	&= static_cast<u8>( ~btn );
}

void mouse::on_sdl_event	( SDL_Event const& ev )
{
	switch ( ev.type ) {
		case SDL_EVENT_MOUSE_MOTION:
			on_motion( ev.motion.xrel, ev.motion.yrel );
			break;
		case SDL_EVENT_MOUSE_WHEEL:
			on_wheel( ev.wheel.y * 120.f );
			break;
		case SDL_EVENT_MOUSE_BUTTON_DOWN:
		case SDL_EVENT_MOUSE_BUTTON_UP: {
			xray::input::mouse_button btn = xray::input::mouse_button_left;
			switch ( ev.button.button ) {
				case SDL_BUTTON_LEFT:   btn = xray::input::mouse_button_left; break;
				case SDL_BUTTON_RIGHT:  btn = xray::input::mouse_button_right; break;
				case SDL_BUTTON_MIDDLE: btn = xray::input::mouse_button_middle; break;
				case SDL_BUTTON_X1:     btn = xray::input::mouse_button_extended0; break;
				case SDL_BUTTON_X2:     btn = xray::input::mouse_button_extended1; break;
				default:                return;
			}
			on_button( btn, ev.button.down );
			break;
		}
		default:
			break;
	}
}

void mouse::execute		( )
{
	memory::copy		( &m_previous_state, sizeof(m_previous_state), &m_current_state, sizeof( m_current_state ) );
	m_current_state.x	= m_pending_x;
	m_current_state.y	= m_pending_y;
	m_current_state.z	= m_pending_z;
	m_current_state.buttons = m_live_buttons;
	m_pending_x			= 0;
	m_pending_y			= 0;
	m_pending_z			= 0;
}
