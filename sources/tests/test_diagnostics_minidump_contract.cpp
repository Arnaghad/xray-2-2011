#include "test_check.hpp"

#include <xray/os_include.h>
#include <xray/debug/debug.h>

#include <array>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>

int main() {
    namespace fs = std::filesystem;

    // Verify BugTrap DLLs are never loaded by diagnostics initialization.
    xray::debug::change_bugtrap_usage(xray::debug::error_mode_silent, xray::debug::no_bugtrap);
    XRAY_TEST_CHECK(GetModuleHandleA("BugTrap-x64.dll") == nullptr);
    XRAY_TEST_CHECK(GetModuleHandleA("BugTrapN-x64.dll") == nullptr);

    fs::path const temp_dir = fs::temp_directory_path() / "xray_diag_test";
    fs::create_directories(temp_dir);
    fs::path const dump_path = temp_dir / "stage04_smoke.dmp";
    std::error_code ec;
    fs::remove(dump_path, ec);

    std::string const dump_str = dump_path.string();
    bool const wrote = xray::debug::write_local_minidump(dump_str.c_str(), nullptr);
    XRAY_TEST_CHECK(wrote);
    XRAY_TEST_CHECK(fs::exists(dump_path));

    auto const dump_size = fs::file_size(dump_path);
    XRAY_TEST_CHECK(dump_size >= 1024);

    std::array<char, 4> magic{};
    {
        std::ifstream f(dump_path, std::ios::binary);
        XRAY_TEST_CHECK(static_cast<bool>(f));
        f.read(magic.data(), magic.size());
        XRAY_TEST_CHECK(f.gcount() == 4);
    }
    XRAY_TEST_CHECK(magic[0] == 'M' && magic[1] == 'D' && magic[2] == 'M' && magic[3] == 'P');

    // Empty path negative control
    XRAY_TEST_CHECK(!xray::debug::write_local_minidump("", nullptr));
    XRAY_TEST_CHECK(!xray::debug::write_local_minidump(nullptr, nullptr));

    fs::remove(dump_path, ec);
    fs::remove(temp_dir, ec);
    xray::debug::finalize();

    std::cout << "PASS: xray_diagnostics_minidump_contract (dmp_bytes=" << dump_size << ")\n";
    return 0;
}
