#include "test_check.hpp"

#include <xray/type_extensions.h>
#include <xray/debug/call_stack.h>
#include <xray/debug/debug.h>

#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

namespace {

struct protected_call_payload {
    int input = 0;
    int output = 0;
};

void protected_worker(pvoid const raw) {
    auto* payload = static_cast<protected_call_payload*>(raw);
    payload->output = payload->input * 3 + 7;
}

} // namespace

int main() {
    namespace fs = std::filesystem;

    // 1. Initialize / configure diagnostics without BugTrap.
    xray::debug::change_bugtrap_usage(xray::debug::error_mode_silent, xray::debug::no_bugtrap);
    (void)xray::debug::is_debugger_present();

    // 2. Capture raw stack trace and verify null-termination & non-zero hash.
    void* raw_frames[32] = {};
    u32 trace_hash = 0;
    xray::debug::call_stack::get_stack_trace(raw_frames, 32, 16, &trace_hash);
    XRAY_TEST_CHECK(raw_frames[0] != nullptr);
    u32 raw_count = 0;
    while (raw_count < 32 && raw_frames[raw_count] != nullptr) {
        ++raw_count;
    }
    XRAY_TEST_CHECK(raw_count >= 1);
    XRAY_TEST_CHECK(raw_count <= 16);
    XRAY_TEST_CHECK(trace_hash != 0);

    // 3. Iterate live call stack (forward, inverted, early stop, and explicit frame array).
    u32 live_count = 0;
    xray::debug::call_stack::iterate(
        nullptr,
        nullptr,
        [&live_count](u32, u32, pcstr, pcstr, int, pcstr, size_t addr) {
            XRAY_TEST_CHECK(addr != 0);
            ++live_count;
            return true;
        },
        false
    );
    XRAY_TEST_CHECK(live_count >= 1);

    std::vector<u32> inverted_ids;
    xray::debug::call_stack::iterate(
        nullptr,
        nullptr,
        [&inverted_ids](u32 const id, u32, pcstr, pcstr, int, pcstr, size_t) {
            inverted_ids.push_back(id);
            return inverted_ids.size() < 2;
        },
        true
    );
    XRAY_TEST_CHECK(!inverted_ids.empty());
    XRAY_TEST_CHECK(inverted_ids.size() <= 2);

    u32 explicit_count = 0;
    xray::debug::call_stack::iterate(
        nullptr,
        raw_frames,
        [&explicit_count](u32, u32 const total, pcstr mod, pcstr, int, pcstr fn, size_t addr) {
            XRAY_TEST_CHECK(total >= 1);
            XRAY_TEST_CHECK(mod != nullptr);
            XRAY_TEST_CHECK(fn != nullptr);
            XRAY_TEST_CHECK(addr != 0);
            ++explicit_count;
            return true;
        },
        false
    );
    XRAY_TEST_CHECK(explicit_count == raw_count);

    // 4. Verify protected_call executes payload cleanly.
    protected_call_payload payload{14, 0};
    xray::debug::protected_call(&protected_worker, &payload);
    XRAY_TEST_CHECK(payload.output == 49);

    // 5. Verify format_system_error handles valid and invalid buffers.
    char err_buf[256] = {};
    XRAY_TEST_CHECK(xray::debug::format_system_error(2, err_buf, sizeof(err_buf)));
    XRAY_TEST_CHECK(std::strlen(err_buf) > 0);
    XRAY_TEST_CHECK(!xray::debug::format_system_error(2, nullptr, 16));
    XRAY_TEST_CHECK(!xray::debug::format_system_error(2, err_buf, 0));

    // 6. Verify scoped_fault_tracer RAII lifecycle (active and inactive).
    {
        xray::debug::scoped_fault_tracer inactive_tracer("pal3_inactive", false);
        XRAY_TEST_CHECK(!inactive_tracer.active());

        xray::debug::scoped_fault_tracer active_tracer("pal3_active", true);
        XRAY_TEST_CHECK(active_tracer.active());
    }

    // 7. Verify cross-platform write_crash_report positive and negative cases.
    fs::path const temp_dir = fs::temp_directory_path() / "xray_pal3_diag_test";
    std::error_code ec;
    fs::remove_all(temp_dir, ec);
    XRAY_TEST_CHECK(fs::create_directories(temp_dir, ec));

    fs::path const report_path = temp_dir / "pal3_report.txt";
    std::string const report_str = report_path.string();
    XRAY_TEST_CHECK(xray::debug::write_crash_report(report_str.c_str(), nullptr));
    XRAY_TEST_CHECK(fs::exists(report_path));
    XRAY_TEST_CHECK(fs::file_size(report_path) > 64);

    {
        std::ifstream f(report_path, std::ios::binary);
        XRAY_TEST_CHECK(static_cast<bool>(f));
        std::string content((std::istreambuf_iterator<char>(f)), std::istreambuf_iterator<char>());
#if XRAY_PLATFORM_WINDOWS
        XRAY_TEST_CHECK(content.size() >= 4 && content.compare(0, 4, "MDMP") == 0);
#else
        XRAY_TEST_CHECK(content.find("XRAY_CRASH_REPORT") != std::string::npos);
        XRAY_TEST_CHECK(content.find("[CALL_STACK]") != std::string::npos);
        XRAY_TEST_CHECK(content.find("[MEMORY_MAPS]") != std::string::npos);
#endif
    }

    XRAY_TEST_CHECK(!xray::debug::write_crash_report("", nullptr));
    XRAY_TEST_CHECK(!xray::debug::write_crash_report(nullptr, nullptr));

    fs::remove_all(temp_dir, ec);
    xray::debug::call_stack::finalize_symbols();
    xray::debug::finalize();

    std::cout << "PASS: xray_diagnostics_pal3_contract (live_frames=" << live_count << ")\n";
    return 0;
}
