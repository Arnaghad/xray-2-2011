#include "test_check.hpp"
#include "vfs_archive.hpp"

#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
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
    } catch (std::runtime_error const&) {
        threw = true;
    }
    if (!threw) {
        std::cerr << "Expected runtime_error for " << label << '\n';
        std::abort();
    }
}

std::uint32_t read_u32(std::vector<std::uint8_t> const& buf, std::size_t off) {
    std::uint32_t v = 0;
    std::memcpy(&v, buf.data() + off, sizeof(v));
    return v;
}

} // namespace

int main() {
    using xray::maya_cook::detail::build_vfs_archive;

    std::map<std::string, std::vector<std::uint8_t>> files;
    files["settings"] = {'s', '=', '1', '\n'};
    files["mesh/vertices"] = {1, 2, 3, 4, 5, 6, 7, 8};
    files["mesh/nested/info.txt"] = {'o', 'k'};

    std::vector<std::uint8_t> archive = build_vfs_archive(files);
    XRAY_TEST_CHECK(archive.size() > 24);
    XRAY_TEST_CHECK(std::memcmp(archive.data(), "little-endian\0\0\0", 16) == 0);

    std::uint32_t node_count = read_u32(archive, 16);
    std::uint32_t fat_size = read_u32(archive, 20);
    // root + settings + mesh + vertices + nested + info.txt = 6 nodes
    XRAY_TEST_CHECK(node_count == 6);
    XRAY_TEST_CHECK(fat_size > 0 && (fat_size % 8) == 0);
    XRAY_TEST_CHECK(archive.size() == 24u + fat_size + 4u + 8u + 2u);

    // Roundtrip through disk write/read helper.
    auto tmp_file = std::filesystem::temp_directory_path() / "xray_vfs_legacy_decode_test.bin";
    xray::maya_cook::detail::write_file_bytes(tmp_file, archive);
    std::ifstream is(tmp_file, std::ios::binary);
    XRAY_TEST_CHECK(is.good());
    std::vector<std::uint8_t> disk_bytes(
        (std::istreambuf_iterator<char>(is)),
        std::istreambuf_iterator<char>()
    );
    is.close();
    std::error_code ec;
    std::filesystem::remove(tmp_file, ec);
    XRAY_TEST_CHECK(disk_bytes == archive);

    // Negative controls on archive builder invariants.
    expect_throw([]() {
        build_vfs_archive({});
    }, "empty archive");
    expect_throw([]() {
        build_vfs_archive({{"/leading_slash", {1}}});
    }, "leading slash");
    expect_throw([]() {
        build_vfs_archive({{"dir/../escape", {1}}});
    }, "parent traversal");
    expect_throw([]() {
        build_vfs_archive({{"mesh", {1}}, {"mesh/sub", {2}}});
    }, "file/directory path conflict");

    std::cout << "PASS: xray_vfs_legacy_decode\n";
    return 0;
}
