#include "test_check.hpp"

#include <xray/macro_platform.h>
#if XRAY_PLATFORM_WINDOWS
#	include <xray/os_include.h>
#endif

#include <xray/macro_extensions.h>
#include <xray/type_extensions.h>
#include <xray/memory_extensions.h>
#include <xray/threading_mutex.h>
#include <xray/threading_event.h>
#include <xray/threading_functions.h>
#include <xray/os_platform.h>
#include <xray/os_functions.h>

#include <atomic>
#include <chrono>
#include <cmath>
#include <filesystem>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

static_assert(sizeof(void*) == 8, "x64 requires 8-byte pointers");
static_assert(sizeof(xray::threading::atomic32_value_type) == 4, "atomic32_value_type must be 4 bytes on both LLP64 and LP64");
static_assert(sizeof(xray::threading::atomic64_value_type) == 8, "atomic64_value_type must be 8 bytes on both LLP64 and LP64");

#if XRAY_PLATFORM_WINDOWS
static_assert(sizeof(CRITICAL_SECTION) >= 40, "Windows x64 CRITICAL_SECTION must be at least 40 bytes");
static_assert(
    sizeof(xray::threading::mutex_tasks_unaware) >= sizeof(CRITICAL_SECTION),
    "mutex_tasks_unaware backing storage too small for CRITICAL_SECTION"
);
#elif XRAY_PLATFORM_LINUX
static_assert(XRAY_PLATFORM_POSIX == 1, "Linux builds must define XRAY_PLATFORM_POSIX=1");
#endif

int main() {
    // 1. mutex_tasks_unaware same-thread reentrancy & cross-thread mutual exclusion.
    {
        xray::threading::mutex_tasks_unaware mtx;
        mtx.lock();
        mtx.lock();
        mtx.unlock();
        mtx.unlock();

        XRAY_TEST_CHECK(mtx.try_lock());
        std::atomic<bool> worker_acquired_while_held{true};
        std::thread probe([&]() {
            bool acquired = mtx.try_lock();
            worker_acquired_while_held.store(acquired, std::memory_order_relaxed);
            if (acquired) {
                mtx.unlock();
            }
        });
        probe.join();
        XRAY_TEST_CHECK(!worker_acquired_while_held.load(std::memory_order_relaxed));
        mtx.unlock();

        int shared_counter = 0;
        constexpr int num_threads = 4;
        constexpr int iters_per_thread = 10000;
        std::vector<std::thread> workers;
        workers.reserve(num_threads);
        for (int t = 0; t < num_threads; ++t) {
            workers.emplace_back([&]() {
                for (int i = 0; i < iters_per_thread; ++i) {
                    xray::threading::mutex_tasks_unaware::mutex_raii guard(mtx);
                    ++shared_counter;
                }
            });
        }
        for (auto& w : workers) {
            w.join();
        }
        XRAY_TEST_CHECK(shared_counter == num_threads * iters_per_thread);
    }

    // 2. event timeout, signaling across threads, and auto-reset semantics.
    {
        xray::threading::event ev(false);
        XRAY_TEST_CHECK(ev.wait(10) == xray::threading::event::wait_result_still_nonsignaled);

        std::atomic<xray::threading::event::wait_result_enum> wait_res{xray::threading::event::wait_result_failed};
        std::thread waiter([&]() {
            wait_res.store(ev.wait(2000), std::memory_order_release);
        });
        std::this_thread::sleep_for(std::chrono::milliseconds(15));
        ev.set(true);
        waiter.join();
        XRAY_TEST_CHECK(wait_res.load(std::memory_order_acquire) == xray::threading::event::wait_result_signaled);
        // Auto-reset event should be non-signaled after waking one waiter.
        XRAY_TEST_CHECK(ev.wait(0) == xray::threading::event::wait_result_still_nonsignaled);
    }

    // 3. dynamic_library loading and symbol lookup.
    {
#if XRAY_PLATFORM_WINDOWS
        xray::os::dynamic_library lib;
        XRAY_TEST_CHECK(lib.open("kernel32.dll"));
        XRAY_TEST_CHECK(lib.valid());
        using get_pid_fn = unsigned long(__stdcall*)();
        auto get_pid = lib.symbol_as<get_pid_fn>("GetCurrentProcessId");
        XRAY_TEST_CHECK(get_pid != nullptr);
        XRAY_TEST_CHECK(get_pid() == xray::os::current_process_id());
        XRAY_TEST_CHECK(lib.symbol("xray_nonexistent_symbol_pal8") == nullptr);
        lib.close();
        XRAY_TEST_CHECK(!lib.valid());
#elif XRAY_PLATFORM_LINUX
        xray::os::dynamic_library lib;
        XRAY_TEST_CHECK(lib.open("libm.so.6"));
        XRAY_TEST_CHECK(lib.valid());
        using cos_fn = double (*)(double);
        auto cos_ptr = lib.symbol_as<cos_fn>("cos");
        XRAY_TEST_CHECK(cos_ptr != nullptr);
        XRAY_TEST_CHECK(std::fabs(cos_ptr(0.0) - 1.0) < 1e-12);
        XRAY_TEST_CHECK(lib.symbol("xray_nonexistent_symbol_pal8") == nullptr);
        lib.close();
        XRAY_TEST_CHECK(!lib.valid());
#endif
        xray::os::dynamic_library missing;
        XRAY_TEST_CHECK(!missing.open("xray_definitely_missing_shared_library_pal8"));
        XRAY_TEST_CHECK(!missing.valid());
    }

    // 4. executable_path, executable_directory, and environment lookup.
    {
        std::filesystem::path const exe_path = xray::os::executable_path();
        std::filesystem::path const exe_dir = xray::os::executable_directory();
        XRAY_TEST_CHECK(!exe_path.empty());
        XRAY_TEST_CHECK(!exe_dir.empty());
        XRAY_TEST_CHECK(std::filesystem::exists(exe_path));
        XRAY_TEST_CHECK(std::filesystem::is_directory(exe_dir));
        XRAY_TEST_CHECK(exe_path.parent_path() == exe_dir);

        std::string path_env;
        XRAY_TEST_CHECK(xray::os::get_environment_variable("PATH", path_env));
        XRAY_TEST_CHECK(!path_env.empty());
    }

    std::cout << "PASS: xray_platform_x64_contract\n";
    return 0;
}
