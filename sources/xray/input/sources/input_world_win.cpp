////////////////////////////////////////////////////////////////////////////
//	Created 	: 20.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "input_world.h"
#include "receiver_gamepad.h"
#include "receiver_mouse.h"
#include "receiver_keyboard.h"
#include <xray/input/handler.h>
#include <xray/input_event_queue.h>

using xray::input::input_world;

void input_world::create_devices	( HWND const window_handle )
{
	m_target_window			= window_handle;
	m_gamepad				= NEW ( receiver::gamepad ) ( 0, *this );
	m_keyboard				= NEW ( receiver::keyboard ) ( window_handle, *this );
	m_mouse					= NEW ( receiver::mouse ) ( window_handle, *this );
}

void input_world::feed_sdl_event	( SDL_Event const& ev )
{
	if ( m_keyboard )
		m_keyboard->on_sdl_event( ev );
	if ( m_mouse )
		m_mouse->on_sdl_event	( ev );
	if ( m_gamepad )
		m_gamepad->on_sdl_event	( ev );
}

void input_world::pump_events	( )
{
	normalized_input_event ev;
	while ( pop_normalized_input_event( ev ) ) {
		switch ( ev.kind ) {
			case normalized_event_kind::key_down:
				if ( m_keyboard ) m_keyboard->on_sdl_scancode( ev.code, true );
				break;
			case normalized_event_kind::key_up:
				if ( m_keyboard ) m_keyboard->on_sdl_scancode( ev.code, false );
				break;
			case normalized_event_kind::mouse_motion:
				if ( m_mouse ) m_mouse->on_motion( ev.x, ev.y );
				break;
			case normalized_event_kind::mouse_button_down:
				if ( m_mouse ) m_mouse->on_button( static_cast<mouse_button>( ev.code ), true );
				break;
			case normalized_event_kind::mouse_button_up:
				if ( m_mouse ) m_mouse->on_button( static_cast<mouse_button>( ev.code ), false );
				break;
			case normalized_event_kind::mouse_wheel:
				if ( m_mouse ) m_mouse->on_wheel( ev.z );
				break;
			case normalized_event_kind::gamepad_added:
				if ( m_gamepad ) m_gamepad->on_gamepad_added( static_cast<SDL_JoystickID>( ev.device_id ) );
				break;
			case normalized_event_kind::gamepad_removed:
				if ( m_gamepad ) m_gamepad->on_gamepad_removed( static_cast<SDL_JoystickID>( ev.device_id ) );
				break;
			case normalized_event_kind::focus_gained:
				process_on_activate();
				break;
			case normalized_event_kind::focus_lost:
				process_on_deactivate();
				break;
		}
	}
}

void input_world::destroy_devices	( )
{
	clear_normalized_input_events();
	XRAY_DELETE_IMPL		( *g_allocator, m_mouse );
	XRAY_DELETE_IMPL		( *g_allocator, m_keyboard );
	XRAY_DELETE_IMPL		( *g_allocator, m_gamepad );
}
