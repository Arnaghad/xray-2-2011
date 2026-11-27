#include "pch.h"

#if defined(XRAY_AUDIO_BACKEND_SDL3)

#include "audio_backend.h"

#include <SDL3/SDL.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <mutex>
#include <vector>

namespace xray {
namespace sound {

namespace {

class sdl3_submix;
class sdl3_source_voice;

struct queued_pcm_buffer
{
	audio_buffer_desc desc;
	u32               start_frame = 0;
	u32               end_frame = 0;
	u32               total_frames = 0;
	double            cursor_frame = 0.0;
	bool              started = false;
};

class sdl3_submix final : public i_audio_submix
{
public:
	explicit sdl3_submix( std::recursive_mutex& mutex ) noexcept
		: m_mutex( mutex )
	{
	}

	void set_volume( float const volume ) override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		m_volume = std::max( 0.0f, volume );
	}

	float get_volume() const override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		return m_volume;
	}

private:
	std::recursive_mutex& m_mutex;
	float                 m_volume = 1.0f;
};

class sdl3_source_voice final : public i_audio_source_voice
{
public:
	sdl3_source_voice(
		std::recursive_mutex& mutex,
		audio_voice_desc const& desc,
		channels_type master_channels,
		i_audio_voice_callback* callback )
		: m_mutex( mutex )
		, m_channels( desc.channels )
		, m_sample_rate( desc.sample_rate ? desc.sample_rate : 44100u )
		, m_bits_per_sample( desc.bits_per_sample ? desc.bits_per_sample : static_cast<u16>( 16 ) )
		, m_max_freq_ratio( desc.max_frequency_ratio > 0.0f ? desc.max_frequency_ratio : 2.0f )
		, m_master_channels( master_channels )
		, m_callback( callback )
	{
		reset_default_routing();
	}

	void reset_default_routing()
	{
		u32 const src = channels_type_to_count( m_channels );
		u32 const dst = channels_type_to_count( m_master_channels );
		m_channel_volumes.fill( 1.0f );
		m_output_matrix.fill( 0.0f );
		if ( src == 1 && dst == 2 ) {
			m_output_matrix[0] = 0.70710678f;
			m_output_matrix[1] = 0.70710678f;
		} else {
			for ( u32 c = 0; c < std::min( src, dst ); ++c )
				m_output_matrix[c * src + c] = 1.0f;
		}
	}

	channels_type channels() const override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		return m_channels;
	}

