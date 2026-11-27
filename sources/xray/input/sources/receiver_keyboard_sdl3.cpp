////////////////////////////////////////////////////////////////////////////
//	Created 	: 21.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "receiver_keyboard.h"
#include <xray/input/handler.h>

using xray::input::receiver::keyboard;
using xray::input::handler;
using xray::input::enum_keyboard;
using xray::input::world;

keyboard::keyboard			( xray::platform::native_surface_handle const window_handle, world& input_world ) :
	m_window_handle		( window_handle ),
	m_world				( input_world ),
	m_active			( true )
{
	memory::zero		( &m_live_key_buffer,     sizeof(m_live_key_buffer)     );
	memory::zero		( &m_current_key_buffer,  sizeof(m_current_key_buffer)  );
	memory::zero		( &m_previous_key_buffer, sizeof(m_previous_key_buffer) );
}

keyboard::~keyboard			( )
{
}

void keyboard::on_activate	( )
{
	m_active			= true;
}

void keyboard::on_deactivate( )
{
	m_active			= false;
	memory::zero		( &m_live_key_buffer,     sizeof(m_live_key_buffer)     );
	memory::zero		( &m_current_key_buffer,  sizeof(m_current_key_buffer)  );
	memory::zero		( &m_previous_key_buffer, sizeof(m_previous_key_buffer) );
}

enum_keyboard keyboard::translate_sdl_scancode	( SDL_Scancode const scancode )
{
	using namespace xray::input;
	switch ( scancode ) {
		case SDL_SCANCODE_ESCAPE:       return key_escape;
		case SDL_SCANCODE_1:            return key_1;
		case SDL_SCANCODE_2:            return key_2;
		case SDL_SCANCODE_3:            return key_3;
		case SDL_SCANCODE_4:            return key_4;
		case SDL_SCANCODE_5:            return key_5;
		case SDL_SCANCODE_6:            return key_6;
		case SDL_SCANCODE_7:            return key_7;
		case SDL_SCANCODE_8:            return key_8;
		case SDL_SCANCODE_9:            return key_9;
		case SDL_SCANCODE_0:            return key_0;
		case SDL_SCANCODE_MINUS:        return key_minus;
		case SDL_SCANCODE_EQUALS:       return key_equals;
		case SDL_SCANCODE_BACKSPACE:    return key_back;
		case SDL_SCANCODE_TAB:          return key_tab;
		case SDL_SCANCODE_Q:            return key_q;
		case SDL_SCANCODE_W:            return key_w;
		case SDL_SCANCODE_E:            return key_e;
		case SDL_SCANCODE_R:            return key_r;
		case SDL_SCANCODE_T:            return key_t;
		case SDL_SCANCODE_Y:            return key_y;
		case SDL_SCANCODE_U:            return key_u;
		case SDL_SCANCODE_I:            return key_i;
		case SDL_SCANCODE_O:            return key_o;
		case SDL_SCANCODE_P:            return key_p;
		case SDL_SCANCODE_LEFTBRACKET:  return key_lbracket;
		case SDL_SCANCODE_RIGHTBRACKET: return key_rbracket;
		case SDL_SCANCODE_RETURN:       return key_return;
		case SDL_SCANCODE_LCTRL:        return key_lcontrol;
		case SDL_SCANCODE_A:            return key_a;
		case SDL_SCANCODE_S:            return key_s;
		case SDL_SCANCODE_D:            return key_d;
		case SDL_SCANCODE_F:            return key_f;
		case SDL_SCANCODE_G:            return key_g;
		case SDL_SCANCODE_H:            return key_h;
		case SDL_SCANCODE_J:            return key_j;
		case SDL_SCANCODE_K:            return key_k;
		case SDL_SCANCODE_L:            return key_l;
		case SDL_SCANCODE_SEMICOLON:    return key_semicolon;
		case SDL_SCANCODE_APOSTROPHE:   return key_apostrophe;
		case SDL_SCANCODE_GRAVE:        return key_grave;
		case SDL_SCANCODE_LSHIFT:       return key_lshift;
		case SDL_SCANCODE_BACKSLASH:    return key_backslash;
		case SDL_SCANCODE_Z:            return key_z;
		case SDL_SCANCODE_X:            return key_x;
		case SDL_SCANCODE_C:            return key_c;
		case SDL_SCANCODE_V:            return key_v;
		case SDL_SCANCODE_B:            return key_b;
		case SDL_SCANCODE_N:            return key_n;
		case SDL_SCANCODE_M:            return key_m;
		case SDL_SCANCODE_COMMA:        return key_comma;
		case SDL_SCANCODE_PERIOD:       return key_period;
		case SDL_SCANCODE_SLASH:        return key_slash;
		case SDL_SCANCODE_RSHIFT:       return key_rshift;
		case SDL_SCANCODE_KP_MULTIPLY:  return key_multiply;
		case SDL_SCANCODE_LALT:         return key_lmenu;
		case SDL_SCANCODE_SPACE:        return key_space;
		case SDL_SCANCODE_CAPSLOCK:     return key_capital;
		case SDL_SCANCODE_F1:           return key_f1;
		case SDL_SCANCODE_F2:           return key_f2;
		case SDL_SCANCODE_F3:           return key_f3;
		case SDL_SCANCODE_F4:           return key_f4;
		case SDL_SCANCODE_F5:           return key_f5;
		case SDL_SCANCODE_F6:           return key_f6;
		case SDL_SCANCODE_F7:           return key_f7;
		case SDL_SCANCODE_F8:           return key_f8;
		case SDL_SCANCODE_F9:           return key_f9;
		case SDL_SCANCODE_F10:          return key_f10;
		case SDL_SCANCODE_NUMLOCKCLEAR: return key_numlock;
		case SDL_SCANCODE_SCROLLLOCK:   return key_scroll;
		case SDL_SCANCODE_KP_7:         return key_numpad7;
		case SDL_SCANCODE_KP_8:         return key_numpad8;
		case SDL_SCANCODE_KP_9:         return key_numpad9;
		case SDL_SCANCODE_KP_MINUS:     return key_subtract;
		case SDL_SCANCODE_KP_4:         return key_numpad4;
		case SDL_SCANCODE_KP_5:         return key_numpad5;
		case SDL_SCANCODE_KP_6:         return key_numpad6;
		case SDL_SCANCODE_KP_PLUS:      return key_add;
		case SDL_SCANCODE_KP_1:         return key_numpad1;
		case SDL_SCANCODE_KP_2:         return key_numpad2;
		case SDL_SCANCODE_KP_3:         return key_numpad3;
		case SDL_SCANCODE_KP_0:         return key_numpad0;
		case SDL_SCANCODE_KP_PERIOD:    return key_decimal;
		case SDL_SCANCODE_NONUSBACKSLASH: return key_oem_102;
		case SDL_SCANCODE_F11:          return key_f11;
		case SDL_SCANCODE_F12:          return key_f12;
		case SDL_SCANCODE_F13:          return key_f13;
		case SDL_SCANCODE_F14:          return key_f14;
		case SDL_SCANCODE_F15:          return key_f15;
		case SDL_SCANCODE_KP_EQUALS:    return key_numpadequals;
		case SDL_SCANCODE_KP_ENTER:     return key_numpadenter;
		case SDL_SCANCODE_RCTRL:        return key_rcontrol;
		case SDL_SCANCODE_MUTE:         return key_mute;
		case SDL_SCANCODE_MEDIA_PLAY_PAUSE: return key_playpause;
		case SDL_SCANCODE_MEDIA_STOP:   return key_mediastop;
		case SDL_SCANCODE_VOLUMEDOWN:   return key_volumedown;
		case SDL_SCANCODE_VOLUMEUP:     return key_volumeup;
		case SDL_SCANCODE_KP_COMMA:     return key_numpadcomma;
		case SDL_SCANCODE_KP_DIVIDE:    return key_divide;
		case SDL_SCANCODE_PRINTSCREEN:  return key_sysrq;
		case SDL_SCANCODE_RALT:         return key_rmenu;
		case SDL_SCANCODE_PAUSE:        return key_pause;
		case SDL_SCANCODE_HOME:         return key_home;
		case SDL_SCANCODE_UP:           return key_up;
		case SDL_SCANCODE_PAGEUP:       return key_prior;
		case SDL_SCANCODE_LEFT:         return key_left;
		case SDL_SCANCODE_RIGHT:        return key_right;
		case SDL_SCANCODE_END:          return key_end;
		case SDL_SCANCODE_DOWN:         return key_down;
		case SDL_SCANCODE_PAGEDOWN:     return key_next;
		case SDL_SCANCODE_INSERT:       return key_insert;
		case SDL_SCANCODE_DELETE:       return key_delete;
		case SDL_SCANCODE_LGUI:         return key_lwin;
		case SDL_SCANCODE_RGUI:         return key_rwin;
		case SDL_SCANCODE_APPLICATION:  return key_apps;
		default:                        return static_cast<enum_keyboard>( 0 );
	}
}

