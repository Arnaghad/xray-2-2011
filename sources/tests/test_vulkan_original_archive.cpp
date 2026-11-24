#define XRAY_LOG_MODULE_INITIATOR "test_vulkan_original_archive"
#include "test_check.hpp"
#include <xray/extensions.h>
#include "vfs_archive.hpp"
#include "vulkan_original_static_assets.h"

#include <cstdint>
#include <cstring>
#include <iostream>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

template <typename Fn>
void expect_throw(Fn&& fn, char const* label) {
    bool threw = false;
    try {
        fn();
    } catch (std::exception const&) {
        threw = true;
    }
    if (!threw) {
        std::cerr << "Expected exception for " << label << '\n';
        std::abort();
    }
}

void append_u32(std::vector<std::uint8_t>& out, std::uint32_t v) {
    auto const* p = reinterpret_cast<std::uint8_t const*>(&v);
    out.insert(out.end(), p, p + sizeof(v));
}

void append_u16(std::vector<std::uint8_t>& out, std::uint16_t v) {
    auto const* p = reinterpret_cast<std::uint8_t const*>(&v);
    out.insert(out.end(), p, p + sizeof(v));
}

void append_f32(std::vector<std::uint8_t>& out, float v) {
    auto const* p = reinterpret_cast<std::uint8_t const*>(&v);
    out.insert(out.end(), p, p + sizeof(v));
}

} // namespace

int main() {
    using xray::const_buffer;
    using xray::maya_cook::detail::build_vfs_archive;
    using xray::render::vulkan::original::convert_static_surface;
    using xray::render::vulkan::original::read_export_archive;

    std::map<std::string, std::vector<std::uint8_t>> files;
    files["settings"] = {'l', 'o', 'd', '=', '0'};
    files["surf/mesh"] = {10, 20, 30, 40};

    std::vector<std::uint8_t> archive = build_vfs_archive(files);
    auto decoded = read_export_archive(const_buffer(archive.data(), archive.size()));
    XRAY_TEST_CHECK(decoded.size() == files.size());
    XRAY_TEST_CHECK(decoded["settings"] == files["settings"]);
    XRAY_TEST_CHECK(decoded["surf/mesh"] == files["surf/mesh"]);

    // Negative controls on original archive reader
    std::vector<std::uint8_t> truncated = {'l', 'i', 't', 't', 'l', 'e'};
    expect_throw([&]() { read_export_archive(const_buffer(truncated.data(), truncated.size())); }, "truncated");

    std::vector<std::uint8_t> bad_magic = archive;
    bad_magic[0] = 'X';
    expect_throw([&]() { read_export_archive(const_buffer(bad_magic.data(), bad_magic.size())); }, "bad magic");

    std::vector<std::uint8_t> bad_fat = archive;
    std::uint32_t huge_fat = 0x7FFFFFFFu;
    std::memcpy(bad_fat.data() + 20, &huge_fat, sizeof(huge_fat));
    expect_throw([&]() { read_export_archive(const_buffer(bad_fat.data(), bad_fat.size())); }, "corrupt FAT");

    // Static surface conversion test (3 valid 56-byte vertices + 3 indices)
    std::vector<std::uint8_t> verts;
    append_u32(verts, 3u);
    float const positions[3][3] = {
        {0.0f, 0.0f, 0.0f},
        {1.0f, 0.0f, 0.0f},
        {0.0f, 1.0f, 0.0f},
    };
    for (int i = 0; i < 3; ++i) {
        append_f32(verts, positions[i][0]);
        append_f32(verts, positions[i][1]);
        append_f32(verts, positions[i][2]);
        // normal, tangent, binormal (each 3 floats)
        append_f32(verts, 0.0f); append_f32(verts, 0.0f); append_f32(verts, 1.0f);
        append_f32(verts, 1.0f); append_f32(verts, 0.0f); append_f32(verts, 0.0f);
        append_f32(verts, 0.0f); append_f32(verts, 1.0f); append_f32(verts, 0.0f);
        // uv
        append_f32(verts, i == 1 ? 1.0f : 0.0f);
        append_f32(verts, i == 2 ? 1.0f : 0.0f);
    }

    std::vector<std::uint8_t> indices;
    append_u32(indices, 3u);
    append_u16(indices, 0u);
    append_u16(indices, 1u);
    append_u16(indices, 2u);

    auto converted = convert_static_surface(
        const_buffer(verts.data(), verts.size()),
        const_buffer(indices.data(), indices.size())
    );
    XRAY_TEST_CHECK(!converted.empty());

    // Degenerate zero-normal vertex must fail
    std::vector<std::uint8_t> bad_verts = verts;
    float zero = 0.0f;
    std::memcpy(bad_verts.data() + 4 + 12 + 8, &zero, sizeof(zero));
    expect_throw([&]() {
        convert_static_surface(
            const_buffer(bad_verts.data(), bad_verts.size()),
            const_buffer(indices.data(), indices.size())
        );
    }, "degenerate basis");

    std::cout << "PASS: xray_vulkan_original_archive\n";
    return 0;
}