	u32 sample_rate() const override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		return m_sample_rate;
	}

	void start() override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		m_playing = true;
	}

	void stop() override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		m_playing = false;
	}

	void submit_buffer( audio_buffer_desc const& buffer ) override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		if ( !buffer.audio_data || buffer.audio_bytes == 0 )
			return;

		u32 const ch = channels_type_to_count( m_channels );
		u32 const frame_bytes = ch * ( m_bits_per_sample / 8u );
		if ( frame_bytes == 0 )
			return;

		u32 const total_frames = buffer.audio_bytes / frame_bytes;
		if ( total_frames == 0 )
			return;

		u32 const begin_frame = std::min( buffer.play_begin, total_frames );
		u32 end_frame = total_frames;
		if ( buffer.play_length > 0 )
			end_frame = std::min( total_frames, begin_frame + buffer.play_length );

		if ( begin_frame >= end_frame )
			return;

		queued_pcm_buffer q;
		q.desc = buffer;
		q.start_frame = begin_frame;
		q.end_frame = end_frame;
		q.total_frames = total_frames;
		q.cursor_frame = static_cast<double>( begin_frame );
		q.started = false;
		m_queue.push_back( q );
	}

	void flush_buffers() override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		m_queue.clear();
		m_lpf_state.fill( 0.0f );
		++m_flush_generation;
	}

	u32 buffers_queued() const override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		return static_cast<u32>( m_queue.size() );
	}

	void* current_buffer_context() const override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		if ( m_queue.empty() )
			return nullptr;
		return m_queue.front().desc.context;
	}

	u64 samples_played() const override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		return m_samples_played;
	}

	void set_output_matrix( u32 src_channels, u32 dst_channels, float const* level_matrix ) override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		if ( !level_matrix || src_channels == 0 || dst_channels == 0 || src_channels > 2 || dst_channels > 2 )
			return;
		m_output_matrix.fill( 0.0f );
		for ( u32 d = 0; d < dst_channels; ++d ) {
			for ( u32 s = 0; s < src_channels; ++s )
				m_output_matrix[d * src_channels + s] = level_matrix[d * src_channels + s];
		}
	}

	void set_channel_volumes( u32 channels_count, float const* volumes ) override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		if ( !volumes || channels_count == 0 || channels_count > 2 )
			return;
		for ( u32 c = 0; c < channels_count; ++c )
			m_channel_volumes[c] = std::max( 0.0f, volumes[c] );
	}

	void set_low_pass_filter( float coeff ) override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		m_lpf_coeff = std::clamp( coeff, 0.0f, 1.0f );
	}

	void set_output_submix( i_audio_submix* submix ) override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		m_submix = static_cast<sdl3_submix*>( submix );
	}

	void set_sample_rate( u32 new_sample_rate ) override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		if ( new_sample_rate > 0 )
			m_sample_rate = new_sample_rate;
	}

	void set_frequency_ratio( float ratio ) override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		m_frequency_ratio = std::clamp( ratio, 0.0f, m_max_freq_ratio );
	}

	void mix_into( float* out_interleaved, u32 frame_count, u32 master_sample_rate, u32 master_channels )
	{
		if ( !m_playing || m_queue.empty() || frame_count == 0 || master_sample_rate == 0 )
			return;

		u32 const src_channels = channels_type_to_count( m_channels );
		u32 const dst_channels = std::min( master_channels, static_cast<u32>( 2 ) );
		double const step = ( static_cast<double>( m_sample_rate ) * static_cast<double>( m_frequency_ratio ) )
			/ static_cast<double>( master_sample_rate );

		if ( m_callback )
			m_callback->on_voice_processing_pass_start( frame_count * dst_channels * sizeof( float ) );

		for ( u32 f = 0; f < frame_count; ++f ) {
			while ( m_playing && !m_queue.empty() ) {
				u64 const gen_before = m_flush_generation;
				queued_pcm_buffer& buf = m_queue.front();

				if ( !buf.started ) {
					buf.started = true;
					if ( m_callback ) {
						void* ctx = buf.desc.context;
						m_callback->on_buffer_start( ctx );
						if ( !m_playing || m_queue.empty() || m_flush_generation != gen_before )
							continue;
					}
				}

				if ( m_queue.empty() )
					break;

				queued_pcm_buffer& active = m_queue.front();
				if ( active.cursor_frame >= static_cast<double>( active.end_frame ) ) {
					void* const ctx = active.desc.context;
					bool const is_eos = ( active.desc.flags & audio_buffer_flag_end_of_stream ) != 0;
					m_queue.pop_front();
					if ( m_callback ) {
						m_callback->on_buffer_end( ctx );
						if ( is_eos )
							m_callback->on_stream_end();
					}
					continue;
				}

				u32 const idx0 = std::min( static_cast<u32>( active.cursor_frame ), active.end_frame - 1 );
				u32 const idx1 = std::min( idx0 + 1, active.end_frame - 1 );
				float const frac = static_cast<float>( active.cursor_frame - static_cast<double>( idx0 ) );

				auto const* pcm16 = reinterpret_cast<s16 const*>( active.desc.audio_data );
				float src_samples[2] = { 0.0f, 0.0f };
				float const lpf = m_lpf_coeff;
				bool const use_lpf = ( lpf < 0.9999f );
				float const alpha = std::clamp( lpf, 0.001f, 1.0f );

				for ( u32 s = 0; s < src_channels; ++s ) {
					float const s0 = static_cast<float>( pcm16[idx0 * src_channels + s] ) / 32768.0f;
					float const s1 = static_cast<float>( pcm16[idx1 * src_channels + s] ) / 32768.0f;
					float sample = ( s0 + ( s1 - s0 ) * frac ) * m_channel_volumes[s];
					if ( use_lpf ) {
						m_lpf_state[s] += alpha * ( sample - m_lpf_state[s] );
						sample = m_lpf_state[s];
					} else {
						m_lpf_state[s] = sample;
					}
					src_samples[s] = sample;
				}

				float const submix_vol = m_submix ? m_submix->get_volume() : 1.0f;
				for ( u32 d = 0; d < dst_channels; ++d ) {
					float sum = 0.0f;
					for ( u32 s = 0; s < src_channels; ++s )
						sum += src_samples[s] * m_output_matrix[d * src_channels + s];
					out_interleaved[f * master_channels + d] += sum * submix_vol;
				}

				u32 const old_int = static_cast<u32>( active.cursor_frame );
				active.cursor_frame += ( step > 0.0 ? step : 0.0 );
				u32 const new_int = std::min( static_cast<u32>( active.cursor_frame ), active.end_frame );
				if ( new_int > old_int )
					m_samples_played += static_cast<u64>( new_int - old_int );

				if ( active.cursor_frame >= static_cast<double>( active.end_frame ) ) {
					void* const ctx = active.desc.context;
					bool const is_eos = ( active.desc.flags & audio_buffer_flag_end_of_stream ) != 0;
					m_queue.pop_front();
					if ( m_callback ) {
						m_callback->on_buffer_end( ctx );
						if ( is_eos )
							m_callback->on_stream_end();
					}
				}
				break;
			}
		}

		if ( m_callback )
			m_callback->on_voice_processing_pass_end();
	}

