#ifndef XRAY_APC_RESOURCE_CALLBACKS_H_INCLUDED
#define XRAY_APC_RESOURCE_CALLBACKS_H_INCLUDED
#include <atomic>
#include <thread>
namespace xray::apc {
// Closing admission and waiting for active callers is one lifetime boundary.
// A caller arriving after close may touch the gate, never the retired manager.
class resource_callback_gate {
    std::atomic<bool> accepting_{true};
    std::atomic<unsigned> active_{0};
public:
    bool accepting() const {return accepting_.load();}
    void open(){accepting_.store(true);}
    template<class Callback> bool dispatch(Callback&& callback) {
        if(!accepting_.load())return false;
        active_.fetch_add(1);
        struct leave {std::atomic<unsigned>& active;~leave(){active.fetch_sub(1);}} lease{active_};
        if(!accepting_.load())return false;
        callback();return true;
    }
    void close(){accepting_.store(false);while(active_.load())std::this_thread::yield();}
};
}
#endif
