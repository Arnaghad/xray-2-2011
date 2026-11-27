#include "test_check.hpp"

#include <xray/sound/sources/audio_backend.h>

#include <chrono>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <thread>
#include <vector>

namespace {

class recording_voice_callback final : public xray::sound::i_audio_voice_callback
{
public:
	int pass_start_count = 0;
	int pass_end_count = 0;
	int buffer_start_count = 0;
	int buffer_end_count = 0;
	int stream_end_count = 0;
	int loop_end_count = 0;
	int error_count = 0;
	void* last_start_ctx = nullptr;
	void* last_end_ctx = nullptr;

	xray::sound::i_audio_source_voice* voice_for_reentrant_check = nullptr;
	bool check_reentrant_api = false;
	u32 queued_inside_end = 9999;

	void on_voice_processing_pass_start( u32 ) override { ++pass_start_count; }
	void on_voice_processing_pass_end() override { ++pass_end_count; }
	void on_stream_end() override { ++stream_end_count; }
	void on_buffer_start( void* buffer_context ) override
	{
		++buffer_start_count;
		last_start_ctx = buffer_context;
	}
	void on_buffer_end( void* buffer_context ) override
	{
		++buffer_end_count;
		last_end_ctx = buffer_context;
		if ( check_reentrant_api && voice_for_reentrant_check ) {
			// Verify re-entrant calls inside callback do not deadlock and see popped queue count.
			queued_inside_end = voice_for_reentrant_check->buffers_queued();
		}
	}
	void on_loop_end( void* ) override { ++loop_end_count; }
	void on_voice_error( void*, s32 ) override { ++error_count; }
};

} // namespace

