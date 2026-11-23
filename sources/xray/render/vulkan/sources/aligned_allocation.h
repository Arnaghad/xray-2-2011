#ifndef XRAY_RENDER_VULKAN_ALIGNED_ALLOCATION_H_INCLUDED
#define XRAY_RENDER_VULKAN_ALIGNED_ALLOCATION_H_INCLUDED

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>

namespace xray { namespace render { namespace vulkan {

struct allocation_functions {
    void* context;
    void* (*allocate)(void* context, size_t size);
    void (*deallocate)(void* context, void* pointer);
};

struct allocation_header {
    void* original;
    size_t size;
};

inline void* allocate_aligned(allocation_functions const& functions, size_t size, size_t alignment) {
    if (!size || !alignment || (alignment & (alignment - 1))) return nullptr;
    if (alignment < alignof(allocation_header)) alignment = alignof(allocation_header);
    size_t const max_size = (std::numeric_limits<size_t>::max)();
    if (alignment - 1 > max_size - sizeof(allocation_header)) return nullptr;
    size_t const overhead = sizeof(allocation_header) + alignment - 1;
    if (size > max_size - overhead) return nullptr;
    void* original = functions.allocate(functions.context, size + overhead);
    if (!original) return nullptr;
    uintptr_t address = reinterpret_cast<uintptr_t>(original) + sizeof(allocation_header);
    address = (address + alignment - 1) & ~(static_cast<uintptr_t>(alignment) - 1);
    auto* header = reinterpret_cast<allocation_header*>(address) - 1;
    header->original = original;
    header->size = size;
    return reinterpret_cast<void*>(address);
}

inline void free_aligned(allocation_functions const& functions, void* pointer) {
    if (!pointer) return;
    auto* header = static_cast<allocation_header*>(pointer) - 1;
    functions.deallocate(functions.context, header->original);
}

inline void* reallocate_aligned(allocation_functions const& functions, void* pointer, size_t size, size_t alignment) {
    if (!size) {
        free_aligned(functions, pointer);
        return nullptr;
    }
    void* result = allocate_aligned(functions, size, alignment);
    // Allocation failure must leave the original block intact.
    if (!result) return nullptr;
    if (pointer) {
        auto* header = static_cast<allocation_header*>(pointer) - 1;
        std::memcpy(result, pointer, header->size < size ? header->size : size);
        free_aligned(functions, pointer);
    }
    return result;
}

} } }
#endif
