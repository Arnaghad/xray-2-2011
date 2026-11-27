////////////////////////////////////////////////////////////////////////////
//	Created		: 23.11.2026
//	Description	: Platform window/surface adapter (SDL3 primary + native WSI handles)
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_VULKAN_PLATFORM_SURFACE_H_INCLUDED
#define XRAY_RENDER_VULKAN_PLATFORM_SURFACE_H_INCLUDED

#include <vulkan/vulkan.h>
#include <xray/platform_surface.h>
#include <xray/render/vulkan/api.h>
#include <cstdint>
#include <vector>

struct SDL_Window;

namespace xray {
namespace render {
namespace vulkan {
namespace surface_adapter {

XRAY_RENDER_VULKAN_API	void		register_sdl_window		( platform::native_surface_handle const& surface, SDL_Window* sdl_window );
XRAY_RENDER_VULKAN_API	void		unregister_sdl_window	( platform::native_surface_handle const& surface );
XRAY_RENDER_VULKAN_API	SDL_Window*	lookup_sdl_window		( platform::native_surface_handle const& surface );

XRAY_RENDER_VULKAN_API	std::vector<char const*>	required_instance_extensions();
XRAY_RENDER_VULKAN_API	bool						create_surface(
														VkInstance instance,
														platform::native_surface_handle const& surface,
														VkAllocationCallbacks const* allocator,
														VkSurfaceKHR* out_surface,
														bool* out_used_sdl = nullptr
													);

XRAY_RENDER_VULKAN_API	bool		query_surface_extent	( platform::native_surface_handle const& surface, u32& out_width, u32& out_height );
XRAY_RENDER_VULKAN_API	void		set_surface_extent		( platform::native_surface_handle const& surface, u32 width, u32 height );
XRAY_RENDER_VULKAN_API	bool		is_surface_alive		( platform::native_surface_handle const& surface );
XRAY_RENDER_VULKAN_API	bool		is_surface_minimized	( platform::native_surface_handle const& surface );

XRAY_RENDER_VULKAN_API	bool		set_surface_property	( platform::native_surface_handle const& surface, char const* key, u64 value );
XRAY_RENDER_VULKAN_API	u64			get_surface_property	( platform::native_surface_handle const& surface, char const* key );
XRAY_RENDER_VULKAN_API	bool		set_surface_handle_property( platform::native_surface_handle const& surface, char const* key, platform::native_surface_handle const& value );
XRAY_RENDER_VULKAN_API	platform::native_surface_handle	get_surface_handle_property( platform::native_surface_handle const& surface, char const* key );
XRAY_RENDER_VULKAN_API	void		remove_surface_property	( platform::native_surface_handle const& surface, char const* key );
XRAY_RENDER_VULKAN_API	void		clear_surface_properties( platform::native_surface_handle const& surface );
XRAY_RENDER_VULKAN_API	void		publish_offscreen_frame	( platform::native_surface_handle const& surface, void const* bgra_pixels, u32 width, u32 height );

} // namespace surface_adapter
} // namespace vulkan
} // namespace render
} // namespace xray

extern "C" {
XRAY_RENDER_VULKAN_API	void		xray_vulkan_offscreen_set_extent	( u64 handle, u32 width, u32 height );
XRAY_RENDER_VULKAN_API	int			xray_vulkan_offscreen_poll_frame	( u64 handle, u64* in_out_serial, u32* out_width, u32* out_height, u8* out_bgra, u32 capacity_bytes );
XRAY_RENDER_VULKAN_API	void		xray_vulkan_offscreen_release		( u64 handle );
}

#endif // #ifndef XRAY_RENDER_VULKAN_PLATFORM_SURFACE_H_INCLUDED