int main()
{
	using namespace xray::sound;

	i_audio_backend* backend = create_audio_backend();
	XRAY_TEST_CHECK( backend != nullptr );

	if ( !backend->initialize( false ) ) {
		std::cout << "SKIP (no audio backend device/driver): xray_audio_backend_contract\n";
		destroy_audio_backend( backend );
		return 0;
	}

	XRAY_TEST_CHECK( backend->backend_name() != nullptr );
	XRAY_TEST_CHECK( backend->master_sample_rate() >= 22050 );
	XRAY_TEST_CHECK( backend->min_frequency_ratio() > 0.0f && backend->min_frequency_ratio() < 1.0f );
	XRAY_TEST_CHECK( backend->max_frequency_ratio() >= 2.0f );

	i_audio_submix* submix = backend->create_submix();
	XRAY_TEST_CHECK( submix != nullptr );
	submix->set_volume( 0.5f );
	XRAY_TEST_CHECK( std::abs( submix->get_volume() - 0.5f ) < 1e-5f );

	recording_voice_callback cb;
	audio_voice_desc desc;
	desc.channels = mono;
	desc.sample_rate = backend->master_sample_rate();
	desc.bits_per_sample = 16;
	desc.max_frequency_ratio = 2.0f;
	desc.use_filter = true;

	i_audio_source_voice* voice = backend->create_voice( desc, &cb );
	XRAY_TEST_CHECK( voice != nullptr );
	XRAY_TEST_CHECK( voice->channels() == mono );
	XRAY_TEST_CHECK( voice->sample_rate() == desc.sample_rate );

	cb.voice_for_reentrant_check = voice;
	cb.check_reentrant_api = true;

	voice->set_output_submix( submix );
	// Pan hard-left: src(1) -> dst(2), matrix row-major dst x src = [left=1.0, right=0.0]
	float const hard_left_matrix[2] = { 1.0f, 0.0f };
	voice->set_output_matrix( 1, 2, hard_left_matrix );

	float const channel_vol[1] = { 1.0f };
	voice->set_channel_volumes( 1, channel_vol );
	voice->set_low_pass_filter( 1.0f ); // bypass LPF

	constexpr u32 k_frames = 256;
	std::vector<s16> pcm( k_frames, 16384 ); // constant +0.5 amplitude
	int marker = 42;

	audio_buffer_desc buf;
	buf.flags = audio_buffer_flag_end_of_stream;
	buf.audio_bytes = static_cast<u32>( pcm.size() * sizeof( s16 ) );
	buf.audio_data = reinterpret_cast<u8 const*>( pcm.data() );
	buf.play_begin = 0;
	buf.play_length = k_frames;
	buf.context = &marker;

	voice->submit_buffer( buf );
	XRAY_TEST_CHECK( voice->buffers_queued() == 1 );
	XRAY_TEST_CHECK( voice->current_buffer_context() == &marker );
	voice->start();

	std::vector<float> mix_probe( k_frames * 2, 0.0f );
	if ( backend->render_mix_for_testing( mix_probe.data(), k_frames ) ) {
		// Software mixer active (SDL3 backend): verify gain, panning, submix scaling & callbacks.
		float left_sum = 0.0f;
		float right_sum = 0.0f;
		for ( u32 i = 0; i < k_frames; ++i ) {
			left_sum += std::abs( mix_probe[i * 2 + 0] );
			right_sum += std::abs( mix_probe[i * 2 + 1] );
		}
		XRAY_TEST_CHECK( left_sum > 10.0f );
		XRAY_TEST_CHECK( right_sum < 1e-4f );
		// Expected average per-frame amplitude: 0.5 (pcm) * 1.0 (pan) * 0.5 (submix) = 0.25
		float const avg_left = left_sum / static_cast<float>( k_frames );
		XRAY_TEST_CHECK( std::abs( avg_left - 0.25f ) < 0.02f );

		XRAY_TEST_CHECK( cb.buffer_start_count == 1 );
		XRAY_TEST_CHECK( cb.buffer_end_count == 1 );
		XRAY_TEST_CHECK( cb.stream_end_count == 1 );
		XRAY_TEST_CHECK( cb.last_start_ctx == &marker );
		XRAY_TEST_CHECK( cb.last_end_ctx == &marker );
		XRAY_TEST_CHECK( cb.queued_inside_end == 0 );
		XRAY_TEST_CHECK( voice->buffers_queued() == 0 );
		XRAY_TEST_CHECK( voice->samples_played() == k_frames );

		// Verify 1-pole LPF attenuates alternating Nyquist square wave
		std::vector<s16> hf_pcm( 128 );
		for ( u32 i = 0; i < hf_pcm.size(); ++i )
			hf_pcm[i] = ( i & 1 ) ? -24000 : 24000;

		audio_buffer_desc hf_buf;
		hf_buf.flags = audio_buffer_flag_end_of_stream;
		hf_buf.audio_bytes = static_cast<u32>( hf_pcm.size() * sizeof( s16 ) );
		hf_buf.audio_data = reinterpret_cast<u8 const*>( hf_pcm.data() );
		hf_buf.play_length = 128;

		submix->set_volume( 1.0f );
		voice->flush_buffers();
		voice->set_low_pass_filter( 0.05f );
		voice->set_sample_rate( desc.sample_rate );
		voice->set_frequency_ratio( 1.0f );
		voice->submit_buffer( hf_buf );

		std::vector<float> lpf_mix( 128 * 2, 0.0f );
		XRAY_TEST_CHECK( backend->render_mix_for_testing( lpf_mix.data(), 128 ) );
		float max_lpf = 0.0f;
		for ( u32 i = 0; i < 128; ++i )
			max_lpf = std::max( max_lpf, std::abs( lpf_mix[i * 2] ) );
		XRAY_TEST_CHECK( max_lpf < 0.15f );
	} else {
		// Hardware backend (XAudio2): Stop/FlushSourceBuffers retire asynchronously across the audio quantum
		voice->stop();
		voice->flush_buffers();
		for ( int attempt = 0; attempt < 100 && voice->buffers_queued() != 0; ++attempt ) {
			std::this_thread::sleep_for( std::chrono::milliseconds( 5 ) );
			if ( attempt == 4 )
				voice->flush_buffers();
		}
		XRAY_TEST_CHECK( voice->buffers_queued() == 0 );
	}

	voice->stop();
	voice->flush_buffers();
	voice->set_output_submix( nullptr );

	backend->destroy_voice( voice );
	backend->destroy_submix( submix );
	backend->shutdown();
	destroy_audio_backend( backend );
	XRAY_TEST_CHECK( backend == nullptr );

	std::cout << "PASS: xray_audio_backend_contract\n";
	return 0;
}
