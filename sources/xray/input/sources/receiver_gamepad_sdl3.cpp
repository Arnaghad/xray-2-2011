////////////////////////////////////////////////////////////////////////////
//	Created 	: 21.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "receiver_gamepad.h"
#include <xray/input/handler.h>
#include <SDL3/SDL_init.h>

using xray::input::receiver::gamepad;
using xray::input::handler;
using xray::input::world;
using xray::math::float2;
using xray::input::gamepad_vibrators;

namespace {

constexpr s16 kStickDeadzone = 7849;

float convert_stick_value( s16 const value, s16 const dead_zone )
{
	if ( value > +dead_zone )
		return ( value - dead_zone ) / ( 32767.f - dead_zone );

	if ( value < -dead_zone )
		return ( value + dead_zone + 1.f ) / ( 32767.f - dead_zone );

	return 0.f;
}

u16 read_sdl_gamepad_buttons( SDL_Gamepad* gp )
{
	u16 mask = 0;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_DPAD_UP ) )        mask |= xray::input::gamepad_dpad_up;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_DPAD_DOWN ) )      mask |= xray::input::gamepad_dpad_down;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_DPAD_LEFT ) )      mask |= xray::input::gamepad_dpad_left;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_DPAD_RIGHT ) )     mask |= xray::input::gamepad_dpad_right;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_START ) )          mask |= xray::input::gamepad_start;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_BACK ) )           mask |= xray::input::gamepad_back;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_LEFT_STICK ) )     mask |= xray::input::gamepad_left_thumb;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_RIGHT_STICK ) )    mask |= xray::input::gamepad_right_thumb;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_LEFT_SHOULDER ) )  mask |= xray::input::gamepad_left_shoulder;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER ) ) mask |= xray::input::gamepad_right_shoulder;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_GUIDE ) )          mask |= xray::input::gamepad_bigbutton;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_SOUTH ) )          mask |= xray::input::gamepad_a;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_EAST ) )           mask |= xray::input::gamepad_b;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_WEST ) )           mask |= xray::input::gamepad_x;
	if ( SDL_GetGamepadButton( gp, SDL_GAMEPAD_BUTTON_NORTH ) )          mask |= xray::input::gamepad_y;
	return mask;
}

} // namespace

gamepad::gamepad			( u32 const user_index, world& input_world ) :
	m_world			( input_world ),
	m_gamepad		( nullptr ),
	m_instance_id	( 0 ),
	m_left_rumble	( 0 ),
	m_right_rumble	( 0 ),
	m_user_index	( user_index ),
	m_connected		( false ),
	m_inserted		( false ),
	m_removed		( false )
{
	memory::zero	( &m_current_state, sizeof( m_current_state ) );
	memory::zero	( &m_previous_state, sizeof( m_previous_state ) );
}

gamepad::~gamepad			( )
{
	if ( m_gamepad ) {
		SDL_RumbleGamepad( m_gamepad, 0, 0, 0 );
		SDL_CloseGamepad( m_gamepad );
		m_gamepad		= nullptr;
		m_instance_id	= 0;
	}
}

xray::input::gamepad::state const& gamepad::get_state	( ) const
{
	return			( m_current_state );
}

void gamepad::on_activate	( )
{
}

void gamepad::on_deactivate	( )
{
	if ( m_gamepad ) {
		SDL_RumbleGamepad( m_gamepad, 0, 0, 0 );
	}
	m_left_rumble	= 0;
	m_right_rumble	= 0;
	memory::zero	( &m_current_state, sizeof( m_current_state ) );
	memory::zero	( &m_previous_state, sizeof( m_previous_state ) );
}

void gamepad::on_gamepad_added	( SDL_JoystickID const instance_id )
{
	if ( m_gamepad || instance_id == 0 )
		return;

	SDL_Gamepad* const opened = SDL_OpenGamepad( instance_id );
	if ( !opened )
		return;

	m_gamepad		= opened;
	m_instance_id	= instance_id;
	m_left_rumble	= 0;
	m_right_rumble	= 0;
	SDL_RumbleGamepad( m_gamepad, 0, 0, 0 );
}

void gamepad::on_gamepad_removed	( SDL_JoystickID const instance_id )
{
	if ( m_gamepad && m_instance_id == instance_id ) {
		SDL_RumbleGamepad( m_gamepad, 0, 0, 0 );
		SDL_CloseGamepad( m_gamepad );
		m_gamepad		= nullptr;
		m_instance_id	= 0;
	}
	if ( m_connected ) {
		apply_gamepad_state( false, 0, 0.f, 0.f, float2( 0.f, 0.f ), float2( 0.f, 0.f ) );
	}
}

void gamepad::on_sdl_event	( SDL_Event const& ev )
{
	if ( ev.type == SDL_EVENT_GAMEPAD_ADDED )
		on_gamepad_added( ev.gdevice.which );
	else if ( ev.type == SDL_EVENT_GAMEPAD_REMOVED )
		on_gamepad_removed( ev.gdevice.which );
}

