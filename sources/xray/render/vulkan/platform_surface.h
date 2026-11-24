////////////////////////////////////////////////////////////////////////////
//	Created		: 23.11.2026
//	Description	: Platform window/surface adapter (SDL3 primary + Win32 HWND fallback)
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_VULKAN_PLATFORM_SURFACE_H_INCLUDED
#define XRAY_RENDER_VULKAN_PLATFORM_SURFACE_H_INCLUDED

#ifndef VK_USE_PLATFORM_WIN32_KHR
#define VK_USE_PLATFORM_WIN32_KHR 1
#endif

#include <vulkan/vulkan.h>
#include <xray/render/vulkan/api.h>
#include <vector>

struct SDL_Window;
struct HWND__;
typedef HWND__* HWND;

namespace xray {
namespace render {
namespace vulkan {
namespace surface_adapter {

XRAY_RENDER_VULKAN_API	void		register_sdl_window		( HWND native_hwnd, SDL_Window* sdl_window );
XRAY_RENDER_VULKAN_API	void		unregister_sdl_window	( HWND native_hwnd );
XRAY_RENDER_VULKAN_API	SDL_Window*	lookup_sdl_window		( HWND native_hwnd );

XRAY_RENDER_VULKAN_API	std::vector<char const*>	required_instance_extensions();
XRAY_RENDER_VULKAN_API	bool						create_surface(
														VkInstance instance,
														HWND hwnd,
														VkAllocationCallbacks const* allocator,
														VkSurfaceKHR* out_surface,
														bool* out_used_sdl = nullptr
													);

} // namespace surface_adapter
} // namespace vulkan
} // namespace render
} // namespace xray

#endif // #ifndef XRAY_RENDER_VULKAN_PLATFORM_SURFACE_H_INCLUDED
