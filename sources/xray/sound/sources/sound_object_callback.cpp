////////////////////////////////////////////////////////////////////////////
//	Created		: 15.04.2010
//	Author		: Andrew Kolomiets
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "sound_object.h"
#include "sound_world.h"
#include "sound_object_commands.h"

namespace xray {
namespace sound {
static bool _logging_initialized = false;

void check_logging()
{
	if(!_logging_initialized)
	{
//		xray::core::debug::on_thread_spawn		( );
//		xray::logging::on_thread_spawn			( );

		_logging_initialized = true;
	}
}
// Called just before this voice's processing pass begins.
void sound_object_impl::on_voice_processing_pass_start( u32 bytes_required )
{
	check_logging();
	XRAY_UNREFERENCED_PARAMETERS( bytes_required );
}

// Called just after this voice's processing pass ends.
void sound_object_impl::on_voice_processing_pass_end( )
{
	check_logging();
}

// Called when this voice has just finished playing a buffer stream
void sound_object_impl::on_stream_end( )
{
	check_logging();
	g_sound_world->add_xaudio_callback_command( MT_NEW(sound_object_callback_command)(::xray::memory::g_mt_allocator, sound_object_callback_command::ev_stream_end, this, nullptr));
	LOG_INFO("OnStreamEnd");
}

// Called when this voice is about to start processing a new buffer.
void sound_object_impl::on_buffer_start( void* buffer_context )
{
	check_logging();
	XRAY_UNREFERENCED_PARAMETERS( buffer_context );
}

// Called when this voice has just finished processing a buffer.
// The buffer can now be reused or destroyed.
void sound_object_impl::on_buffer_end( void* buffer_context )
{
	check_logging();
	g_sound_world->add_xaudio_callback_command( MT_NEW(sound_object_callback_command)(::xray::memory::g_mt_allocator, sound_object_callback_command::ev_buffer_end, this, buffer_context));
}

// Called when this voice has just reached the end position of a loop.
void sound_object_impl::on_loop_end( void* buffer_context )
{
	check_logging();
	LOG_INFO("OnLoopEnd: %p", buffer_context);
}

void sound_object_impl::on_voice_error( void* buffer_context, s32 error )
{
	check_logging();
	g_sound_world->add_xaudio_callback_command( MT_NEW(sound_object_callback_command)(::xray::memory::g_mt_allocator, sound_object_callback_command::ev_buffer_error, this, buffer_context));
	LOG_INFO("OnVoiceError: %d, %p", error, buffer_context);
}

} // namespace sound
} // namespace xray
