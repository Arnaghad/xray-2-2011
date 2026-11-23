#ifndef XRAY_RENDER_VULKAN_HANDLE_POOL_H_INCLUDED
#define XRAY_RENDER_VULKAN_HANDLE_POOL_H_INCLUDED

#include <xray/render/core/sources/rhi/rhi_types.h>
#include <cstdint>
#include <vector>

namespace xray { namespace render { namespace vulkan {

// Upper 32 bits are the generation; lower 32 bits are the slot index.
template<typename T>
class handle_pool {
public:
    uint64_t allocate(T const& entry) {
        uint32_t index;
        if (!m_free_list.empty()) {
            index = m_free_list.back();
            m_entries[index].entry = entry;
            m_free_list.pop_back();
        } else {
            index = static_cast<uint32_t>(m_entries.size());
            // Reserve before ownership is acquired: release must not allocate.
            if (m_free_list.capacity() < m_entries.size() + 1)
                m_free_list.reserve((m_entries.size() + 1) * 2);
            m_entries.push_back({entry, 1});
        }
        m_entries[index].entry.in_use = true;
        m_entries[index].active = true;
        return (static_cast<uint64_t>(m_entries[index].generation) << 32) | index;
    }

    T* get(uint64_t handle) {
        uint32_t index = static_cast<uint32_t>(handle);
        uint32_t generation = static_cast<uint32_t>(handle >> 32);
        if (handle == RHI_INVALID_HANDLE || index >= m_entries.size()
            || m_entries[index].generation != generation || !m_entries[index].active)
            return nullptr;
        return &m_entries[index].entry;
    }

    T const* get(uint64_t handle) const {
        return const_cast<handle_pool*>(this)->get(handle);
    }

    void release(uint64_t handle) noexcept {
        T* entry = get(handle);
        if (!entry) return;
        uint32_t index = static_cast<uint32_t>(handle);
        entry->in_use = false;
        m_entries[index].active = false;
        // Retire an exhausted slot instead of making ancient handles valid.
        if (m_entries[index].generation == UINT32_MAX) return;
        ++m_entries[index].generation;
        m_free_list.push_back(index);
    }

    template<typename Fn>
    void for_each_active(Fn&& fn) {
        for (auto& entry : m_entries)
            if (entry.active) fn(entry.entry);
    }

    void clear() {
        // Preserve generations across clear/reinitialize so stale handles stay stale.
        for (uint32_t i = 0; i < m_entries.size(); ++i) {
            if (m_entries[i].active)
                release((static_cast<uint64_t>(m_entries[i].generation) << 32) | i);
            m_entries[i].entry = {};
        }
    }

    size_t active_count() const {
        size_t count = 0;
        for (auto const& entry : m_entries)
            if (entry.active) ++count;
        return count;
    }

private:
    // Pool liveness must not change if a resource copy throws halfway through.
    struct slot { T entry; uint32_t generation = 1; bool active = false; };
    std::vector<slot> m_entries;
    std::vector<uint32_t> m_free_list;
};

} } }
#endif
