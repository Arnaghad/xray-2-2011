#ifndef XRAY_RENDER_RHI_CONTENT_HASH_H_INCLUDED
#define XRAY_RENDER_RHI_CONTENT_HASH_H_INCLUDED

#include <array>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <string>
#include <thread>
#if defined(_WIN32)
#	include <windows.h>
#else
#	include <limits.h>
#	include <sstream>
#	include <unistd.h>
#endif

namespace xray { namespace render { namespace rhi { namespace cache_util {

class sha256_hasher {
    std::array<std::uint32_t, 8> m_state = {
        0x6a09e667u, 0xbb67ae85u, 0x3c6ef372u, 0xa54ff53au,
        0x510e527fu, 0x9b05688cu, 0x1f83d9abu, 0x5be0cd19u
    };
    std::array<unsigned char, 64> m_block = {};
    std::size_t m_buffer_len = 0;
    std::uint64_t m_total_bytes = 0;

    static std::uint32_t rotr(std::uint32_t value, unsigned bits) noexcept {
        return (value >> bits) | (value << (32u - bits));
    }

    void transform(unsigned char const* block) noexcept {
        static constexpr std::array<std::uint32_t, 64> k = {
            0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
            0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u, 0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
            0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
            0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
            0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u, 0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
            0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
            0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
            0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u, 0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u
        };

        std::array<std::uint32_t, 64> w = {};
        for (std::size_t i = 0; i < 16; ++i) {
            std::size_t const j = i * 4;
            w[i] = (std::uint32_t(block[j]) << 24)
                 | (std::uint32_t(block[j + 1]) << 16)
                 | (std::uint32_t(block[j + 2]) << 8)
                 | std::uint32_t(block[j + 3]);
        }
        for (std::size_t i = 16; i < 64; ++i) {
            std::uint32_t const s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >> 3);
            std::uint32_t const s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >> 10);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }

        std::uint32_t a = m_state[0];
        std::uint32_t b = m_state[1];
        std::uint32_t c = m_state[2];
        std::uint32_t d = m_state[3];
        std::uint32_t e = m_state[4];
        std::uint32_t f = m_state[5];
        std::uint32_t g = m_state[6];
        std::uint32_t h = m_state[7];

        for (std::size_t i = 0; i < 64; ++i) {
            std::uint32_t const s1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
            std::uint32_t const ch = (e & f) ^ ((~e) & g);
            std::uint32_t const temp1 = h + s1 + ch + k[i] + w[i];
            std::uint32_t const s0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
            std::uint32_t const maj = (a & b) ^ (a & c) ^ (b & c);
            std::uint32_t const temp2 = s0 + maj;

            h = g;
            g = f;
            f = e;
            e = d + temp1;
            d = c;
            c = b;
            b = a;
            a = temp1 + temp2;
        }

        m_state[0] += a;
        m_state[1] += b;
        m_state[2] += c;
        m_state[3] += d;
        m_state[4] += e;
        m_state[5] += f;
        m_state[6] += g;
        m_state[7] += h;
    }

public:
    void update(void const* data, std::size_t size) noexcept {
        if (!data || !size)
            return;
        auto const* bytes = static_cast<unsigned char const*>(data);
        m_total_bytes += static_cast<std::uint64_t>(size);

        if (m_buffer_len > 0) {
            std::size_t const space = 64 - m_buffer_len;
            if (size < space) {
                std::memcpy(m_block.data() + m_buffer_len, bytes, size);
                m_buffer_len += size;
                return;
            }
            std::memcpy(m_block.data() + m_buffer_len, bytes, space);
            transform(m_block.data());
            bytes += space;
            size -= space;
            m_buffer_len = 0;
        }

        while (size >= 64) {
            transform(bytes);
            bytes += 64;
            size -= 64;
        }

        if (size > 0) {
            std::memcpy(m_block.data(), bytes, size);
            m_buffer_len = size;
        }
    }

    template <typename T>
    void update_pod(T const& value) noexcept {
        update(&value, sizeof(value));
    }

    void update_string(std::string const& text) noexcept {
        std::uint64_t const len = static_cast<std::uint64_t>(text.size());
        update_pod(len);
        if (!text.empty())
            update(text.data(), text.size());
    }

