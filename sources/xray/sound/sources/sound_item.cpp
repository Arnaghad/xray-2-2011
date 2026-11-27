////////////////////////////////////////////////////////////////////////////
//	Created		: 15.04.2010
//	Author		: Andrew Kolomiets
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "sound_item.h"
#include "sound_world.h"
#include "sound_stream.h"


namespace xray {
namespace sound {

sound_item::sound_item( sound_world& w )
:m_sound_world	( w ),
m_stream		( nullptr ),
m_source_voice	( nullptr ),
m_stream_cursor_pcm	( 0 )
{
}

sound_item::~sound_item( )
{
	if( m_source_voice && m_sound_world.audio_backend() )
		m_sound_world.audio_backend()->destroy_voice( m_source_voice );

	m_source_voice					= nullptr;

}

void sound_item::start( sound_stream* stream )
{
	m_stream	= stream;
	audio_voice_desc desc;
	desc.channels			= count_to_channels_type( m_stream->wave_format().channels );
	desc.sample_rate		= m_stream->wave_format().samples_per_sec;
	desc.bits_per_sample	= m_stream->wave_format().bits_per_sample;
	desc.use_filter			= false;

	m_source_voice = m_sound_world.audio_backend()->create_voice( desc, this );
	ASSERT					( m_source_voice );

	refill_buffers			( );
	m_source_voice->start	( );
}

void sound_item::refill_buffers( )
{
	ASSERT					( m_stream );
	u32 queued				= m_source_voice->buffers_queued();
	u32 pcm_total 			= m_stream->pcm_total();
	
	while( (queued < max_queued_buffers) && ( m_stream_cursor_pcm < pcm_total) )
	{
		audio_buffer_desc		buffer;

		pbyte dest_buffer		= m_sound_world.get_cache_buffer( snd_buff_size );
		buffer.audio_bytes		= m_stream->decompress( dest_buffer, m_stream_cursor_pcm, snd_buff_size );
		buffer.audio_data		= dest_buffer;
		buffer.context			= dest_buffer;

		buffer.flags			= ( m_stream_cursor_pcm==pcm_total ) ? audio_buffer_flag_end_of_stream : 0u;

		ASSERT					( buffer.audio_bytes>0 );

		m_source_voice->submit_buffer( buffer );
		++queued;
	}
}

void sound_item::on_end_stream( )
{
	if ( m_source_voice ) {
		m_source_voice->stop			( );
		if ( m_sound_world.audio_backend() )
			m_sound_world.audio_backend()->destroy_voice( m_source_voice );
		m_source_voice					= nullptr;
	}
//	m_sound_world.on_voice_stopped	( this );
	LOG_INFO("---voice destroyed");
}

void sound_item::on_buffer_end_impl( void* pcontext )
{
	m_sound_world.free_cache_buffer ( (pbyte)pcontext );
	LOG_INFO("---voice on_buffer_end");
	refill_buffers					( );
}

void sound_item::on_buffer_error( void* pcontext)
{
	XRAY_UNREFERENCED_PARAMETERS( pcontext );
	LOG_INFO("---voice on_buffer_error");
}

} // namespace sound
} // namespace xray
