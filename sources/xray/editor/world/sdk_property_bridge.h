#pragma once
#include <cstdint>
#include <cstddef>

namespace xray::editor::hosting {
// Optional SDK extension. The existing callbacks/world_bridge ABI v2 is unchanged.
// All calls are synchronous on the editor owner; neither side retains payload pointers.
struct sdk_position {
    std::uint32_t version,size;
    std::uint64_t object,revision;
    float x,y,z;
    std::uint32_t reserved;
};
struct sdk_callbacks {
    std::uint32_t version,size;
    void* context;
    std::uint32_t flags,reserved; // bit 0: permit the background diagnostic steps
    int (*read)(void*,sdk_position*);
    // Compare object + revision, queue the render mutation, return the new snapshot.
    // On rejection both the scene and caller's payload are unchanged.
    int (*write)(void*,sdk_position*);
};
struct sdk_bridge {
    std::uint32_t version,size;
    int (*diagnostic_step)(void*,std::uint32_t);
};
using bind_sdk_fn=int (*)(void*,sdk_callbacks const*,sdk_bridge*);
static_assert(sizeof(sdk_position)==40 && offsetof(sdk_position,x)==24);
static_assert(sizeof(sdk_callbacks)==40 && offsetof(sdk_callbacks,read)==24);
static_assert(sizeof(sdk_bridge)==16);
}
