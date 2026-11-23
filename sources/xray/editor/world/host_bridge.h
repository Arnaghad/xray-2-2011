#ifndef XRAY_EDITOR_HOST_BRIDGE_H_INCLUDED
#define XRAY_EDITOR_HOST_BRIDGE_H_INCLUDED
#include <cstdint>
#include <cstddef>
namespace xray::editor::hosting {
inline constexpr std::uint32_t abi_version=2;
inline constexpr std::uint32_t background=1;
// C ABI: pointers/handles retain their native width; matrices are 16 copied
// floats, never a compiler-specific float4x4 value or a retained caller pointer.
struct callbacks {
    std::uint32_t version,size;
    void* context;
    std::uint32_t flags,reserved;
    int (*tick)(void*);
    int (*render)(void*,std::uint32_t,float const*);
    int (*clear)(void*);
    int (*closing)(void*);
    int (*request_exit)(void*,int);
    int (*command)(void*,std::uint32_t);
    // Open (0) or request close (1). The HWND remains alive until close ACK.
    int (*preview)(void*,std::uintptr_t,std::uint32_t);
};
struct world_bridge {
    std::uint32_t version,size;
    void* context;
    int (*load)(void*);
    int (*run)(void*);
    int (*clear)(void*);
    int (*destroy)(void*);
    std::uintptr_t (*main_handle)(void*);
    std::uintptr_t (*view_handle)(void*);
    int (*editor_mode)(void*,int);
    int (*render)(void*,std::uint32_t,float const*);
    int (*message_out)(void*,char const*);
    int (*command)(void*,std::uint32_t);
    std::uintptr_t (*preview_handle)(void*);
    int (*preview_closed)(void*);
};
using create_world_fn=int (*)(callbacks const*,world_bridge*);
static_assert(offsetof(callbacks,context)==8 && sizeof(callbacks)==80);
static_assert(offsetof(world_bridge,context)==8 && sizeof(world_bridge)==112);
}
#endif
