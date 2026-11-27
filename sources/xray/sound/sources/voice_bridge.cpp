////////////////////////////////////////////////////////////////////////////
//	Created		: 28.02.2011
//	Author		: Dmitry Kulikov
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "voice_bridge.h"
#include "sound_buffer.h"
#include "sound_voice.h"

namespace xray {
namespace sound {

enum
{
	default_bits_per_sample = 16,
	default_sample_rate		= 44100,
};

voice_bridge::voice_bridge			( creation_parametrs& params ) :
	m_next				( 0 ),
	m_backend			( params.audio_backend ),
	m_handler			( 0 ),
	m_source_voice		( 0 ),
	m_master_channels	( params.master_channels_type )
{
	R_ASSERT_U						( params.type < channels_type_count );
	R_ASSERT_U						( m_backend );

	audio_voice_desc desc;
	desc.channels					= params.type;
	desc.sample_rate				= default_sample_rate;
	desc.bits_per_sample			= default_bits_per_sample;
	desc.max_frequency_ratio		= params.max_frequency_ratio;
	desc.use_filter					= true;

	m_source_voice					= m_backend->create_voice( desc, this );
	R_ASSERT_U						( m_source_voice );
}

voice_bridge::~voice_bridge( )
{
	if ( m_backend && m_source_voice ) {
		m_backend->destroy_voice( m_source_voice );
		m_source_voice = 0;
	}
}

void voice_bridge::set_handler ( voice_callback_handler* handler )
{
	m_handler = handler;
}

channels_type voice_bridge::get_channels_type ( ) const
{
	return m_source_voice->channels( );
}

u32 voice_bridge::get_sample_rate ( ) const
{
	return m_source_voice->sample_rate( );
}

void voice_bridge::set_sample_rate	( u32 new_sample_rate )
{
	R_ASSERT_U			( buffers_queued() == 0 );
	if ( get_sample_rate() != new_sample_rate )
		m_source_voice->set_sample_rate( new_sample_rate );
}

void voice_bridge::set_output_voice		( i_audio_submix* output_voice )
{
	m_source_voice->set_output_submix( output_voice );
}

void voice_bridge::start ( )
{
	m_source_voice->start( );
}

void voice_bridge::stop ( )
{
	m_source_voice->stop( );
}

void voice_bridge::submit_source_buffer	( sound_buffer* buffer, u32 playing_offset, u32 playing_length )
{
	audio_buffer_desc* audio_buffer	= buffer->get_audio_buffer( );

	if ( playing_offset )
	{
		R_ASSERT					( playing_offset <= buffer->get_length_in_pcm( ) );
		audio_buffer->play_begin	= playing_offset;
		audio_buffer->play_length	= buffer->get_length_in_pcm() - playing_offset;
	}
	else
	{
		audio_buffer->play_begin	= 0;
		audio_buffer->play_length	= playing_length;
	}

	m_source_voice->submit_buffer	( *audio_buffer );
}

void voice_bridge::flush_source_buffers ( )
{
	m_source_voice->flush_buffers( );
}

u32 voice_bridge::buffers_queued ( ) const
{
	return m_source_voice->buffers_queued( );
}

u64	voice_bridge::samples_played	( ) const
{
	return m_source_voice->samples_played( );
}

sound_buffer* voice_bridge::current_sound_buffer ( ) const
{
	return static_cast_checked<sound_buffer*>( m_source_voice->current_buffer_context( ) );
}

void voice_bridge::set_output_matrix ( float const* level_matrix )
{
	m_source_voice->set_output_matrix(
		channels_type_to_count( get_channels_type() ),
		channels_type_to_count( m_master_channels ),
		level_matrix
	);
}

void voice_bridge::set_channel_volumes	( channels_type type, float const* level_matrix )
{
	m_source_voice->set_channel_volumes( channels_type_to_count( type ), level_matrix );
}

void voice_bridge::set_low_pass_filter_params		( float coeff )
{
	m_source_voice->set_low_pass_filter( coeff );
}

void voice_bridge::set_frequency_ratio				( float ratio )
{
	R_ASSERT_U				( !( ratio < 0.0f ) );
	R_ASSERT_U				( m_source_voice );
	m_source_voice->set_frequency_ratio( ratio );
}

u8 voice_bridge::get_bytes_per_second( ) const
{
	return default_bits_per_sample >> 3;
}

void voice_bridge::on_voice_processing_pass_start	( u32 bytes_required )
{
	R_ASSERT_U					( m_handler );
	m_handler->on_voice_processing_pass_start( bytes_required );
}

void voice_bridge::on_voice_processing_pass_end	( )
{
	R_ASSERT_U					( m_handler );
	m_handler->on_voice_processing_pass_end( );
}

void voice_bridge::on_stream_end	( )
{
	R_ASSERT_U					( m_handler );
	m_handler->on_stream_end	( );
}

void voice_bridge::on_buffer_start( void* buffer_context )
{
	R_ASSERT_U					( m_handler );
	m_handler->on_buffer_start	( buffer_context );
}

void voice_bridge::on_buffer_end	( void* buffer_context )
{
	R_ASSERT_U					( m_handler );
	m_handler->on_buffer_end	( buffer_context );
}

void voice_bridge::on_loop_end	( void* buffer_context )
{
	R_ASSERT_U					( m_handler );
	m_handler->on_loop_end		( buffer_context );
}

void voice_bridge::on_voice_error	( void* buffer_context, s32 error )
{
	R_ASSERT_U					( m_handler );
	m_handler->on_voice_error	( buffer_context, error );
}

} // namespace sound
} // namespace xray