    std::array<unsigned char, 32> finalize() noexcept {
        std::uint64_t const total_bits = m_total_bytes * 8ull;
        unsigned char pad = 0x80u;
        m_block[m_buffer_len++] = pad;
        if (m_buffer_len > 56) {
            if (m_buffer_len < 64)
                std::memset(m_block.data() + m_buffer_len, 0, 64 - m_buffer_len);
            transform(m_block.data());
            m_buffer_len = 0;
        }
        if (m_buffer_len < 56)
            std::memset(m_block.data() + m_buffer_len, 0, 56 - m_buffer_len);

        for (std::size_t i = 0; i < 8; ++i)
            m_block[56 + i] = static_cast<unsigned char>((total_bits >> ((7 - i) * 8)) & 0xffu);
        transform(m_block.data());

        std::array<unsigned char, 32> digest = {};
        for (std::size_t i = 0; i < 8; ++i) {
            digest[i * 4 + 0] = static_cast<unsigned char>((m_state[i] >> 24) & 0xffu);
            digest[i * 4 + 1] = static_cast<unsigned char>((m_state[i] >> 16) & 0xffu);
            digest[i * 4 + 2] = static_cast<unsigned char>((m_state[i] >> 8) & 0xffu);
            digest[i * 4 + 3] = static_cast<unsigned char>(m_state[i] & 0xffu);
        }
        return digest;
    }
};

inline std::string hex_string(std::array<unsigned char, 32> const& digest) {
    static constexpr char hex[] = "0123456789abcdef";
    std::string out(64, '0');
    for (std::size_t i = 0; i < digest.size(); ++i) {
        out[i * 2 + 0] = hex[(digest[i] >> 4) & 0x0fu];
        out[i * 2 + 1] = hex[digest[i] & 0x0fu];
    }
    return out;
}

inline std::filesystem::path resolve_sdk_user_data_dir() {
    if (char const* disable = std::getenv("XRAY_SDK_DISABLE_COOKED_CACHE"); disable && std::strcmp(disable, "1") == 0)
        return {};

#if defined(_WIN32)
    if (wchar_t const* cmd = GetCommandLineW()) {
        std::wstring_view view(cmd);
        std::size_t i = 0;
        std::size_t const n = view.size();
        while (i < n) {
            while (i < n && (view[i] == L' ' || view[i] == L'\t'))
                ++i;
            if (i >= n)
                break;
            std::wstring token;
            bool in_quotes = false;
            while (i < n) {
                wchar_t const ch = view[i];
                if (!in_quotes && (ch == L' ' || ch == L'\t'))
                    break;
                if (ch == L'"')
                    in_quotes = !in_quotes;
                else
                    token.push_back(ch);
                ++i;
            }
            static constexpr wchar_t prefix[] = L"-user_data_path=";
            constexpr std::size_t plen = (sizeof(prefix) / sizeof(wchar_t)) - 1;
            if (token.size() > plen && _wcsnicmp(token.c_str(), prefix, plen) == 0)
                return std::filesystem::path(token.substr(plen)).lexically_normal();
        }
    }

    if (wchar_t const* env = _wgetenv(L"XRAY_SDK_USER_DATA"); env && *env)
        return std::filesystem::path(env).lexically_normal();

    wchar_t executable[32768] = {};
    DWORD const length = GetModuleFileNameW(nullptr, executable, 32768);
    if (length > 0 && length < 32768) {
        std::error_code ec;
        auto const pkg_root = std::filesystem::path(executable).parent_path().parent_path();
        if (std::filesystem::is_regular_file(pkg_root / L"settings" / L"sdk-runtime.json", ec))
            return (pkg_root / L"user-data").lexically_normal();
    }
#else
    if (std::ifstream cmdline("/proc/self/cmdline", std::ios::binary); cmdline) {
        std::string raw((std::istreambuf_iterator<char>(cmdline)), std::istreambuf_iterator<char>());
        std::size_t pos = 0;
        static constexpr char prefix[] = "-user_data_path=";
        constexpr std::size_t plen = sizeof(prefix) - 1;
        while (pos < raw.size()) {
            std::size_t end = raw.find('\0', pos);
            if (end == std::string::npos) end = raw.size();
            std::string_view token(raw.data() + pos, end - pos);
            if (token.size() > plen && strncasecmp(token.data(), prefix, plen) == 0)
                return std::filesystem::path(token.substr(plen)).lexically_normal();
            pos = end + 1;
        }
    }

    if (char const* env = std::getenv("XRAY_SDK_USER_DATA"); env && *env)
        return std::filesystem::path(env).lexically_normal();

    char executable[PATH_MAX] = {};
    ssize_t const length = ::readlink("/proc/self/exe", executable, sizeof(executable) - 1);
    if (length > 0) {
        executable[length] = '\0';
        std::error_code ec;
        auto const pkg_root = std::filesystem::path(executable).parent_path().parent_path();
        if (std::filesystem::is_regular_file(pkg_root / "settings" / "sdk-runtime.json", ec))
            return (pkg_root / "user-data").lexically_normal();
    }
#endif
    return {};
}

inline bool atomic_write_file(std::filesystem::path const& target, void const* data, std::size_t size) {
    if (target.empty() || (!data && size > 0))
        return false;
    std::error_code ec;
    if (target.has_parent_path())
        std::filesystem::create_directories(target.parent_path(), ec);
#if defined(_WIN32)
    auto const tmp = std::filesystem::path(
        target.wstring() + L"." + std::to_wstring(GetCurrentProcessId()) + L"." + std::to_wstring(GetCurrentThreadId()) + L".tmp");
#else
    std::ostringstream tid_ss;
    tid_ss << std::this_thread::get_id();
    auto const tmp = std::filesystem::path(
        target.string() + "." + std::to_string(::getpid()) + "." + tid_ss.str() + ".tmp");
#endif
    {
        std::ofstream out(tmp, std::ios::binary | std::ios::trunc);
        if (!out)
            return false;
        if (size > 0)
            out.write(static_cast<char const*>(data), static_cast<std::streamsize>(size));
        out.flush();
        if (out.fail()) {
            out.close();
            std::filesystem::remove(tmp, ec);
            return false;
        }
    }
    std::filesystem::rename(tmp, target, ec);
    if (ec) {
        std::error_code ec2;
        bool const already_present = std::filesystem::is_regular_file(target, ec2);
        std::filesystem::remove(tmp, ec2);
        return already_present;
    }
    return true;
}

} } } } // namespace xray::render::rhi::cache_util

#endif // #ifndef XRAY_RENDER_RHI_CONTENT_HASH_H_INCLUDED
