#include "test_check.hpp"

#include <ogg/ogg.h>
#include <vorbis/codec.h>
#include <vorbis/vorbisenc.h>
#include <vorbis/vorbisfile.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

static_assert(sizeof(ogg_int16_t) == 2, "ogg_int16_t ABI mismatch");
static_assert(sizeof(ogg_uint16_t) == 2, "ogg_uint16_t ABI mismatch");
static_assert(sizeof(ogg_int32_t) == 4, "ogg_int32_t ABI mismatch");
static_assert(sizeof(ogg_uint32_t) == 4, "ogg_uint32_t ABI mismatch");
static_assert(sizeof(ogg_int64_t) == 8, "ogg_int64_t ABI mismatch");
static_assert(sizeof(ogg_uint64_t) == 8, "ogg_uint64_t ABI mismatch");

namespace {

struct memory_ogg_source {
    std::vector<std::uint8_t> const* data = nullptr;
    std::size_t pos = 0;
};

size_t mem_read(void* ptr, size_t size, size_t nmemb, void* datasource) {
    auto* src = static_cast<memory_ogg_source*>(datasource);
    if (size == 0 || nmemb == 0 || src->pos >= src->data->size()) {
        return 0;
    }
    std::size_t avail_elems = (src->data->size() - src->pos) / size;
    std::size_t count = std::min(nmemb, avail_elems);
    if (count > 0) {
        std::memcpy(ptr, src->data->data() + src->pos, count * size);
        src->pos += count * size;
    }
    return count;
}

int mem_seek(void* datasource, ogg_int64_t offset, int whence) {
    auto* src = static_cast<memory_ogg_source*>(datasource);
    ogg_int64_t target = 0;
    switch (whence) {
    case SEEK_SET:
        target = offset;
        break;
    case SEEK_CUR:
        target = static_cast<ogg_int64_t>(src->pos) + offset;
        break;
    case SEEK_END:
        target = static_cast<ogg_int64_t>(src->data->size()) + offset;
        break;
    default:
        return -1;
    }
    if (target < 0 || static_cast<std::size_t>(target) > src->data->size()) {
        return -1;
    }
    src->pos = static_cast<std::size_t>(target);
    return 0;
}

int mem_close(void*) {
    return 0;
}

long mem_tell(void* datasource) {
    auto* src = static_cast<memory_ogg_source*>(datasource);
    return static_cast<long>(src->pos);
}

std::vector<std::uint8_t> encode_sine_ogg(long rate, int channels, double seconds, float amplitude) {
    vorbis_info vi;
    vorbis_info_init(&vi);
    int rc = vorbis_encode_init_vbr(&vi, channels, rate, 0.4f);
    XRAY_TEST_CHECK(rc == 0);

    vorbis_comment vc;
    vorbis_comment_init(&vc);
    vorbis_comment_add_tag(&vc, "ENCODER", "xray_ogg_vorbis_contract");

    vorbis_dsp_state vd;
    vorbis_block vb;
    XRAY_TEST_CHECK(vorbis_analysis_init(&vd, &vi) == 0);
    XRAY_TEST_CHECK(vorbis_block_init(&vd, &vb) == 0);

    ogg_stream_state os;
    XRAY_TEST_CHECK(ogg_stream_init(&os, 0x12345678) == 0);

    std::vector<std::uint8_t> out;
    auto flush_pages = [&](bool force) {
        ogg_page og;
        while (force ? ogg_stream_flush(&os, &og) : ogg_stream_pageout(&os, &og)) {
            out.insert(out.end(), og.header, og.header + og.header_len);
            out.insert(out.end(), og.body, og.body + og.body_len);
        }
    };

    ogg_packet header{}, header_comm{}, header_code{};
    vorbis_analysis_headerout(&vd, &vc, &header, &header_comm, &header_code);
    ogg_stream_packetin(&os, &header);
    ogg_stream_packetin(&os, &header_comm);
    ogg_stream_packetin(&os, &header_code);
    flush_pages(true);

    long const total_frames = static_cast<long>(rate * seconds);
    long written = 0;
    constexpr int chunk = 1024;
    constexpr double two_pi = 6.28318530717958647692;
    while (written < total_frames) {
        int const n = static_cast<int>(std::min<long>(chunk, total_frames - written));
        float** buf = vorbis_analysis_buffer(&vd, n);
        for (int i = 0; i < n; ++i) {
            double t = static_cast<double>(written + i) / static_cast<double>(rate);
            float sample = amplitude * static_cast<float>(std::sin(two_pi * 440.0 * t));
            for (int ch = 0; ch < channels; ++ch) {
                buf[ch][i] = sample;
            }
        }
        vorbis_analysis_wrote(&vd, n);
        written += n;

        while (vorbis_analysis_blockout(&vd, &vb) == 1) {
            vorbis_analysis(&vb, nullptr);
            vorbis_bitrate_addblock(&vb);
            ogg_packet op;
            while (vorbis_bitrate_flushpacket(&vd, &op)) {
                ogg_stream_packetin(&os, &op);
                flush_pages(false);
            }
        }
    }

    vorbis_analysis_wrote(&vd, 0);
    while (vorbis_analysis_blockout(&vd, &vb) == 1) {
        vorbis_analysis(&vb, nullptr);
        vorbis_bitrate_addblock(&vb);
        ogg_packet op;
        while (vorbis_bitrate_flushpacket(&vd, &op)) {
            ogg_stream_packetin(&os, &op);
            flush_pages(false);
        }
    }
    flush_pages(true);

    ogg_stream_clear(&os);
    vorbis_block_clear(&vb);
    vorbis_dsp_clear(&vd);
    vorbis_comment_clear(&vc);
    vorbis_info_clear(&vi);
    return out;
}

void verify_ogg_stream(std::vector<std::uint8_t> const& ogg_bytes, bool check_sine_rms) {
    memory_ogg_source src{&ogg_bytes, 0};
    OggVorbis_File ovf{};
    ov_callbacks cb{mem_read, mem_seek, mem_close, mem_tell};
    int open_rc = ov_open_callbacks(&src, &ovf, nullptr, 0, cb);
    XRAY_TEST_CHECK(open_rc == 0);

    vorbis_info* info = ov_info(&ovf, -1);
    XRAY_TEST_CHECK(info != nullptr);
    XRAY_TEST_CHECK(info->channels > 0 && info->rate > 0);
    ogg_int64_t pcm_total = ov_pcm_total(&ovf, -1);
    XRAY_TEST_CHECK(pcm_total > 0);

    // 1. Full streaming decode
    std::vector<char> full_pcm;
    full_pcm.reserve(static_cast<std::size_t>(pcm_total) * info->channels * 2);
    int current_section = 0;
    char buf[1024];
    for (;;) {
        long n = ov_read(&ovf, buf, sizeof(buf), 0, 2, 1, &current_section);
        XRAY_TEST_CHECK(n >= 0);
        if (n == 0) {
            break;
        }
        full_pcm.insert(full_pcm.end(), buf, buf + n);
    }
    std::size_t expected_bytes = static_cast<std::size_t>(pcm_total) * static_cast<std::size_t>(info->channels) * 2u;
    XRAY_TEST_CHECK(full_pcm.size() == expected_bytes);

    // 2. Mid-stream seek consistency
    ogg_int64_t mid_pcm = pcm_total / 2;
    XRAY_TEST_CHECK(ov_pcm_seek(&ovf, mid_pcm) == 0);
    std::size_t const probe_bytes = std::min<std::size_t>(4096, full_pcm.size() - static_cast<std::size_t>(mid_pcm) * info->channels * 2u);
    std::vector<char> seek_buf(probe_bytes);
    std::size_t got = 0;
    while (got < probe_bytes) {
        long n = ov_read(&ovf, seek_buf.data() + got, static_cast<int>(probe_bytes - got), 0, 2, 1, &current_section);
        XRAY_TEST_CHECK(n > 0);
        got += static_cast<std::size_t>(n);
    }
    std::size_t mid_byte_off = static_cast<std::size_t>(mid_pcm) * static_cast<std::size_t>(info->channels) * 2u;
    XRAY_TEST_CHECK(std::memcmp(seek_buf.data(), full_pcm.data() + mid_byte_off, probe_bytes) == 0);

    // 3. Loop seek-to-0 parity
    XRAY_TEST_CHECK(ov_pcm_seek(&ovf, 0) == 0);
    std::size_t const loop_probe = std::min<std::size_t>(4096, full_pcm.size());
    std::vector<char> loop_buf(loop_probe);
    got = 0;
    while (got < loop_probe) {
        long n = ov_read(&ovf, loop_buf.data() + got, static_cast<int>(loop_probe - got), 0, 2, 1, &current_section);
        XRAY_TEST_CHECK(n > 0);
        got += static_cast<std::size_t>(n);
    }
    XRAY_TEST_CHECK(std::memcmp(loop_buf.data(), full_pcm.data(), loop_probe) == 0);

    // 4. RMS evaluation matching sound_rms.cpp (ov_read_float on channel 0)
    XRAY_TEST_CHECK(ov_pcm_seek(&ovf, 0) == 0);
    long const win_samples = std::max<long>(1, static_cast<long>(info->rate * 0.05f));
    double accum = 0.0;
    long count = 0;
    std::vector<float> windows;
    float** pcm = nullptr;
    for (;;) {
        long n = ov_read_float(&ovf, &pcm, 4096, &current_section);
        XRAY_TEST_CHECK(n >= 0);
        if (n == 0) {
            if (count > 0) {
                windows.push_back(static_cast<float>(std::sqrt(accum / count)));
            }
            break;
        }
        for (long i = 0; i < n; ++i) {
            float s = pcm[0][i];
            accum += static_cast<double>(s) * static_cast<double>(s);
            ++count;
            if (count == win_samples) {
                windows.push_back(static_cast<float>(std::sqrt(accum / count)));
                accum = 0.0;
                count = 0;
            }
        }
    }
    XRAY_TEST_CHECK(!windows.empty());
    for (std::size_t i = 0; i < windows.size(); ++i) {
        XRAY_TEST_CHECK(std::isfinite(windows[i]));
        if (check_sine_rms && i + 1 < windows.size()) {
            XRAY_TEST_CHECK(windows[i] >= 0.25f && windows[i] <= 0.45f);
        }
    }

    ov_clear(&ovf);
}

} // namespace

int main(int argc, char** argv) {
    char const* ver = vorbis_version_string();
    XRAY_TEST_CHECK(ver != nullptr && std::strlen(ver) > 0);

    std::string legacy_path;
    for (int i = 1; i < argc; ++i) {
        if (std::string(argv[i]) == "--legacy-ogg" && i + 1 < argc) {
            legacy_path = argv[++i];
        }
    }

    if (legacy_path.empty()) {
        auto encoded = encode_sine_ogg(44100, 2, 0.5, 0.5f);
        XRAY_TEST_CHECK(!encoded.empty());
        verify_ogg_stream(encoded, true);
        std::cout << "PASS: xray_ogg_vorbis_contract (" << ver << ")\n";
    } else {
        std::ifstream is(legacy_path, std::ios::binary);
        XRAY_TEST_CHECK(is.good());
        std::vector<std::uint8_t> bytes(
            (std::istreambuf_iterator<char>(is)),
            std::istreambuf_iterator<char>()
        );
        XRAY_TEST_CHECK(!bytes.empty());
        verify_ogg_stream(bytes, false);
        std::cout << "PASS: xray_ogg_vorbis_legacy_corpus (" << legacy_path << ")\n";
    }

    return 0;
}
