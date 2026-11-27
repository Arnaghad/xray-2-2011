#include "test_check.hpp"

#if defined(_WIN32)
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
#include <xray/os_raii.h>
#include <xray/memory_base_allocator.h>

#include <atomic>
#include <cstdio>
#include <cstring>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

namespace {

struct mock_allocator final : xray::memory::base_allocator {
    mock_allocator()
#ifndef MASTER_GOLD
        : xray::memory::base_allocator(false)
#endif
    {}

    size_t total_size() const override { return 0; }
    size_t allocated_size() const override { return 0; }

protected:
    pvoid call_malloc(size_t XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_DECLARATION XRAY_CORE_DEBUG_PARAMETERS_DECLARATION) override {
        return nullptr;
    }
    pvoid call_realloc(pvoid, size_t XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_DECLARATION XRAY_CORE_DEBUG_PARAMETERS_DECLARATION) override {
        return nullptr;
    }
    void call_free(pvoid XRAY_CORE_DEBUG_PARAMETERS_DECLARATION) override {}
    size_t usable_size_impl(pvoid) const override { return 0; }

private:
    void initialize_impl(pvoid, u64, pcstr) override {}
    void finalize_impl() override {}
};

xray::memory::base_allocator const* g_expected_clipboard_allocator = nullptr;
std::string g_mock_clipboard_text;
char g_mock_clipboard_paste_buf[128] = {};

bool mock_clipboard_is_empty() {
    return g_mock_clipboard_text.empty();
}

void mock_clipboard_copy(pcstr buffer) {
    g_mock_clipboard_text = buffer ? buffer : "";
}

pstr mock_clipboard_paste(xray::memory::base_allocator& allocator) {
    if (&allocator != g_expected_clipboard_allocator) {
        return nullptr;
    }
    std::snprintf(g_mock_clipboard_paste_buf, sizeof(g_mock_clipboard_paste_buf), "%s", g_mock_clipboard_text.c_str());
    return g_mock_clipboard_paste_buf;
}

} // namespace

