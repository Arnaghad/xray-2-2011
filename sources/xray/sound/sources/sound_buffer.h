////////////////////////////////////////////////////////////////////////////
//	Created		: 21.02.2011
//	Author		: Dmitriy Kulikov
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef SOUND_BUFFER_H_INCLUDED
#define SOUND_BUFFER_H_INCLUDED

#include <xray/sound/encoded_sound_interface.h>
#include <xray/intrusive_double_linked_list.h>

struct XAUDIO2_BUFFER;

namespace xray {
namespace sound {

class sound_buffer;
class sound_buffer_factory;
class sound_voice;

enum
{
	sound_buffer_size = 44100,//88200, //44100,//15000,//10000, //3532,
};

class lightweight_sound_buffer
{
public:
	lightweight_sound_buffer( encoded_sound_ptr const& encoded_sound, u32 offset );
private:
	encoded_sound_ptr		m_encoded_sound;
	u32						m_cached_offset;

	friend class sound_buffer_compare_predicate;
	friend class sound_buffer_factory;
};


class sound_buffer : 
	
	private xray::noncopyable
{
private:
							sound_buffer			( );
public:
			u32				get_length_in_pcm		( ) const;
	inline	XAUDIO2_BUFFER*	get_xaudio_buffer		( )
			{
				return &m_xaudio_buffer;

			}
			sound_buffer*	m_next;
			sound_buffer*	m_prev;
							~sound_buffer			( );
			
			u32				next_sound_buffer_offset( ) { return m_cached_offset_after_decompress; }
private:
			void			fill_buffer				( encoded_sound_ptr const& encoded_sound, u32 pcm_offset, u32& next_pcm_offset );
			void			fill_mute_buffer		( encoded_sound_ptr const& encoded_sound );
			u32				reference_count			( ) const { return m_reference_count; }

private:
			void			increment				( ) { ++m_reference_count; }
			void			decrement				( ) { --m_reference_count; }
private:
	XAUDIO2_BUFFER			m_xaudio_buffer;
	u32						m_users;
	u32						m_reference_count;
	u32						m_length_in_pcm;
	encoded_sound_ptr		m_encoded_sound;
	u32						m_cached_offset;
	u32						m_cached_offset_after_decompress;
	s16						m_last_value_in_buffer;
	friend class sound_buffer_compare_predicate;
	friend class sound_buffer_factory;
	friend class sound_voice;

	// this member must be last
#pragma warning(disable:4200)
	u8						m_buff[];
#pragma warning(default:4200)
}; // class sound_buffer



class sound_buffer_compare_predicate
{
public:
	bool operator( )( sound_buffer const &lhs, lightweight_sound_buffer const& rhs ) const
	{
	   if ( lhs.m_encoded_sound < rhs.m_encoded_sound )
		   return false;
   	   if ( lhs.m_encoded_sound > rhs.m_encoded_sound )
		   return true;
	   return lhs.m_cached_offset < rhs.m_cached_offset;
	}

	bool operator( )( lightweight_sound_buffer const& lhs, sound_buffer const& rhs ) const
	{
	   if ( lhs.m_encoded_sound < rhs.m_encoded_sound )
		   return false;
   	   if ( lhs.m_encoded_sound > rhs.m_encoded_sound )
		   return true;
	   return lhs.m_cached_offset < rhs.m_cached_offset;
	}

	bool operator( )( sound_buffer const& lhs, sound_buffer const& rhs ) const
	{
	   if ( lhs.m_encoded_sound < rhs.m_encoded_sound )
		   return true;
	   if ( lhs.m_encoded_sound > rhs.m_encoded_sound )
		   return false;
	   return lhs.m_cached_offset < rhs.m_cached_offset;
	}
};

typedef intrusive_list <
	sound_buffer,
	sound_buffer*,
	&sound_buffer::m_next,
	threading::single_threading_policy >	sound_buffer_list_type;

typedef xray::intrusive_double_linked_list< sound_buffer, sound_buffer*, &sound_buffer::m_next, &sound_buffer::m_prev > sound_buffers_set_type;

} // namespace sound
} // namespace xray

#endif // #ifndef SOUND_BUFFER_H_INCLUDED