void gamepad::apply_gamepad_state	(
		bool const connected,
		u16 const buttons,
		float const left_trigger,
		float const right_trigger,
		float2 const left_stick,
		float2 const right_stick
	)
{
	bool const		was_connected = m_connected;
	m_connected		= connected;
	m_removed		= was_connected && !m_connected;
	m_inserted		= !was_connected && m_connected;

	if ( m_removed ) {
		memory::copy( &m_previous_state, sizeof(m_previous_state), &m_current_state, sizeof( m_current_state ) );
		memory::zero( &m_current_state, sizeof( m_current_state ) );
		m_left_rumble	= 0;
		m_right_rumble	= 0;
		return;
	}

	if ( !m_connected )
		return;

	if ( m_inserted ) {
		memory::zero	( &m_current_state, sizeof( m_current_state ) );
		memory::zero	( &m_previous_state, sizeof( m_previous_state ) );
		m_left_rumble	= 0;
		m_right_rumble	= 0;
	}

	memory::copy	( &m_previous_state, sizeof(m_previous_state), &m_current_state, sizeof( m_current_state ) );

	m_current_state.buttons				= static_cast<gamepad_button>( buttons );
	m_current_state.left_trigger		= left_trigger;
	m_current_state.right_trigger		= right_trigger;
	m_current_state.left_thumb_stick	= left_stick;
	m_current_state.right_thumb_stick	= right_stick;
}

void gamepad::execute		( )
{
	if ( !m_gamepad ) {
		if ( !m_connected && SDL_WasInit( SDL_INIT_GAMEPAD ) != 0 ) {
			int count = 0;
			SDL_JoystickID* ids = SDL_GetGamepads( &count );
			if ( ids ) {
				if ( static_cast<int>( m_user_index ) < count ) {
					on_gamepad_added( ids[m_user_index] );
				}
				SDL_free( ids );
			}
		}
		if ( !m_gamepad ) {
			m_removed	= false;
			m_inserted	= false;
			return;
		}
	}

	u16 const buttons	= read_sdl_gamepad_buttons( m_gamepad );
	float const lt		= math::clamp_r( static_cast<float>( SDL_GetGamepadAxis( m_gamepad, SDL_GAMEPAD_AXIS_LEFT_TRIGGER ) ) / 32767.f, 0.f, 1.f );
	float const rt		= math::clamp_r( static_cast<float>( SDL_GetGamepadAxis( m_gamepad, SDL_GAMEPAD_AXIS_RIGHT_TRIGGER ) ) / 32767.f, 0.f, 1.f );
	float2 const ls(
		convert_stick_value( SDL_GetGamepadAxis( m_gamepad, SDL_GAMEPAD_AXIS_LEFTX ), kStickDeadzone ),
		convert_stick_value( SDL_GetGamepadAxis( m_gamepad, SDL_GAMEPAD_AXIS_LEFTY ), kStickDeadzone )
	);
	float2 const rs(
		convert_stick_value( SDL_GetGamepadAxis( m_gamepad, SDL_GAMEPAD_AXIS_RIGHTX ), kStickDeadzone ),
		convert_stick_value( SDL_GetGamepadAxis( m_gamepad, SDL_GAMEPAD_AXIS_RIGHTY ), kStickDeadzone )
	);

	apply_gamepad_state( true, buttons, lt, rt, ls, rs );
}

void gamepad::process		( handlers_type& handlers )
{
	if ( !m_connected && !m_removed )
		return;

	u16 const changed_buttons	= u16( m_current_state.buttons ^ m_previous_state.buttons );

	u16 const buttons_down		= u16( m_current_state.buttons & changed_buttons );
	for ( u16 i = buttons_down; i; i &= i - 1 ) {
		for ( handlers_type::iterator it = handlers.begin(), it_e = handlers.end(); it != it_e; ++it ) {
			if ( (*it)->on_gamepad_action( &m_world, gamepad_button( i ^ ( i & ( i - 1 ) ) ), gp_key_down ) )
				break;
		}
	}

	u16 const buttons_up		= u16( ~m_current_state.buttons & changed_buttons );
	for ( u16 i = buttons_up; i; i &= i - 1 ) {
		for ( handlers_type::iterator it = handlers.begin(), it_e = handlers.end(); it != it_e; ++it ) {
			if ( (*it)->on_gamepad_action( &m_world, gamepad_button( i ^ ( i & ( i - 1 ) ) ), gp_key_up ) )
				break;
		}
	}

	u16 const hold_buttons		= u16( m_current_state.buttons & m_previous_state.buttons );
	for ( u16 i = hold_buttons; i; i &= i - 1 ) {
		for ( handlers_type::iterator it = handlers.begin(), it_e = handlers.end(); it != it_e; ++it ) {
			if ( (*it)->on_gamepad_action( &m_world, gamepad_button( i ^ ( i & ( i - 1 ) ) ), gp_key_hold ) )
				break;
		}
	}
}

void gamepad::set_vibration		( gamepad_vibrators const vibrator, float value_raw )
{
	if ( !m_connected )
		return;

	float const clamped			= math::clamp_r( value_raw, 0.f, 1.f );
	u16 const value				= (u16)math::clamp_r( math::floor( clamped*65535.f + 0.5f ), 0, 65535 );

	if ( vibrator == gamepad_vibrator_left )
		m_left_rumble			= value;
	else {
		ASSERT					( vibrator == gamepad_vibrator_right );
		m_right_rumble			= value;
	}

	if ( m_gamepad ) {
		SDL_RumbleGamepad( m_gamepad, m_left_rumble, m_right_rumble, 0 );
	}
}

float gamepad::get_vibration	( gamepad_vibrators const vibrator ) const
{
	if ( vibrator == gamepad_vibrator_left )
		return					( float( m_left_rumble )/65535.f );

	ASSERT						( vibrator == gamepad_vibrator_right );
	return						( float( m_right_rumble )/65535.f );
}