static_assert(sizeof(void*) == 8, "Windows x64 requires 8-byte pointers");
#if XRAY_PLATFORM_WINDOWS
static_assert(sizeof(CRITICAL_SECTION) == 40, "Windows x64 CRITICAL_SECTION must be 40 bytes");
static_assert(
    sizeof(xray::threading::mutex_tasks_unaware) >= sizeof(CRITICAL_SECTION),
    "mutex_tasks_unaware backing storage too small for CRITICAL_SECTION"
);
static_assert(
    sizeof(xray::threading::mutex) >= sizeof(CRITICAL_SECTION),
    "mutex backing storage too small for CRITICAL_SECTION"
);
#endif

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

    // Verify mutex_tasks_unaware same-thread reentrancy.
    XRAY_TEST_CHECK(mtx.try_lock());
    XRAY_TEST_CHECK(mtx.try_lock());
    mtx.unlock();
    mtx.unlock();

    // Verify all xray::threading::interlocked_* operations and memory_barrier().
    {
        xray::threading::atomic32_type a32 = 10;
        XRAY_TEST_CHECK(xray::threading::interlocked_increment(a32) == 11);
        XRAY_TEST_CHECK(a32 == 11);
        XRAY_TEST_CHECK(xray::threading::interlocked_decrement(a32) == 10);
        XRAY_TEST_CHECK(a32 == 10);
        XRAY_TEST_CHECK(xray::threading::interlocked_exchange_add(a32, 5) == 10);
        XRAY_TEST_CHECK(a32 == 15);
        XRAY_TEST_CHECK(xray::threading::interlocked_exchange_sub(a32, 3) == 15);
        XRAY_TEST_CHECK(a32 == 12);
        XRAY_TEST_CHECK(xray::threading::interlocked_and(a32, 0xA) == 12);
        XRAY_TEST_CHECK(a32 == 8);
        XRAY_TEST_CHECK(xray::threading::interlocked_or(a32, 0x3) == 8);
        XRAY_TEST_CHECK(a32 == 11);
        XRAY_TEST_CHECK(xray::threading::interlocked_exchange(a32, 42) == 11);
        XRAY_TEST_CHECK(a32 == 42);
        XRAY_TEST_CHECK(xray::threading::interlocked_compare_exchange(a32, 99, 77) == 42);
        XRAY_TEST_CHECK(a32 == 42);
        XRAY_TEST_CHECK(xray::threading::interlocked_compare_exchange(a32, 99, 42) == 42);
        XRAY_TEST_CHECK(a32 == 99);

        xray::threading::atomic64_type a64 = 10000000000LL;
        XRAY_TEST_CHECK(xray::threading::interlocked_exchange(a64, 20000000000LL) == 10000000000LL);
        XRAY_TEST_CHECK(a64 == 20000000000LL);
        XRAY_TEST_CHECK(xray::threading::interlocked_compare_exchange(a64, 30000000000LL, 12345LL) == 20000000000LL);
        XRAY_TEST_CHECK(a64 == 20000000000LL);
        XRAY_TEST_CHECK(xray::threading::interlocked_compare_exchange(a64, 30000000000LL, 20000000000LL) == 20000000000LL);
        XRAY_TEST_CHECK(a64 == 30000000000LL);

        int first_obj = 1;
        int second_obj = 2;
        xray::threading::atomic_ptr_type aptr = &first_obj;
        XRAY_TEST_CHECK(xray::threading::interlocked_exchange_pointer(aptr, &second_obj) == &first_obj);
        XRAY_TEST_CHECK(aptr == &second_obj);
        XRAY_TEST_CHECK(xray::threading::interlocked_compare_exchange_pointer(aptr, &first_obj, nullptr) == &second_obj);
        XRAY_TEST_CHECK(aptr == &second_obj);
        XRAY_TEST_CHECK(xray::threading::interlocked_compare_exchange_pointer(aptr, &first_obj, &second_obj) == &second_obj);
        XRAY_TEST_CHECK(aptr == &first_obj);

        int* volatile typed_ptr = &first_obj;
        XRAY_TEST_CHECK(xray::threading::interlocked_exchange_pointer(typed_ptr, &second_obj) == &first_obj);
        XRAY_TEST_CHECK(typed_ptr == &second_obj);

        xray::threading::memory_barrier();
    }

    // Verify event_tasks_unaware auto-reset set/wait(0) semantics.
    {
        xray::threading::event_tasks_unaware ev(false);
        XRAY_TEST_CHECK(ev.wait(0) == xray::threading::event_tasks_unaware::wait_result_still_nonsignaled);
        ev.set(true);
        XRAY_TEST_CHECK(ev.wait(0) == xray::threading::event_tasks_unaware::wait_result_signaled);
        XRAY_TEST_CHECK(ev.wait(0) == xray::threading::event_tasks_unaware::wait_result_still_nonsignaled);
        ev.set(true);
        ev.set(false);
        XRAY_TEST_CHECK(ev.wait(0) == xray::threading::event_tasks_unaware::wait_result_still_nonsignaled);
    }

    // Verify PAL-1 xray::os primitives.
    {
        auto const exe_path = xray::os::executable_path();
        auto const exe_dir = xray::os::executable_directory();
        XRAY_TEST_CHECK(!exe_path.empty());
        XRAY_TEST_CHECK(!exe_dir.empty());
        XRAY_TEST_CHECK(exe_path.parent_path() == exe_dir);
        XRAY_TEST_CHECK(xray::os::current_process_id() != 0);

        auto const mem_info = xray::os::query_system_memory();
        XRAY_TEST_CHECK(mem_info.total_physical > 0);
        XRAY_TEST_CHECK(mem_info.page_size > 0);

        xray::os::scoped_com_init com_scope(true);

#if XRAY_PLATFORM_WINDOWS
        xray::os::dynamic_library kernel32;
        XRAY_TEST_CHECK(kernel32.open("kernel32.dll"));
        XRAY_TEST_CHECK(kernel32.valid());
        using get_pid_fn = unsigned long(__stdcall*)();
        auto get_pid = kernel32.symbol_as<get_pid_fn>("GetCurrentProcessId");
        XRAY_TEST_CHECK(get_pid != nullptr);
        XRAY_TEST_CHECK(get_pid() == xray::os::current_process_id());
        kernel32.close();
        XRAY_TEST_CHECK(!kernel32.valid());
#endif

        // Verify mock clipboard_provider dispatch and clean restoration.
        mock_allocator mock_alloc;
        g_expected_clipboard_allocator = &mock_alloc;
        g_mock_clipboard_text.clear();

        xray::os::clipboard_provider mock_provider{};
        mock_provider.is_empty = &mock_clipboard_is_empty;
        mock_provider.copy = &mock_clipboard_copy;
        mock_provider.paste = &mock_clipboard_paste;

        auto const* prev_provider = xray::os::get_clipboard_provider();
        xray::os::set_clipboard_provider(&mock_provider);
        auto restore_clipboard = xray::os::make_scoped_cleanup([prev_provider]() {
            xray::os::set_clipboard_provider(prev_provider);
            g_expected_clipboard_allocator = nullptr;
            g_mock_clipboard_text.clear();
        });

        XRAY_TEST_CHECK(xray::os::get_clipboard_provider() == &mock_provider);
        XRAY_TEST_CHECK(xray::os::is_clipboard_empty());
        xray::os::copy_to_clipboard("pal1-clipboard-check");
        XRAY_TEST_CHECK(!xray::os::is_clipboard_empty());
        pstr pasted = xray::os::paste_from_clipboard(mock_alloc);
        XRAY_TEST_CHECK(pasted != nullptr);
        XRAY_TEST_CHECK(std::strcmp(pasted, "pal1-clipboard-check") == 0);
    }

    std::cout << "PASS: xray_windows_x64_contract\n";
    return 0;
}
