////////////////////////////////////////////////////////////////////////////
//	Created		: 27.04.2010
//	Author		: Andrew Kolomiets
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/sound/sound_object_commands.h>
#include "sound_object_commands.h"
#include "sound_object.h"
#include "sound_world.h"

namespace xray {
namespace sound {

playback_command::playback_command(base_allocator_type* allocator,
    playback_event const& event, sound_object_impl* owner)
    : super(allocator, owner), m_event(event)
{
}

void playback_command::execute()
{
    switch (m_event) {
    case ev_play: m_owner->play_impl(); break;
    case ev_stop: m_owner->stop_impl(); break;
    default: NODEFAULT();
    }
}

sound_position_command::sound_position_command(base_allocator_type* allocator,
    float3 const& position, sound_object_impl* owner)
    : super(allocator, owner), m_position(position)
{
}

void sound_position_command::execute()
{
    m_owner->set_position_impl(m_position);
}

sound_order::sound_order		( base_allocator_type* a ) :
	m_next_for_orders			( 0 ),
	m_next_for_postponed_orders	( 0 ),
	m_creator					( a )
{
}

sound_order::~sound_order		( )
{
}

void sound_order::execute	( )
{
}

sound_response::sound_response	( ) :
	m_next						( 0 )
{
}

sound_response::~sound_response	( )
{
}

void sound_response::execute	( )
{
}

//sound_object_order::sound_object_order (
//		sound_object_impl* const owner
//	) :
//	m_owner			( owner ),
//	m_next			( 0 )
//{
//}
//
//void sound_object_order::execute	( )
//{
//}
//
sound_object_callback_command::sound_object_callback_command( base_allocator_type* a, cb_event const& ev, sound_object_impl* o, void* context ) :
	super			( a, o ),
	m_event_type	( ev ),
	m_context		( context )
{
}

void sound_object_callback_command::execute( )
{
	switch ( m_event_type )
	{
	case ev_stream_end:
		m_owner->on_end_stream		( );
		break;

	case ev_buffer_end:
		m_owner->on_buffer_end		( m_context );
		break;

	case ev_buffer_error:
		m_owner->on_buffer_error	( m_context );
		break;

	default:
		LOG_ERROR					( "unknown callback event: %d", (int)m_event_type );
		NODEFAULT					( );
	}
}

listener_properties_order::listener_properties_order	(
		sound_world& world,
		sound_scene& scene,
		float4x4 const& inv_view_matrix
	) :
	m_world				( world ),
	m_scene				( scene ), 
	m_inv_view_matrix	( inv_view_matrix )
{
}

void listener_properties_order::execute	( )
{
	return m_world.set_listener_properties_impl	( m_scene, m_inv_view_matrix );
}

//playback_order::playback_order	(
//		playback_event const& event,
//		sound_object_impl* const owner
//	) :
//	super		( owner ),
//	m_event		( event )
//{
//}
//
//void playback_order::execute		( )
//{
//	switch ( m_event )
//	{
//	case ev_play:
//		m_owner->play_impl			( );
//		break;
//
//	case ev_stop:
//		m_owner->stop_impl			( );
//		break;
//
//	default:
//		NODEFAULT					( );
//	};
//}
//
//sound_position_order::sound_position_order	(
//		float3 const& position,
//		sound_object_impl* const owner
//	) :
//	super		( owner ),
//	m_position	( position )
//{
//}
//
//void sound_position_order::execute	( )
//{
//	m_owner->set_position_impl			( m_position );
//}

} // namespace sound
} // namespace xray