private:
	std::recursive_mutex&         m_mutex;
	channels_type                 m_channels = mono;
	u32                           m_sample_rate = 44100;
	u16                           m_bits_per_sample = 16;
	float                         m_max_freq_ratio = 2.0f;
	float                         m_frequency_ratio = 1.0f;
	float                         m_lpf_coeff = 1.0f;
	channels_type                 m_master_channels = stereo;
	i_audio_voice_callback*       m_callback = nullptr;
	sdl3_submix*                  m_submix = nullptr;
	std::array<float, 2>          m_channel_volumes = { 1.0f, 1.0f };
	std::array<float, 4>          m_output_matrix = { 1.0f, 0.0f, 0.0f, 1.0f };
	std::array<float, 2>          m_lpf_state = { 0.0f, 0.0f };
	std::deque<queued_pcm_buffer> m_queue;
	u64                           m_samples_played = 0;
	u64                           m_flush_generation = 0;
	bool                          m_playing = false;
};

class sdl3_audio_backend final : public i_audio_backend
{
public:
	~sdl3_audio_backend() override
	{
		shutdown();
	}

	bool initialize( bool const /*debug_mode*/ ) override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		if ( m_initialized )
			return true;

		if ( SDL_WasInit( SDL_INIT_AUDIO ) == 0 ) {
			if ( !SDL_InitSubSystem( SDL_INIT_AUDIO ) )
				return false;
			m_owns_audio_init = true;
		}

		int preferred_freq = 48000;
		SDL_AudioSpec dev_spec = {};
		if ( SDL_GetAudioDeviceFormat( SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &dev_spec, nullptr ) ) {
			if ( dev_spec.freq >= 22050 && dev_spec.freq <= 192000 )
				preferred_freq = dev_spec.freq;
		}

		SDL_AudioSpec spec = {};
		spec.format = SDL_AUDIO_F32;
		spec.channels = 2;
		spec.freq = preferred_freq;

		m_stream = SDL_OpenAudioDeviceStream(
			SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK,
			&spec,
			&sdl3_audio_backend::sdl_audio_callback,
			this
		);
		if ( !m_stream ) {
			if ( m_owns_audio_init ) {
				SDL_QuitSubSystem( SDL_INIT_AUDIO );
				m_owns_audio_init = false;
			}
			return false;
		}

