////////////////////////////////////////////////////////////////////////////
//	Created 	: 21.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef RECEIVER_MOUSE_H_INCLUDED
#define RECEIVER_MOUSE_H_INCLUDED

#include <xray/input/world.h>
#include <xray/input/mouse.h>
#include <xray/platform_surface.h>
#include <SDL3/SDL_events.h>

namespace xray {
namespace input {

struct handler;

namespace receiver {

class mouse :
	public input::mouse,
	private xray::noncopyable
{
typedef vector< handler* >		handlers_type;
public:
							mouse			( platform::native_surface_handle window_handle, xray::input::world& input_world );
	virtual					~mouse			( );
	virtual	state const&	get_state		( ) const;
			void			on_activate		( );
			void			on_deactivate	( );
			void			execute			( );
			void			process			( handlers_type& handlers );
			void			on_motion		( float dx, float dy );
			void			on_wheel		( float dz );
			void			on_button		( mouse_button btn, bool down );
			void			on_sdl_event	( SDL_Event const& ev );
private:
	state					m_current_state;
	state					m_previous_state;
	platform::native_surface_handle	m_window_handle;
	xray::input::world&		m_world;
	long					m_pending_x;
	long					m_pending_y;
	long					m_pending_z;
	u8						m_live_buttons;
	bool					m_active;
}; // class world

} // namespace receiver
} // namespace input
} // namespace xray

#endif // #ifndef RECEIVER_MOUSE_H_INCLUDED
