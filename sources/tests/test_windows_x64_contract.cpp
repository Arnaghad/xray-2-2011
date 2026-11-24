#include "test_check.hpp"

#include <xray/os_include.h>
#include <xray/macro_extensions.h>
#include <xray/type_extensions.h>
#include <xray/threading_mutex.h>

#include <atomic>
#include <iostream>
#include <thread>
#include <vector>

static_assert(sizeof(void*) == 8, "Windows x64 requires 8-byte pointers");
static_assert(sizeof(CRITICAL_SECTION) == 40, "Windows x64 CRITICAL_SECTION must be 40 bytes");
static_assert(
    sizeof(xray::threading::mutex_tasks_unaware) >= sizeof(CRITICAL_SECTION),
    "mutex_tasks_unaware backing storage too small for CRITICAL_SECTION"
);
static_assert(
    sizeof(xray::threading::mutex) >= sizeof(CRITICAL_SECTION),
    "mutex backing storage too small for CRITICAL_SECTION"
);

int main() {
    // Verify contended multi-threaded mutual exclusion via mutex_tasks_unaware.
    xray::threading::mutex_tasks_unaware mtx;
    int shared_counter = 0;
    constexpr int num_threads = 4;
    constexpr int iters_per_thread = 25000;

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

    // Verify try_lock contention semantics across threads.
    {
        xray::threading::mutex_tasks_unaware::mutex_raii guard(mtx);
        std::atomic<bool> try_locked_while_held{true};
        std::thread probe([&]() {
            bool acquired = mtx.try_lock();
            try_locked_while_held.store(acquired, std::memory_order_relaxed);
            if (acquired) {
                mtx.unlock();
            }
        });
        probe.join();
        XRAY_TEST_CHECK(!try_locked_while_held.load(std::memory_order_relaxed));
    }

    XRAY_TEST_CHECK(mtx.try_lock());
    mtx.unlock();

    // Verify uncontended tasks-aware mutex try_lock/unlock without tasks runtime.
    xray::threading::mutex tasks_mtx;
    XRAY_TEST_CHECK(tasks_mtx.try_lock());
    tasks_mtx.unlock();

    std::cout << "PASS: xray_windows_x64_contract\n";
    return 0;
}
