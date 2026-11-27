////////////////////////////////////////////////////////////////////////////
//	Created		: 28.02.2011
//	Author		: Dmitry Kulikov
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef VOICE_BRIDGE_H_INCLUDED
#define VOICE_BRIDGE_H_INCLUDED

#include <xray/sound/channels_type.h>
#include "audio_backend.h"
#include "voice_format.h"
#include "voice_callback_handler.h"

namespace xray {
namespace sound {

class sound_buffer;

class voice_bridge :
	private i_audio_voice_callback,
	private xray::noncopyable
{
public:
			channels_type	get_channels_type				( ) const;
			u32				get_sample_rate					( ) const;
			u8				get_bytes_per_second			( ) const;

			void			start							( );
			void			stop							( );
			void			submit_source_buffer			( sound_buffer* buffer, u32 playing_offset = 0, u32 playing_length = 0 );
			void			flush_source_buffers			( );

			u32				buffers_queued					( ) const;
			sound_buffer*	current_sound_buffer			( ) const;
			u64				samples_played					( ) const;

			void			set_output_matrix				( float const* level_matrix );
			void			set_channel_volumes				( channels_type type, float const* level_matrix );
			void			set_low_pass_filter_params		( float coeff );
			void			set_output_voice				( i_audio_submix* output_voice );
			void			set_sample_rate					( u32 new_sample_rate );
			void			set_frequency_ratio				( float ratio );
private:
	struct creation_parametrs
	{
		i_audio_backend*	audio_backend;
		float				max_frequency_ratio;
		channels_type		type;
		channels_type		master_channels_type;
		
	}; // struct creation_parametrs

							voice_bridge					( creation_parametrs& params );

			bool			has_handler						( ) const { return m_handler != 0; }
			void			set_handler						( voice_callback_handler* handler );
			

	void					on_voice_processing_pass_start	( u32 bytes_required ) override;
	void					on_voice_processing_pass_end	( ) override;
	void					on_stream_end					( ) override;
	void					on_buffer_start					( void* buffer_context ) override;
	void					on_buffer_end					( void* buffer_context ) override;
	void					on_loop_end						( void* buffer_context ) override;
	void					on_voice_error					( void* buffer_context, s32 error ) override;

public:
	voice_bridge*			m_next;
	virtual					~voice_bridge					( );
private:
	i_audio_backend*		m_backend;
	voice_callback_handler*	m_handler;
	i_audio_source_voice*	m_source_voice;
	channels_type			m_master_channels;

	friend class			voice_factory;
}; // class voice_bridge

typedef intrusive_list<voice_bridge, voice_bridge*, &voice_bridge::m_next, threading::single_threading_policy> voice_list_type;

} // namespace sound
} // namespace xray

#endif // #ifndef VOICE_BRIDGE_H_INCLUDED
