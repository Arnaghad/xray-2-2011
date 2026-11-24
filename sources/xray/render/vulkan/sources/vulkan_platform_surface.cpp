////////////////////////////////////////////////////////////////////////////
//	Created		: 23.11.2026
//	Description	: Vulkan platform surface adapter (SDL3_Vulkan + Win32 HWND)
////////////////////////////////////////////////////////////////////////////

#include <xray/os_include.h>
#include <xray/render/vulkan/platform_surface.h>
#include <SDL3/SDL.h>
#include <SDL3/SDL_vulkan.h>

#include <algorithm>
#include <cstring>
#include <mutex>
#include <stdexcept>
#include <string>
#include <unordered_map>

namespace xray {
namespace render {
namespace vulkan {
namespace surface_adapter {

namespace {

std::mutex& registry_mutex()
{
	static std::mutex m;
	return m;
}

std::unordered_map<HWND, SDL_Window*>& window_registry()
{
	static std::unordered_map<HWND, SDL_Window*> reg;
	return reg;
}

void push_unique_ext( std::vector<char const*>& list, char const* ext )
{
	if ( !ext || !*ext )
		return;
	bool const present = std::any_of( list.begin(), list.end(), [ext]( char const* existing ) {
		return std::strcmp( existing, ext ) == 0;
	} );
	if ( !present )
		list.push_back( ext );
}

} // namespace

void register_sdl_window( HWND const native_hwnd, SDL_Window* const sdl_window )
{
	if ( !native_hwnd || !sdl_window )
		return;

	std::lock_guard<std::mutex> lock( registry_mutex() );
	window_registry()[native_hwnd] = sdl_window;
}

void unregister_sdl_window( HWND const native_hwnd )
{
	if ( !native_hwnd )
		return;

	std::lock_guard<std::mutex> lock( registry_mutex() );
	window_registry().erase( native_hwnd );
}

SDL_Window* lookup_sdl_window( HWND const native_hwnd )
{
	if ( !native_hwnd )
		return nullptr;

	std::lock_guard<std::mutex> lock( registry_mutex() );
	auto const it = window_registry().find( native_hwnd );
	return it != window_registry().end() ? it->second : nullptr;
}

std::vector<char const*> required_instance_extensions()
{
	std::vector<char const*> result;

	if ( SDL_WasInit( SDL_INIT_VIDEO ) != 0 ) {
		Uint32 sdl_count = 0;
		char const* const* const sdl_exts = SDL_Vulkan_GetInstanceExtensions( &sdl_count );
		if ( sdl_exts ) {
			for ( Uint32 i = 0; i < sdl_count; ++i )
				push_unique_ext( result, sdl_exts[i] );
		}
	}

	// Ensure Win32 surface extensions remain available for embedded EditorHost HWNDs or headless runs.
	push_unique_ext( result, VK_KHR_SURFACE_EXTENSION_NAME );
	push_unique_ext( result, VK_KHR_WIN32_SURFACE_EXTENSION_NAME );
	return result;
}

bool create_surface(
		VkInstance const instance,
		HWND const hwnd,
		VkAllocationCallbacks const* const allocator,
		VkSurfaceKHR* const out_surface,
		bool* const out_used_sdl
	)
{
	if ( out_used_sdl )
		*out_used_sdl = false;

	if ( !hwnd || !out_surface )
		return false;

	*out_surface = VK_NULL_HANDLE;

	if ( SDL_Window* const sdl_win = lookup_sdl_window( hwnd ) ) {
		if ( !SDL_Vulkan_CreateSurface( sdl_win, instance, allocator, out_surface ) || *out_surface == VK_NULL_HANDLE ) {
			char const* const sdl_err = SDL_GetError();
			throw std::runtime_error( std::string( "SDL_Vulkan_CreateSurface failed: " ) + ( sdl_err ? sdl_err : "unknown" ) );
		}
		if ( out_used_sdl )
			*out_used_sdl = true;
		return true;
	}

	VkWin32SurfaceCreateInfoKHR surface_info = {};
	surface_info.sType     = VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR;
	surface_info.hinstance = GetModuleHandle( nullptr );
	surface_info.hwnd      = hwnd;

	VkResult const res = vkCreateWin32SurfaceKHR( instance, &surface_info, allocator, out_surface );
	if ( res != VK_SUCCESS || *out_surface == VK_NULL_HANDLE ) {
		throw std::runtime_error( "Create Win32 Vulkan surface (VkResult=" + std::to_string( static_cast<int>( res ) ) + ")" );
	}
	return true;
}

} // namespace surface_adapter
} // namespace vulkan
} // namespace render
} // namespace xray
