#include "pch.h"

#if defined(XRAY_AUDIO_BACKEND_XAUDIO2)

#include "audio_backend.h"
#include <xray/os_platform.h>

#include <xaudio2.h>
#include <algorithm>
#include <memory>

namespace xray {
namespace sound {

namespace {

WORD channels_to_xaudio( channels_type const type ) noexcept
{
	return static_cast<WORD>( channels_type_to_count( type ) );
}

class xaudio2_submix final : public i_audio_submix
{
public:
	explicit xaudio2_submix( IXAudio2SubmixVoice* voice ) noexcept : m_voice( voice ) {}
	~xaudio2_submix() override
	{
		if ( m_voice ) {
			m_voice->DestroyVoice();
			m_voice = nullptr;
		}
	}

	IXAudio2SubmixVoice* raw() const noexcept { return m_voice; }

	void set_volume( float const volume ) override
	{
		if ( m_voice ) {
			m_voice->SetVolume( volume );
			m_volume = volume;
		}
	}

	float get_volume() const override
	{
		return m_volume;
	}

private:
	IXAudio2SubmixVoice* m_voice = nullptr;
	float                m_volume = 1.0f;
};

class xaudio2_source_voice final : public i_audio_source_voice, private IXAudio2VoiceCallback
{
public:
	xaudio2_source_voice(
		IXAudio2* engine,
		audio_voice_desc const& desc,
		channels_type master_channels,
		i_audio_voice_callback* callback )
		: m_channels( desc.channels )
		, m_master_channels( master_channels )
		, m_callback( callback )
	{
		WAVEFORMATEX wfx = {};
		wfx.wFormatTag = WAVE_FORMAT_PCM;
		wfx.nChannels = channels_to_xaudio( desc.channels );
		wfx.nSamplesPerSec = desc.sample_rate;
		wfx.wBitsPerSample = desc.bits_per_sample;
		wfx.cbSize = 0;
		wfx.nBlockAlign = static_cast<WORD>( wfx.nChannels * ( wfx.wBitsPerSample >> 3 ) );
		wfx.nAvgBytesPerSec = wfx.nSamplesPerSec * wfx.nBlockAlign;

		UINT32 const flags = desc.use_filter ? XAUDIO2_VOICE_USEFILTER : 0u;
		HRESULT const hr = engine->CreateSourceVoice(
			&m_voice,
			&wfx,
			flags,
			desc.max_frequency_ratio,
			this,
			nullptr,
			nullptr
		);
		if ( FAILED( hr ) )
			m_voice = nullptr;
	}

	~xaudio2_source_voice() override
	{
		if ( m_voice ) {
			m_voice->DestroyVoice();
			m_voice = nullptr;
		}
	}

	bool valid() const noexcept { return m_voice != nullptr; }

	channels_type channels() const override
	{
		if ( !m_voice )
			return m_channels;
		XAUDIO2_VOICE_DETAILS details = {};
		m_voice->GetVoiceDetails( &details );
		return count_to_channels_type( details.InputChannels );
	}

	u32 sample_rate() const override
	{
		if ( !m_voice )
			return 0;
		XAUDIO2_VOICE_DETAILS details = {};
		m_voice->GetVoiceDetails( &details );
		return details.InputSampleRate;
	}

	void start() override
	{
		if ( m_voice )
			m_voice->Start( 0 );
	}

	void stop() override
	{
		if ( m_voice )
			m_voice->Stop();
	}

	void submit_buffer( audio_buffer_desc const& buffer ) override
	{
		if ( !m_voice )
			return;
		XAUDIO2_BUFFER xb = {};
		xb.Flags = ( buffer.flags & audio_buffer_flag_end_of_stream ) ? XAUDIO2_END_OF_STREAM : 0u;
		xb.AudioBytes = buffer.audio_bytes;
		xb.pAudioData = buffer.audio_data;
		xb.PlayBegin = buffer.play_begin;
		xb.PlayLength = buffer.play_length;
		xb.LoopBegin = buffer.loop_begin;
		xb.LoopLength = buffer.loop_length;
		xb.LoopCount = buffer.loop_count;
		xb.pContext = buffer.context;
		m_voice->SubmitSourceBuffer( &xb );
	}