		m_master_sample_rate = static_cast<u32>( spec.freq );
		m_master_channels = stereo;
		m_initialized = true;
		SDL_ResumeAudioStreamDevice( m_stream );
		return true;
	}

	void shutdown() override
	{
		SDL_AudioStream* stream_to_destroy = nullptr;
		bool owns_init = false;
		{
			std::lock_guard<std::recursive_mutex> lock( m_mutex );
			stream_to_destroy = m_stream;
			m_stream = nullptr;
			owns_init = m_owns_audio_init;
			m_owns_audio_init = false;
			m_initialized = false;
		}

		// Destroy stream outside m_mutex so any in-flight SDL audio thread callback can finish cleanly.
		if ( stream_to_destroy )
			SDL_DestroyAudioStream( stream_to_destroy );

		{
			std::lock_guard<std::recursive_mutex> lock( m_mutex );
			for ( auto* voice : m_voices )
				delete voice;
			m_voices.clear();
			for ( auto* submix : m_submixes )
				delete submix;
			m_submixes.clear();
		}

		if ( owns_init )
			SDL_QuitSubSystem( SDL_INIT_AUDIO );
	}

	char const* backend_name() const override
	{
		char const* drv = SDL_GetCurrentAudioDriver();
		return ( drv && *drv ) ? drv : "SDL3-Audio";
	}

	channels_type master_channel_type() const override { return m_master_channels; }
	u32 master_sample_rate() const override { return m_master_sample_rate; }
	float min_frequency_ratio() const override { return 1.0f / 1024.0f; }
	float max_frequency_ratio() const override { return 2.0f; }

	i_audio_submix* create_submix() override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		if ( !m_initialized )
			return nullptr;
		auto* s = new sdl3_submix( m_mutex );
		m_submixes.push_back( s );
		return s;
	}

	void destroy_submix( i_audio_submix* submix ) override
	{
		if ( !submix )
			return;
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		auto* s = static_cast<sdl3_submix*>( submix );
		m_submixes.erase( std::remove( m_submixes.begin(), m_submixes.end(), s ), m_submixes.end() );
		delete s;
	}

	i_audio_source_voice* create_voice( audio_voice_desc const& desc, i_audio_voice_callback* callback ) override
	{
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		if ( !m_initialized )
			return nullptr;
		auto* v = new sdl3_source_voice( m_mutex, desc, m_master_channels, callback );
		m_voices.push_back( v );
		return v;
	}

	void destroy_voice( i_audio_source_voice* voice ) override
	{
		if ( !voice )
			return;
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		auto* v = static_cast<sdl3_source_voice*>( voice );
		m_voices.erase( std::remove( m_voices.begin(), m_voices.end(), v ), m_voices.end() );
		delete v;
	}

	bool render_mix_for_testing( float* interleaved_out, u32 frame_count ) override
	{
		if ( !interleaved_out || frame_count == 0 )
			return false;
		std::lock_guard<std::recursive_mutex> lock( m_mutex );
		if ( !m_initialized )
			return false;
		mix_frames( interleaved_out, frame_count );
		return true;
	}

private:
	static void SDLCALL sdl_audio_callback(
		void* userdata,
		SDL_AudioStream* stream,
		int additional_amount,
		int /*total_amount*/ )
	{
		auto* self = static_cast<sdl3_audio_backend*>( userdata );
		if ( !self || !stream || additional_amount <= 0 )
			return;

		constexpr u32 k_chunk_frames = 512u;
		constexpr u32 k_channels = 2u;
		int remaining_bytes = additional_amount;
		float buffer[k_chunk_frames * k_channels];

		std::lock_guard<std::recursive_mutex> lock( self->m_mutex );
		if ( !self->m_initialized )
			return;

		while ( remaining_bytes > 0 ) {
			u32 const req_frames = static_cast<u32>( remaining_bytes ) / ( k_channels * sizeof( float ) );
			u32 const frames = std::min( k_chunk_frames, std::max<u32>( 1u, req_frames ) );
			self->mix_frames( buffer, frames );
			int const byte_count = static_cast<int>( frames * k_channels * sizeof( float ) );
			SDL_PutAudioStreamData( stream, buffer, byte_count );
			remaining_bytes -= byte_count;
		}
	}

	void mix_frames( float* out_interleaved, u32 frame_count )
	{
		u32 const ch = channels_type_to_count( m_master_channels );
		std::fill( out_interleaved, out_interleaved + static_cast<std::size_t>( frame_count ) * ch, 0.0f );

		std::vector<sdl3_source_voice*> snapshot = m_voices;
		for ( auto* voice : snapshot ) {
			if ( voice )
				voice->mix_into( out_interleaved, frame_count, m_master_sample_rate, ch );
		}

		std::size_t const total_samples = static_cast<std::size_t>( frame_count ) * ch;
		for ( std::size_t i = 0; i < total_samples; ++i )
			out_interleaved[i] = std::clamp( out_interleaved[i], -1.0f, 1.0f );
	}

	mutable std::recursive_mutex    m_mutex;
	SDL_AudioStream*                m_stream = nullptr;
	std::vector<sdl3_submix*>       m_submixes;
	std::vector<sdl3_source_voice*> m_voices;
	channels_type                   m_master_channels = stereo;
	u32                             m_master_sample_rate = 48000;
	bool                            m_initialized = false;
	bool                            m_owns_audio_init = false;
};

} // namespace

i_audio_backend* create_audio_backend()
{
	return new sdl3_audio_backend();
}

void destroy_audio_backend( i_audio_backend*& backend )
{
	if ( backend ) {
		backend->shutdown();
		delete backend;
		backend = nullptr;
	}
}

} // namespace sound
} // namespace xray

#endif // XRAY_AUDIO_BACKEND_SDL3
