#include "test_check.hpp"

#include <xray/os_raii.h>
#include <xray/os_raii_win.h>
#include <fastdelegate/FastDelegate.h>

#include <algorithm>
#include <cstdint>
#include <iostream>
#include <utility>
#include <vector>

namespace {

struct mock_com_object {
    int ref_count = 1;
    bool* destroyed_flag = nullptr;

    unsigned long AddRef() {
        ++ref_count;
        return static_cast<unsigned long>(ref_count);
    }

    unsigned long Release() {
        --ref_count;
        int remaining = ref_count;
        if (remaining == 0) {
            if (destroyed_flag) {
                *destroyed_flag = true;
            }
            delete this;
        }
        return static_cast<unsigned long>(remaining);
    }
};

class listener {
public:
    explicit listener(int id, std::vector<int>& log)
        : m_id(id)
        , m_log(log) {}

    void on_event(int value) {
        if (!m_alive) {
            m_log.push_back(-9999);
            return;
        }
        m_log.push_back(m_id * 100 + value);
    }

    void on_event_const(int value) const {
        m_log.push_back(-(m_id * 100 + value));
    }

    ~listener() {
        m_alive = false;
    }

private:
    int m_id;
    bool m_alive = true;
    std::vector<int>& m_log;
};

class subscription_bus {
public:
    using callback_t = fastdelegate::FastDelegate1<int>;

    struct entry {
        std::uint64_t token = 0;
        callback_t cb;
        bool removed = false;
    };

    class subscription {
    public:
        subscription() = default;
        subscription(subscription_bus* bus, std::uint64_t token)
            : m_bus(bus)
            , m_token(token) {}

        ~subscription() {
            reset();
        }

        subscription(subscription const&) = delete;
        subscription& operator=(subscription const&) = delete;

        subscription(subscription&& other) noexcept
            : m_bus(std::exchange(other.m_bus, nullptr))
            , m_token(std::exchange(other.m_token, 0)) {}

        subscription& operator=(subscription&& other) noexcept {
            if (this != &other) {
                reset();
                m_bus = std::exchange(other.m_bus, nullptr);
                m_token = std::exchange(other.m_token, 0);
            }
            return *this;
        }

        void reset() {
            if (m_bus && m_token != 0) {
                m_bus->unsubscribe(m_token);
            }
            m_bus = nullptr;
            m_token = 0;
        }

    private:
        subscription_bus* m_bus = nullptr;
        std::uint64_t m_token = 0;
    };

    [[nodiscard]] subscription subscribe(callback_t cb) {
        std::uint64_t const tok = ++m_next_token;
        m_entries.push_back({tok, cb, false});
        return subscription(this, tok);
    }

    void unsubscribe(std::uint64_t token) {
        for (auto& e : m_entries) {
            if (e.token == token) {
                e.removed = true;
                e.cb.clear();
            }
        }
        if (m_dispatch_depth == 0) {
            compact();
        }
    }

    void dispatch(int value) {
        ++m_dispatch_depth;
        std::size_t const snapshot_count = m_entries.size();
        for (std::size_t i = 0; i < snapshot_count; ++i) {
            if (!m_entries[i].removed && !m_entries[i].cb.empty()) {
                m_entries[i].cb(value);
            }
        }
        --m_dispatch_depth;
        if (m_dispatch_depth == 0) {
            compact();
        }
    }

    std::size_t active_count() const {
        return static_cast<std::size_t>(std::count_if(
            m_entries.begin(),
            m_entries.end(),
            [](entry const& e) { return !e.removed; }
        ));
    }

private:
    void compact() {
        m_entries.erase(
            std::remove_if(
                m_entries.begin(),
                m_entries.end(),
                [](entry const& e) { return e.removed; }
            ),
            m_entries.end()
        );
    }