	void flush_buffers() override
	{
		if ( m_voice )
			m_voice->FlushSourceBuffers();
	}

	u32 buffers_queued() const override
	{
		if ( !m_voice )
			return 0;
		XAUDIO2_VOICE_STATE st = {};
		m_voice->GetState( &st );
		return st.BuffersQueued;
	}

	void* current_buffer_context() const override
	{
		if ( !m_voice )
			return nullptr;
		XAUDIO2_VOICE_STATE st = {};
		m_voice->GetState( &st );
		return st.pCurrentBufferContext;
	}

	u64 samples_played() const override
	{
		if ( !m_voice )
			return 0;
		XAUDIO2_VOICE_STATE st = {};
		m_voice->GetState( &st );
		return st.SamplesPlayed;
	}

	void set_output_matrix( u32 src_channels, u32 dst_channels, float const* level_matrix ) override
	{
		if ( m_voice && level_matrix )
			m_voice->SetOutputMatrix( nullptr, src_channels, dst_channels, level_matrix );
	}

	void set_channel_volumes( u32 channels, float const* volumes ) override
	{
		if ( m_voice && volumes )
			m_voice->SetChannelVolumes( channels, volumes );
	}

	void set_low_pass_filter( float coeff ) override
	{
		if ( !m_voice )
			return;
		XAUDIO2_FILTER_PARAMETERS params = {};
		params.Type = LowPassFilter;
		params.Frequency = coeff;
		params.OneOverQ = 1.0f;
		m_voice->SetFilterParameters( &params );
	}

	void set_output_submix( i_audio_submix* submix ) override
	{
		if ( !m_voice )
			return;
		if ( submix ) {
			auto* xm = static_cast<xaudio2_submix*>( submix );
			XAUDIO2_SEND_DESCRIPTOR desc = {};
			desc.Flags = 0;
			desc.pOutputVoice = xm->raw();
			XAUDIO2_VOICE_SENDS sends = {};
			sends.SendCount = 1;
			sends.pSends = &desc;
			m_voice->SetOutputVoices( &sends );
		} else {
			m_voice->SetOutputVoices( nullptr );
		}
	}

	void set_sample_rate( u32 new_sample_rate ) override
	{
		if ( m_voice )
			m_voice->SetSourceSampleRate( new_sample_rate );
	}

	void set_frequency_ratio( float ratio ) override
	{
		if ( m_voice )
			m_voice->SetFrequencyRatio( ratio );
	}

private:
	void __stdcall OnVoiceProcessingPassStart( UINT32 BytesRequired ) override
	{
		if ( m_callback )
			m_callback->on_voice_processing_pass_start( BytesRequired );
	}

	void __stdcall OnVoiceProcessingPassEnd() override
	{
		if ( m_callback )
			m_callback->on_voice_processing_pass_end();
	}

	void __stdcall OnStreamEnd() override
	{
		if ( m_callback )
			m_callback->on_stream_end();
	}

	void __stdcall OnBufferStart( void* pBufferContext ) override
	{
		if ( m_callback )
			m_callback->on_buffer_start( pBufferContext );
	}

	void __stdcall OnBufferEnd( void* pBufferContext ) override
	{
		if ( m_callback )
			m_callback->on_buffer_end( pBufferContext );
	}

	void __stdcall OnLoopEnd( void* pBufferContext ) override
	{
		if ( m_callback )
			m_callback->on_loop_end( pBufferContext );
	}

	void __stdcall OnVoiceError( void* pBufferContext, HRESULT Error ) override
	{
		if ( m_callback )
			m_callback->on_voice_error( pBufferContext, static_cast<s32>( Error ) );
	}

	IXAudio2SourceVoice*   m_voice = nullptr;
	channels_type          m_channels = mono;
	channels_type          m_master_channels = stereo;
	i_audio_voice_callback* m_callback = nullptr;
};

class xaudio2_audio_backend final : public i_audio_backend
{
public:
	~xaudio2_audio_backend() override
	{
		shutdown();
	}

