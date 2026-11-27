////////////////////////////////////////////////////////////////////////////
//	Created		: 15.04.2010
//	Author		: Andrew Kolomiets
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "sound_item.h"
#include "sound_item_callback.h"
#include "sound_world.h"

namespace xray {
namespace sound {

// Called just before this voice's processing pass begins.
void sound_item::on_voice_processing_pass_start( u32 bytes_required )
{
	XRAY_UNREFERENCED_PARAMETERS( bytes_required );
}

// Called just after this voice's processing pass ends.
void sound_item::on_voice_processing_pass_end( )
{
}

// Called when this voice has just finished playing a buffer stream
void sound_item::on_stream_end( )
{
	m_sound_world.add_xaudio_order( MT_NEW(sound_item_callback_command)(sound_item_callback_command::ev_stream_end, this, nullptr));
	LOG_INFO("OnStreamEnd");
}

// Called when this voice is about to start processing a new buffer.
void sound_item::on_buffer_start( void* buffer_context )
{
	XRAY_UNREFERENCED_PARAMETERS( buffer_context );
}

// Called when this voice has just finished processing a buffer.
// The buffer can now be reused or destroyed.
void sound_item::on_buffer_end( void* buffer_context )
{
	m_sound_world.add_xaudio_order( MT_NEW(sound_item_callback_command)(sound_item_callback_command::ev_buffer_end, this, buffer_context));
}

// Called when this voice has just reached the end position of a loop.
void sound_item::on_loop_end( void* buffer_context )
{
	LOG_INFO("OnLoopEnd: %p", buffer_context);
}

void sound_item::on_voice_error( void* buffer_context, s32 error )
{
	m_sound_world.add_xaudio_order( MT_NEW(sound_item_callback_command)(sound_item_callback_command::ev_buffer_error, this, buffer_context));
	LOG_INFO("OnVoiceError: %d, %p", error, buffer_context);
}

void sound_item_callback_command::execute( )
{
	switch (m_event_type)
	{
	case ev_stream_end:
		m_owner->on_end_stream( );
		break;

	case ev_buffer_end:
		m_owner->on_buffer_end_impl( m_context );
		break;

	case ev_buffer_error:
		m_owner->on_buffer_error( m_context );
		break;

	default:
		LOG_ERROR("unknown callback event: %d", (int)m_event_type);
		NODEFAULT();
	}
}

} // namespace sound
} // namespace xray
