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

sound_object_impl::sound_object_impl( sound_stream_ptr stream )
:m_stream			( stream ),
m_source_voice		( nullptr ),
m_stream_cursor_pcm	( 0 ),
m_thread_allocator	( nullptr ),
m_playing			( false ),
m_position			( 0.0f, 0.0f, 0.0f)
{
}

sound_object_impl::~sound_object_impl( )
{
	if( m_source_voice )
		stop_impl		( );
}

void sound_object_impl::refill_buffers( )
{
#if 1
	R_ASSERT				(m_playing);
	ASSERT					( m_stream );
	u32 queued				= m_source_voice->buffers_queued();
	u32 pcm_total 			= m_stream->pcm_total();
	
	while( (queued < max_queued_buffers) && ( m_stream_cursor_pcm < pcm_total) )
	{
		audio_buffer_desc		buffer;

		pbyte const cache_buffer = g_sound_world->get_cache_buffer( snd_buff_size );
		buffer.audio_data		= cache_buffer;
		buffer.audio_bytes		= m_stream->decompress( cache_buffer, m_stream_cursor_pcm, snd_buff_size );
		buffer.context			= cache_buffer;

		buffer.flags			= ( m_stream_cursor_pcm==pcm_total ) ? audio_buffer_flag_end_of_stream : 0u;

		ASSERT					( buffer.audio_bytes>0 );

		m_source_voice->submit_buffer( buffer );
		++queued;
	}
#endif // #if 1
}

void sound_object_impl::on_end_stream( )
{
	stop_impl						( );
	LOG_INFO						("---voice destroyed");
}

void sound_object_impl::on_buffer_end_impl( void* pcontext )
{
	g_sound_world->free_cache_buffer ( (pbyte)pcontext );
	
	if(is_playing())
		refill_buffers				( );
}

void sound_object_impl::on_buffer_error( void* pcontext)
{
	XRAY_UNREFERENCED_PARAMETERS( pcontext );
	LOG_INFO("---voice on_buffer_error");
}

void sound_object_impl::play( )
{
	playback_command* cmd = XRAY_NEW_IMPL(thread_allocator(), playback_command)(thread_allocator(), playback_command::ev_play, this);
	g_sound_world->add_end_user_command( cmd );
}

void sound_object_impl::stop( )
{
	playback_command* cmd = XRAY_NEW_IMPL(thread_allocator(), playback_command)(thread_allocator(), playback_command::ev_stop, this);
	g_sound_world->add_end_user_command( cmd );
}

void sound_object_impl::set_position( float3 const& p )
{
	XRAY_UNREFERENCED_PARAMETER	( p );
}

void sound_object_impl::play_impl( )
{
	m_stream_cursor_pcm		= 0;

	if(!m_source_voice)
	{
		audio_voice_desc desc;
		desc.channels			= count_to_channels_type( m_stream->wave_format().channels );
		desc.sample_rate		= m_stream->wave_format().samples_per_sec;
		desc.bits_per_sample	= m_stream->wave_format().bits_per_sample;
		desc.use_filter			= false;

		m_source_voice			= g_sound_world->audio_backend()->create_voice( desc, this );
		R_ASSERT_U				( m_source_voice );
	}
	m_playing				= true;
	refill_buffers			( );
	m_source_voice->start	( );

//	g_sound_world->on_voice_started( this );
}

void sound_object_impl::set_position_impl( float3 const& position )
{
	m_position = position;
//	g_sound_world->on_voice_position_changed( this );
}

void sound_object_impl::stop_impl( )
{
	m_playing						= false;
	if ( m_source_voice ) {
		m_source_voice->stop			( );
		m_source_voice->flush_buffers	( );
		if ( g_sound_world && g_sound_world->audio_backend() )
			g_sound_world->audio_backend()->destroy_voice( m_source_voice );
		m_source_voice					= nullptr;
	}
//	g_sound_world->on_voice_stopped	( this );
}

} // namespace sound
} // namespace xray
