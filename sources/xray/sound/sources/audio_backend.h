////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Portable internal audio backend abstraction (PAL-6)
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_SOUND_AUDIO_BACKEND_H_INCLUDED
#define XRAY_SOUND_AUDIO_BACKEND_H_INCLUDED

#include <xray/sound/channels_type.h>
#include <xray/type_extensions.h>

namespace xray {
namespace sound {

inline u32 channels_type_to_count( channels_type const type ) noexcept
{
	switch ( type )
	{
		case mono:		return 1;
		case stereo:	return 2;
		default:		return 2;
	}
}

inline channels_type count_to_channels_type( u32 const count ) noexcept
{
	switch ( count )
	{
		case 1:		return mono;
		case 2:		return stereo;
		default:	return channels_type_count;
	}
}

struct pcm_wave_format
{
	u16 format_tag = 1; // 1 = PCM
	u16 channels = 0;
	u32 samples_per_sec = 0;
	u32 avg_bytes_per_sec = 0;
	u16 block_align = 0;
	u16 bits_per_sample = 16;
	u16 cb_size = 0;
};

constexpr u32 audio_buffer_flag_end_of_stream = 1u;

struct audio_buffer_desc
{
	u32       flags = 0;
	u32       audio_bytes = 0;
	u8 const* audio_data = nullptr;
	u32       play_begin = 0;   // PCM frames
	u32       play_length = 0;  // PCM frames; 0 = remainder of buffer
	u32       loop_begin = 0;
	u32       loop_length = 0;
	u32       loop_count = 0;
	void*     context = nullptr;
};

struct audio_voice_desc
{
	channels_type channels = mono;
	u32           sample_rate = 44100;
	u16           bits_per_sample = 16;
	float         max_frequency_ratio = 2.0f;
	bool          use_filter = true;
};

class i_audio_voice_callback
{
public:
	virtual ~i_audio_voice_callback() = default;
	virtual void on_voice_processing_pass_start( u32 bytes_required ) = 0;
	virtual void on_voice_processing_pass_end() = 0;
	virtual void on_stream_end() = 0;
	virtual void on_buffer_start( void* buffer_context ) = 0;
	virtual void on_buffer_end( void* buffer_context ) = 0;
	virtual void on_loop_end( void* buffer_context ) = 0;
	virtual void on_voice_error( void* buffer_context, s32 error_code ) = 0;
};

class i_audio_submix
{
public:
	virtual ~i_audio_submix() = default;
	virtual void  set_volume( float volume ) = 0;
	virtual float get_volume() const = 0;
};

class i_audio_source_voice
{
public:
	virtual ~i_audio_source_voice() = default;
	virtual channels_type channels() const = 0;
	virtual u32           sample_rate() const = 0;
	virtual void          start() = 0;
	virtual void          stop() = 0;
	virtual void          submit_buffer( audio_buffer_desc const& buffer ) = 0;
	virtual void          flush_buffers() = 0;
	virtual u32           buffers_queued() const = 0;
	virtual void*         current_buffer_context() const = 0;
	virtual u64           samples_played() const = 0;
	virtual void          set_output_matrix( u32 src_channels, u32 dst_channels, float const* level_matrix ) = 0;
	virtual void          set_channel_volumes( u32 channels, float const* volumes ) = 0;
	virtual void          set_low_pass_filter( float coeff ) = 0;
	virtual void          set_output_submix( i_audio_submix* submix ) = 0;
	virtual void          set_sample_rate( u32 new_sample_rate ) = 0;
	virtual void          set_frequency_ratio( float ratio ) = 0;
};

class i_audio_backend
{
public:
	virtual ~i_audio_backend() = default;
	virtual bool                  initialize( bool debug_mode ) = 0;
	virtual void                  shutdown() = 0;
	virtual char const*           backend_name() const = 0;
	virtual channels_type         master_channel_type() const = 0;
	virtual u32                   master_sample_rate() const = 0;
	virtual float                 min_frequency_ratio() const = 0;
	virtual float                 max_frequency_ratio() const = 0;
	virtual i_audio_submix*       create_submix() = 0;
	virtual void                  destroy_submix( i_audio_submix* submix ) = 0;
	virtual i_audio_source_voice* create_voice( audio_voice_desc const& desc, i_audio_voice_callback* callback ) = 0;
	virtual void                  destroy_voice( i_audio_source_voice* voice ) = 0;
	virtual bool                  render_mix_for_testing( float* interleaved_out, u32 frame_count )
	{
		(void)interleaved_out;
		(void)frame_count;
		return false;
	}
};

i_audio_backend* create_audio_backend();
void destroy_audio_backend( i_audio_backend*& backend );

} // namespace sound
} // namespace xray

#endif // XRAY_SOUND_AUDIO_BACKEND_H_INCLUDED
