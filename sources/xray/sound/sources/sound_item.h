////////////////////////////////////////////////////////////////////////////
//	Created		: 15.04.2010
//	Author		: Andrew Kolomiets
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef SOUND_ITEM_H_INCLUDED
#define SOUND_ITEM_H_INCLUDED

#include "audio_backend.h"

namespace xray {
namespace sound {

class sound_world;
class sound_stream;

// 0.5 sec buffer (44.1 kHz*2byte_per_sample / 2 )
#define snd_buff_size		(44100 * 2 / 2)
#define max_queued_buffers	( 2 )


class sound_item:	public i_audio_voice_callback,
					public xray::noncopyable
{
public:
								sound_item					( sound_world& w );
								~sound_item					( );

			void				start						( sound_stream* stream );

private:
			sound_world&		m_sound_world;

	i_audio_source_voice*		m_source_voice;
			sound_stream*		m_stream;
			u32					m_stream_cursor_pcm;
private:
			void				refill_buffers				( );
public: // callback impl
	void						on_voice_processing_pass_start	( u32 bytes_required ) override;
	void						on_voice_processing_pass_end	( ) override;
	void						on_stream_end					( ) override;
	void						on_buffer_start					( void* buffer_context ) override;
	void						on_buffer_end					( void* buffer_context ) override;
	void						on_loop_end						( void* buffer_context ) override;
	void						on_voice_error					( void* buffer_context, s32 error ) override;

	// in sound thread:
			void				on_end_stream				( );
			void				on_buffer_end_impl			( void* pcontext );
			void				on_buffer_error				( void* pcontext );

}; // class sound_item

} // namespace sound
} // namespace xray

#endif // #ifndef SOUND_ITEM_H_INCLUDED