	bool initialize( bool const debug_mode ) override
	{
		if ( !m_com )
			m_com = std::make_unique<xray::os::scoped_com_init>( true );

		u32 creation_flags = debug_mode ? XAUDIO2_DEBUG_ENGINE : 0u;
		HRESULT hr = XAudio2Create( &m_xaudio, creation_flags, XAUDIO2_DEFAULT_PROCESSOR );
		if ( FAILED( hr ) && creation_flags != 0 ) {
			m_xaudio = nullptr;
			hr = XAudio2Create( &m_xaudio, 0, XAUDIO2_DEFAULT_PROCESSOR );
		}
		if ( FAILED( hr ) || !m_xaudio ) {
			m_xaudio = nullptr;
			return false;
		}

		hr = m_xaudio->CreateMasteringVoice(
			&m_master_voice,
			XAUDIO2_DEFAULT_CHANNELS,
			XAUDIO2_DEFAULT_SAMPLERATE,
			0,
			nullptr,
			nullptr
		);
		if ( FAILED( hr ) || !m_master_voice ) {
			m_master_voice = nullptr;
			m_xaudio->Release();
			m_xaudio = nullptr;
			return false;
		}

		XAUDIO2_VOICE_DETAILS details = {};
		m_master_voice->GetVoiceDetails( &details );
		m_master_channels = count_to_channels_type( details.InputChannels );
		m_master_sample_rate = details.InputSampleRate;
		return true;
	}

	void shutdown() override
	{
		if ( m_master_voice ) {
			m_master_voice->DestroyVoice();
			m_master_voice = nullptr;
		}
		if ( m_xaudio ) {
			m_xaudio->Release();
			m_xaudio = nullptr;
		}
		m_com.reset();
	}

	char const* backend_name() const override { return "XAudio2.9"; }
	channels_type master_channel_type() const override { return m_master_channels; }
	u32 master_sample_rate() const override { return m_master_sample_rate; }
	float min_frequency_ratio() const override { return XAUDIO2_MIN_FREQ_RATIO; }
	float max_frequency_ratio() const override { return XAUDIO2_DEFAULT_FREQ_RATIO; }

	i_audio_submix* create_submix() override
	{
		if ( !m_xaudio || !m_master_voice )
			return nullptr;
		IXAudio2SubmixVoice* submix_voice = nullptr;
		HRESULT hr = m_xaudio->CreateSubmixVoice(
			&submix_voice,
			channels_to_xaudio( m_master_channels ),
			m_master_sample_rate
		);
		if ( FAILED( hr ) || !submix_voice )
			return nullptr;
		submix_voice->SetOutputVoices( nullptr );
		return new xaudio2_submix( submix_voice );
	}

	void destroy_submix( i_audio_submix* submix ) override
	{
		delete static_cast<xaudio2_submix*>( submix );
	}

	i_audio_source_voice* create_voice( audio_voice_desc const& desc, i_audio_voice_callback* callback ) override
	{
		if ( !m_xaudio )
			return nullptr;
		auto* voice = new xaudio2_source_voice( m_xaudio, desc, m_master_channels, callback );
		if ( !voice->valid() ) {
			delete voice;
			return nullptr;
		}
		return voice;
	}

	void destroy_voice( i_audio_source_voice* voice ) override
	{
		delete static_cast<xaudio2_source_voice*>( voice );
	}

private:
	std::unique_ptr<xray::os::scoped_com_init> m_com;
	IXAudio2*                                  m_xaudio = nullptr;
	IXAudio2MasteringVoice*                    m_master_voice = nullptr;
	channels_type                              m_master_channels = stereo;
	u32                                        m_master_sample_rate = 44100;
};

} // namespace

i_audio_backend* create_audio_backend()
{
	return new xaudio2_audio_backend();
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

#endif // XRAY_AUDIO_BACKEND_XAUDIO2
