////////////////////////////////////////////////////////////////////////////
//	Created 	: 21.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef RECEIVER_KEYBOARD_H_INCLUDED
#define RECEIVER_KEYBOARD_H_INCLUDED

#include <xray/input/world.h>
#include <xray/input/keyboard.h>
#include <SDL3/SDL_scancode.h>
#include <SDL3/SDL_events.h>

namespace xray {
namespace input {

struct handler;

namespace receiver {

class keyboard :
	public input::keyboard,
	private xray::noncopyable
{
typedef vector< handler* >		handlers_type;
public:
							keyboard		( HWND window_handle, xray::input::world& input_world );
	virtual					~keyboard		( );
	virtual	bool			is_key_down		( enum_keyboard key ) const;
			void			on_activate		( );
			void			on_deactivate	( );
			void			execute			( );
			void			process			( handlers_type& handlers );
			void			on_key_event	( enum_keyboard key, bool down );
			void			on_sdl_scancode	( s32 scancode, bool down );
			void			on_sdl_event	( SDL_Event const& ev );
	static	enum_keyboard	translate_sdl_scancode( SDL_Scancode scancode );

private:
			bool	is_key_down		( char value ) const;

private:
	string256				m_live_key_buffer;
	string256				m_current_key_buffer;
	string256				m_previous_key_buffer;
	HWND					m_window_handle;
	xray::input::world&		m_world;
	bool					m_active;
}; // class world

} // namespace receiver
} // namespace input
} // namespace xray

#endif // #ifndef RECEIVER_KEYBOARD_H_INCLUDED
