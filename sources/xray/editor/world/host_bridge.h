#ifndef XRAY_EDITOR_HOST_BRIDGE_H_INCLUDED
#define XRAY_EDITOR_HOST_BRIDGE_H_INCLUDED
#include <cstdint>
#include <cstddef>
namespace xray::editor::hosting {
inline constexpr std::uint32_t abi_version=3;
inline constexpr std::uint32_t background=1;

enum class surface_kind : std::uint32_t {
    none       = 0,
    sdl_window = 1,
    win32_hwnd = 2,
    x11_xlib   = 3,
    x11_xcb    = 4,
    wayland    = 5,
    offscreen  = 6,
};

struct surface_descriptor {
    std::uint32_t kind;
    std::uint32_t reserved;
    std::uint64_t handle;
    std::uint64_t display;
};

enum class window_control_op : std::uint32_t {
    protect_background = 0,
    resize             = 1,
    minimize           = 2,
    close              = 3,
    is_alive           = 4,
    resolve_surface    = 5,
};

struct window_control_request {
    std::uint32_t version,size;
    std::uint32_t operation,target; // target: 0=main, 1=preview, 2=by surface.handle
    std::int32_t  width,height;
    std::uint32_t arg0,reserved;
    surface_descriptor surface;
};

// C ABI: pointers/handles retain their native width and surface descriptors;
// matrices are 16 copied floats, never a compiler-specific float4x4 value or a retained caller pointer.
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
    // Open (0) or request close (1). The surface remains alive until close ACK.
    int (*preview)(void*,surface_descriptor const*,std::uint32_t);
};
struct world_bridge {
    std::uint32_t version,size;
    void* context;
    int (*load)(void*);
    int (*run)(void*);
    int (*clear)(void*);
    int (*destroy)(void*);
    int (*main_surface)(void*,surface_descriptor*);
    int (*view_surface)(void*,surface_descriptor*);
    int (*editor_mode)(void*,int);
    int (*render)(void*,std::uint32_t,float const*);
    int (*message_out)(void*,char const*);
    int (*command)(void*,std::uint32_t);
    int (*preview_surface)(void*,surface_descriptor*);
    int (*preview_closed)(void*);
    int (*window_control)(void*,window_control_request const*,surface_descriptor*);
};
using create_world_fn=int (*)(callbacks const*,world_bridge*);
static_assert(sizeof(surface_descriptor)==24);
static_assert(sizeof(window_control_request)==56);
static_assert(offsetof(callbacks,context)==8 && sizeof(callbacks)==80);
static_assert(offsetof(world_bridge,context)==8 && sizeof(world_bridge)==120);
}
#endif
