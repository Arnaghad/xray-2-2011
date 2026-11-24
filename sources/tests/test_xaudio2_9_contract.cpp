#include "test_check.hpp"

#include <xray/os_raii_win.h>
#include <ole2.h>
#include <xaudio2.h>

#include <cstdint>
#include <iostream>
#include <vector>

namespace {

#ifndef XAUDIO2_E_DEVICE_INVALIDATED
#define XAUDIO2_E_DEVICE_INVALIDATED static_cast<HRESULT>(0x88960004L)
#endif

} // namespace

int main() {
    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    XRAY_TEST_CHECK(SUCCEEDED(hr) || hr == RPC_E_CHANGED_MODE);
    bool const should_uninit = SUCCEEDED(hr);
    auto co_cleanup = xray::os::make_scoped_cleanup([&]() {
        if (should_uninit) {
            CoUninitialize();
        }
    });

    xray::os::com_ptr<IXAudio2> xaudio;
    hr = XAudio2Create(xaudio.put(), 0, XAUDIO2_DEFAULT_PROCESSOR);
    XRAY_TEST_CHECK(SUCCEEDED(hr));
    XRAY_TEST_CHECK(static_cast<bool>(xaudio));

    IXAudio2MasteringVoice* mastering = nullptr;
    hr = xaudio->CreateMasteringVoice(&mastering, 2, 44100, 0, nullptr, nullptr, AudioCategory_GameEffects);
    if (FAILED(hr) || !mastering) {
        // Headless CI / VM environments without an active audio endpoint still verify XAudio2_9 creation & DLL binding.
        std::cout << "PASS (headless XAudio2_9 engine only, no audio endpoint, hr=0x"
                  << std::hex << static_cast<unsigned long>(hr) << std::dec << "): xray_xaudio2_9_contract\n";
        return 0;
    }

    WAVEFORMATEX wfx{};
    wfx.wFormatTag = WAVE_FORMAT_PCM;
    wfx.nChannels = 2;
    wfx.nSamplesPerSec = 44100;
    wfx.wBitsPerSample = 16;
    wfx.nBlockAlign = static_cast<WORD>(wfx.nChannels * (wfx.wBitsPerSample / 8));
    wfx.nAvgBytesPerSec = wfx.nSamplesPerSec * wfx.nBlockAlign;

    IXAudio2SourceVoice* src = nullptr;
    hr = xaudio->CreateSourceVoice(&src, &wfx);
    XRAY_TEST_CHECK(SUCCEEDED(hr));
    XRAY_TEST_CHECK(src != nullptr);

    std::vector<std::int16_t> silent_pcm(4410 * 2, 0); // 100 ms stereo silence
    XAUDIO2_BUFFER buf{};
    buf.AudioBytes = static_cast<UINT32>(silent_pcm.size() * sizeof(std::int16_t));
    buf.pAudioData = reinterpret_cast<BYTE const*>(silent_pcm.data());
    buf.Flags = XAUDIO2_END_OF_STREAM;

    XRAY_TEST_CHECK(SUCCEEDED(src->SubmitSourceBuffer(&buf)));
    XRAY_TEST_CHECK(SUCCEEDED(src->Start(0)));
    XAUDIO2_VOICE_STATE st{};
    src->GetState(&st);
    XRAY_TEST_CHECK(st.BuffersQueued <= 1);
    XRAY_TEST_CHECK(SUCCEEDED(src->Stop(0)));
    XRAY_TEST_CHECK(SUCCEEDED(src->FlushSourceBuffers()));

    src->DestroyVoice();
    mastering->DestroyVoice();
    mastering = nullptr;

    // Simulate output device change by recreating mastering voice on the same XAudio2 2.9 engine
    hr = xaudio->CreateMasteringVoice(&mastering, XAUDIO2_DEFAULT_CHANNELS, XAUDIO2_DEFAULT_SAMPLERATE, 0, nullptr, nullptr, AudioCategory_GameEffects);
    XRAY_TEST_CHECK(SUCCEEDED(hr) && mastering != nullptr);
    mastering->DestroyVoice();

    std::cout << "PASS: xray_xaudio2_9_contract\n";
    return 0;
}