    std::uint64_t m_next_token = 0;
    int m_dispatch_depth = 0;
    std::vector<entry> m_entries;
};

int static_callback_sum = 0;
void free_callback(int x) {
    static_callback_sum += x;
}

#if XRAY_PLATFORM_WINDOWS
void test_unique_handle() {
    xray::os::unique_handle empty_handle;
    XRAY_TEST_CHECK(!empty_handle.valid());
    XRAY_TEST_CHECK(!empty_handle);

    xray::os::unique_handle invalid_handle(INVALID_HANDLE_VALUE);
    XRAY_TEST_CHECK(!invalid_handle.valid());

    HANDLE raw = nullptr;
    {
        xray::os::unique_handle ev(CreateEventW(nullptr, TRUE, FALSE, nullptr));
        XRAY_TEST_CHECK(ev.valid());
        raw = ev.get();

        DWORD flags = 0;
        XRAY_TEST_CHECK(GetHandleInformation(raw, &flags) != 0);

        xray::os::unique_handle moved_ctor(std::move(ev));
        XRAY_TEST_CHECK(!ev.valid());
        XRAY_TEST_CHECK(ev.get() == nullptr);
        XRAY_TEST_CHECK(moved_ctor.get() == raw);
        XRAY_TEST_CHECK(GetHandleInformation(raw, &flags) != 0);

        xray::os::unique_handle moved_assign;
        moved_assign = std::move(moved_ctor);
        XRAY_TEST_CHECK(!moved_ctor.valid());
        XRAY_TEST_CHECK(moved_assign.get() == raw);
        XRAY_TEST_CHECK(GetHandleInformation(raw, &flags) != 0);
    }

    DWORD flags_after = 0;
    XRAY_TEST_CHECK(GetHandleInformation(raw, &flags_after) == 0);

    // Verify release() transfers ownership cleanly
    HANDLE released_raw = nullptr;
    {
        xray::os::unique_handle ev2(CreateEventW(nullptr, TRUE, FALSE, nullptr));
        XRAY_TEST_CHECK(ev2.valid());
        released_raw = ev2.release();
        XRAY_TEST_CHECK(!ev2.valid());
    }
    DWORD flags_released = 0;
    XRAY_TEST_CHECK(GetHandleInformation(released_raw, &flags_released) != 0);
    XRAY_TEST_CHECK(CloseHandle(released_raw) != 0);
}

void test_com_ptr() {
    bool destroyed = false;
    {
        auto* raw = new mock_com_object{1, &destroyed};
        xray::os::com_ptr<mock_com_object> p1(raw);
        XRAY_TEST_CHECK(p1 && p1->ref_count == 1);

        {
            xray::os::com_ptr<mock_com_object> p2(p1);
            XRAY_TEST_CHECK(p1->ref_count == 2);

            xray::os::com_ptr<mock_com_object> p3;
            p3 = p2;
            XRAY_TEST_CHECK(p1->ref_count == 3);

            xray::os::com_ptr<mock_com_object> p4(std::move(p3));
            XRAY_TEST_CHECK(!p3);
            XRAY_TEST_CHECK(p4.get() == raw);
            XRAY_TEST_CHECK(p1->ref_count == 3);

            // Self-assignment safety
            p2 = p2;
            XRAY_TEST_CHECK(p1->ref_count == 3);
        }

        XRAY_TEST_CHECK(!destroyed);
        XRAY_TEST_CHECK(p1->ref_count == 1);
    }
    XRAY_TEST_CHECK(destroyed);
}
#endif // #if XRAY_PLATFORM_WINDOWS

void test_scoped_cleanup() {
    bool cleaned = false;
    {
        auto guard = xray::os::make_scoped_cleanup([&cleaned]() { cleaned = true; });
        XRAY_TEST_CHECK(!cleaned);
    }
    XRAY_TEST_CHECK(cleaned);
}

void test_fastdelegate_lifecycle_and_order() {
    std::vector<int> log;
    listener l1(1, log);
    listener l2(2, log);
    listener l3(3, log);

    fastdelegate::FastDelegate1<int> dg_free(&free_callback);
    static_callback_sum = 0;
    dg_free(7);
    XRAY_TEST_CHECK(static_callback_sum == 7);
    dg_free.clear();
    XRAY_TEST_CHECK(dg_free.empty());

    auto dg1 = fastdelegate::MakeDelegate(&l1, &listener::on_event);
    auto dg1_copy = dg1;
    auto dg2 = fastdelegate::MakeDelegate(&l2, &listener::on_event_const);
    XRAY_TEST_CHECK(dg1 == dg1_copy);
    XRAY_TEST_CHECK(dg1 != dg2);

    subscription_bus bus;
    {
        auto sub1 = bus.subscribe(dg1);
        auto sub2 = bus.subscribe(dg2);
        auto sub3 = bus.subscribe(fastdelegate::MakeDelegate(&l3, &listener::on_event));
        XRAY_TEST_CHECK(bus.active_count() == 3);

        bus.dispatch(5);
        std::vector<int> const expected_first = {105, -205, 305};
        XRAY_TEST_CHECK(log == expected_first);

        // Destroy middle subscriber; remaining subscribers must fire in FIFO order
        log.clear();
        sub2.reset();
        XRAY_TEST_CHECK(bus.active_count() == 2);
        bus.dispatch(9);
        std::vector<int> const expected_second = {109, 309};
        XRAY_TEST_CHECK(log == expected_second);
    }
    XRAY_TEST_CHECK(bus.active_count() == 0);

    // Test unsubscribing downstream listener re-entrantly during dispatch
    log.clear();
    subscription_bus::subscription sub_target;
    struct self_unsub {
        subscription_bus::subscription* target = nullptr;
        std::vector<int>* log_ptr = nullptr;
        void fire(int v) {
            log_ptr->push_back(v);
            target->reset();
        }
    } actor{&sub_target, &log};

    auto sub_actor = bus.subscribe(fastdelegate::MakeDelegate(&actor, &self_unsub::fire));
    sub_target = bus.subscribe(fastdelegate::MakeDelegate(&l3, &listener::on_event));
    bus.dispatch(42);
    std::vector<int> const expected_reentrant = {42};
    XRAY_TEST_CHECK(log == expected_reentrant);
    XRAY_TEST_CHECK(bus.active_count() == 1);
}

} // namespace

int main() {
#if XRAY_PLATFORM_WINDOWS
    test_unique_handle();
    test_com_ptr();
#endif
    test_scoped_cleanup();
    test_fastdelegate_lifecycle_and_order();
    std::cout << "PASS: xray_callback_and_raii_contract\n";
    return 0;
}