void keyboard::on_key_event	( enum_keyboard const key, bool const down )
{
	if ( !m_active )
		return;

	u32 const scan = static_cast<u32>( key );
	if ( scan == 0 || scan >= sizeof( m_live_key_buffer ) )
		return;

	m_live_key_buffer[scan] = down ? static_cast<char>( 0x80 ) : 0;
}

void keyboard::on_sdl_scancode	( s32 const scancode, bool const down )
{
	on_key_event( translate_sdl_scancode( static_cast<SDL_Scancode>( scancode ) ), down );
}

void keyboard::on_sdl_event	( SDL_Event const& ev )
{
	if ( ev.type != SDL_EVENT_KEY_DOWN && ev.type != SDL_EVENT_KEY_UP )
		return;

	on_key_event( translate_sdl_scancode( ev.key.scancode ), ev.key.down );
}

bool keyboard::is_key_down	( char const value ) const
{
	return				( !!( value & 0x80 ) );
}

bool keyboard::is_key_down	( enum_keyboard const key ) const
{
	return				( is_key_down( m_current_key_buffer[ key ] ) );
}

void keyboard::execute		( )
{
	memory::copy		( &m_previous_key_buffer, sizeof(m_previous_key_buffer), &m_current_key_buffer, sizeof( m_current_key_buffer ) );
	memory::copy		( &m_current_key_buffer,  sizeof(m_current_key_buffer),  &m_live_key_buffer,    sizeof( m_live_key_buffer ) );
}
