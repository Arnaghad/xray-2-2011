////////////////////////////////////////////////////////////////////////////
//	Created		: 21.02.2011
//	Author		: Dmitriy Kulikov
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "sound_buffer.h"

namespace xray {
namespace sound {



lightweight_sound_buffer::lightweight_sound_buffer( encoded_sound_ptr const& encoded_sound, u32 offset ) :
	m_encoded_sound				( encoded_sound ),
	m_cached_offset				( offset )
{
	// empty
}



sound_buffer::sound_buffer ( ) : 
	m_users								( 0 ),
	m_cached_offset						( 0 ),
	m_cached_offset_after_decompress	( 0 ),
	m_reference_count					( 0 ),
	m_length_in_pcm						( 0 ),
	m_last_value_in_buffer				( 0 )
{
}

sound_buffer::~sound_buffer	( )
{
}

void sound_buffer::fill_buffer( encoded_sound_ptr const& encoded_sound, u32 pcm_offset, u32& next_pcm_offset )
{
	m_encoded_sound						= encoded_sound;
	m_cached_offset						= pcm_offset;
//	u64 total_pcm						= m_encoded_sound->get_length_in_pcm( );
	u32 audio_bytes						= m_encoded_sound->decompress(m_buff, pcm_offset, next_pcm_offset, sound_buffer_size );
	m_last_value_in_buffer				= *(((s16*)m_buff) + ( next_pcm_offset - pcm_offset ) - 1);
	LOG_DEBUG							( "first val in buffer: %d", *((s16*)m_buff));
	LOG_DEBUG							( "m_last_value_in_buffer: %d", m_last_value_in_buffer );
	m_length_in_pcm						= ( audio_bytes / m_encoded_sound->get_bytes_per_sample( ) );
	m_cached_offset_after_decompress	= next_pcm_offset;
	m_audio_buffer.flags				= 0;
	m_audio_buffer.audio_bytes			= audio_bytes;
	m_audio_buffer.audio_data			= m_buff;
	m_audio_buffer.play_begin			= 0;
	m_audio_buffer.play_length			= m_length_in_pcm;
	m_audio_buffer.loop_begin			= 0;
	m_audio_buffer.loop_length			= 0;
	m_audio_buffer.loop_count			= 0;
	m_audio_buffer.context				= this;
}

void sound_buffer::fill_mute_buffer		( encoded_sound_ptr const& encoded_sound )
{
	memory::zero( (pvoid)m_buff, sound_buffer_size * sizeof(u8) );
	m_audio_buffer						= {};
	m_audio_buffer.audio_bytes			= sound_buffer_size;
	m_audio_buffer.audio_data			= m_buff;
	m_audio_buffer.context				= this;
	m_length_in_pcm						= sound_buffer_size / encoded_sound ->get_bytes_per_sample( );
}

u32 sound_buffer::get_length_in_pcm		( ) const
{
	return m_length_in_pcm;
}

} // namespace sound
} // namespace xray
