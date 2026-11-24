////////////////////////////////////////////////////////////////////////////
//	Created 	: 21.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef RECEIVER_CONTROLLER_H_INCLUDED
#define RECEIVER_CONTROLLER_H_INCLUDED

#include <xray/input/world.h>
#include <xray/input/gamepad.h>
#include <SDL3/SDL_gamepad.h>
#include <SDL3/SDL_events.h>

namespace xray {
namespace input {

struct handler;

namespace receiver {

class gamepad :
	public input::gamepad,
	private xray::noncopyable
{
	typedef vector< handler* >				handlers_type;

public:
							gamepad			( u32 user_index, xray::input::world& input_world );
	virtual					~gamepad		( );
	virtual	state const&	get_state		( ) const;
	virtual	float			get_vibration	( gamepad_vibrators vibrator ) const;
	virtual	void			set_vibration	( gamepad_vibrators vibrator, float value );
			void			on_activate		( );
			void			on_deactivate	( );
			void			execute			( );
			void			process			( handlers_type& handlers );
			void			on_gamepad_added	( SDL_JoystickID instance_id );
			void			on_gamepad_removed	( SDL_JoystickID instance_id );
			void			on_sdl_event		( SDL_Event const& ev );
			void			apply_gamepad_state	(
								bool connected,
								u16 buttons,
								float left_trigger,
								float right_trigger,
								math::float2 left_stick,
								math::float2 right_stick
							);

private:
	xray::input::world&		m_world;
	SDL_Gamepad*			m_gamepad;
	SDL_JoystickID			m_instance_id;
	u16						m_left_rumble;
	u16						m_right_rumble;
	state					m_current_state;
	state					m_previous_state;
	u32						m_user_index;
	bool					m_connected;
	bool					m_inserted;
	bool					m_removed;
}; // class world

} // namespace receiver
} // namespace input
} // namespace xray

#endif // #ifndef RECEIVER_CONTROLLER_H_INCLUDED
