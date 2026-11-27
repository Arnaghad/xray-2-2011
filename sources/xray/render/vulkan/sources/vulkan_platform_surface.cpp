////////////////////////////////////////////////////////////////////////////
//	Created		: 23.11.2026
//	Description	: Vulkan platform surface adapter (SDL3_Vulkan + native WSI + property bus)
////////////////////////////////////////////////////////////////////////////

#include <xray/os_platform.h>

#if XRAY_PLATFORM_WINDOWS
#	ifndef VK_USE_PLATFORM_WIN32_KHR
#		define VK_USE_PLATFORM_WIN32_KHR 1
#	endif
#	include <xray/os_include.h>
#elif XRAY_PLATFORM_LINUX
#	ifndef VK_USE_PLATFORM_WAYLAND_KHR
#		define VK_USE_PLATFORM_WAYLAND_KHR 1
#	endif
#	ifndef VK_USE_PLATFORM_XLIB_KHR
#		define VK_USE_PLATFORM_XLIB_KHR 1
#	endif
#	ifndef VK_USE_PLATFORM_XCB_KHR
#		define VK_USE_PLATFORM_XCB_KHR 1
#	endif
#endif

#include <vulkan/vulkan.h>

#if XRAY_PLATFORM_LINUX
#	include <dlfcn.h>
// Guard against X11/Xlib macro collisions with C++ identifiers.
#	ifdef None
#		undef None
#	endif
#	ifdef Bool
#		undef Bool
#	endif
#	ifdef Status
#		undef Status
#	endif
#	ifdef Success
#		undef Success
#	endif
#	ifdef Always
#		undef Always
#	endif
#	ifdef CurrentTime
#		undef CurrentTime
#	endif
#endif

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

std::unordered_map<platform::native_surface_handle, SDL_Window*>& window_registry()
{
	static std::unordered_map<platform::native_surface_handle, SDL_Window*> reg;
	return reg;
}

struct property_entry {
	bool							is_surface = false;
	u64								token = 0;
	platform::native_surface_handle	surface_value{};
};

std::mutex& property_mutex()
{
	static std::mutex m;
	return m;
}

std::unordered_map<platform::native_surface_handle, std::unordered_map<std::string, property_entry>>& property_registry()
{
	static std::unordered_map<platform::native_surface_handle, std::unordered_map<std::string, property_entry>> reg;
	return reg;
}

std::unordered_map<platform::native_surface_handle, std::pair<u32, u32>>& extent_registry()
{
	static std::unordered_map<platform::native_surface_handle, std::pair<u32, u32>> reg;
	return reg;
}

#if XRAY_PLATFORM_LINUX
using pfn_xget_geometry = int (*)( Display*, Drawable, Window*, int*, int*, unsigned int*, unsigned int*, unsigned int*, unsigned int* );

pfn_xget_geometry load_xget_geometry() noexcept
{
	static pfn_xget_geometry fn = []() noexcept -> pfn_xget_geometry {
		void* lib = dlopen( "libX11.so.6", RTLD_LAZY | RTLD_LOCAL );
		if ( !lib )
			lib = dlopen( "libX11.so", RTLD_LAZY | RTLD_LOCAL );
		return lib ? reinterpret_cast<pfn_xget_geometry>( dlsym( lib, "XGetGeometry" ) ) : nullptr;
	}();
	return fn;
}
#endif

bool has_available_extension( std::vector<VkExtensionProperties> const& available, char const* ext )
{
	if ( !ext || !*ext )
		return false;
	return std::any_of( available.begin(), available.end(), [ext]( VkExtensionProperties const& props ) {
		return std::strcmp( props.extensionName, ext ) == 0;
	} );
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

void push_if_available(
		std::vector<char const*>& list,
		std::vector<VkExtensionProperties> const& available,
		char const* ext
	)
{
	if ( available.empty() || has_available_extension( available, ext ) )
		push_unique_ext( list, ext );
}

} // namespace

void register_sdl_window( platform::native_surface_handle const& surface, SDL_Window* const sdl_window )
{
	if ( !surface.valid() || !sdl_window )
		return;

	std::lock_guard<std::mutex> lock( registry_mutex() );
	window_registry()[surface] = sdl_window;
}

void unregister_sdl_window( platform::native_surface_handle const& surface )
{
	if ( !surface.valid() )
		return;

	std::lock_guard<std::mutex> lock( registry_mutex() );
	window_registry().erase( surface );
}

SDL_Window* lookup_sdl_window( platform::native_surface_handle const& surface )
{
	if ( !surface.valid() )
		return nullptr;

	if ( SDL_Window* const direct = surface.as_sdl_window() )
		return direct;

	std::lock_guard<std::mutex> lock( registry_mutex() );
	auto const it = window_registry().find( surface );
	return it != window_registry().end() ? it->second : nullptr;
}

std::vector<char const*> required_instance_extensions()
{
	std::vector<char const*> result;

	uint32_t avail_count = 0;
	std::vector<VkExtensionProperties> available;
	if ( vkEnumerateInstanceExtensionProperties( nullptr, &avail_count, nullptr ) == VK_SUCCESS && avail_count > 0 ) {
		available.resize( avail_count );
		if ( vkEnumerateInstanceExtensionProperties( nullptr, &avail_count, available.data() ) != VK_SUCCESS )
			available.clear();
	}

	if ( SDL_WasInit( SDL_INIT_VIDEO ) != 0 ) {
		Uint32 sdl_count = 0;
		char const* const* const sdl_exts = SDL_Vulkan_GetInstanceExtensions( &sdl_count );
		if ( sdl_exts ) {
			for ( Uint32 i = 0; i < sdl_count; ++i )
				push_if_available( result, available, sdl_exts[i] );
		}
	}

	push_if_available( result, available, VK_KHR_SURFACE_EXTENSION_NAME );
#if XRAY_PLATFORM_WINDOWS
	push_if_available( result, available, VK_KHR_WIN32_SURFACE_EXTENSION_NAME );
#elif XRAY_PLATFORM_LINUX
	push_if_available( result, available, VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME );
	push_if_available( result, available, VK_KHR_XLIB_SURFACE_EXTENSION_NAME );
	push_if_available( result, available, VK_KHR_XCB_SURFACE_EXTENSION_NAME );
#endif
	return result;
}

bool create_surface(
		VkInstance const instance,
		platform::native_surface_handle const& surface,
		VkAllocationCallbacks const* const allocator,
		VkSurfaceKHR* const out_surface,
		bool* const out_used_sdl
	)
{
	if ( out_used_sdl )
		*out_used_sdl = false;

	if ( !instance || !surface.valid() || !out_surface )
		return false;

	*out_surface = VK_NULL_HANDLE;

	if ( SDL_Window* const sdl_win = lookup_sdl_window( surface ) ) {
		if ( !SDL_Vulkan_CreateSurface( sdl_win, instance, allocator, out_surface ) || *out_surface == VK_NULL_HANDLE ) {
			char const* const sdl_err = SDL_GetError();
			throw std::runtime_error( std::string( "SDL_Vulkan_CreateSurface failed: " ) + ( sdl_err ? sdl_err : "unknown" ) );
		}
		if ( out_used_sdl )
			*out_used_sdl = true;
		return true;
	}

	switch ( surface.kind ) {
#if XRAY_PLATFORM_WINDOWS
		case platform::native_surface_kind::win32_hwnd: {
			VkWin32SurfaceCreateInfoKHR surface_info = {};
			surface_info.sType		= VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR;
			surface_info.hinstance	= surface.display
				? reinterpret_cast<HINSTANCE>( static_cast<std::uintptr_t>( surface.display ) )
				: GetModuleHandle( nullptr );
			surface_info.hwnd		= static_cast<HWND>( surface.as_win32_hwnd_raw() );

			VkResult const res = vkCreateWin32SurfaceKHR( instance, &surface_info, allocator, out_surface );
			if ( res != VK_SUCCESS || *out_surface == VK_NULL_HANDLE ) {
				throw std::runtime_error( "Create Win32 Vulkan surface (VkResult=" + std::to_string( static_cast<int>( res ) ) + ")" );
			}
			return true;
		}
#elif XRAY_PLATFORM_LINUX
		case platform::native_surface_kind::wayland: {
			auto const create_wayland = reinterpret_cast<PFN_vkCreateWaylandSurfaceKHR>(
				vkGetInstanceProcAddr( instance, "vkCreateWaylandSurfaceKHR" )
			);
			if ( !create_wayland )
				throw std::runtime_error( "vkCreateWaylandSurfaceKHR entry point unavailable" );

			VkWaylandSurfaceCreateInfoKHR surface_info = {};
			surface_info.sType		= VK_STRUCTURE_TYPE_WAYLAND_SURFACE_CREATE_INFO_KHR;
			surface_info.display	= reinterpret_cast<struct wl_display*>( static_cast<std::uintptr_t>( surface.display ) );
			surface_info.surface	= reinterpret_cast<struct wl_surface*>( static_cast<std::uintptr_t>( surface.handle ) );

			VkResult const res = create_wayland( instance, &surface_info, allocator, out_surface );
			if ( res != VK_SUCCESS || *out_surface == VK_NULL_HANDLE ) {
				throw std::runtime_error( "Create Wayland Vulkan surface (VkResult=" + std::to_string( static_cast<int>( res ) ) + ")" );
			}
			return true;
		}
		case platform::native_surface_kind::x11_xlib: {
			auto const create_xlib = reinterpret_cast<PFN_vkCreateXlibSurfaceKHR>(
				vkGetInstanceProcAddr( instance, "vkCreateXlibSurfaceKHR" )
			);
			if ( !create_xlib )
				throw std::runtime_error( "vkCreateXlibSurfaceKHR entry point unavailable" );

			VkXlibSurfaceCreateInfoKHR surface_info = {};
			surface_info.sType		= VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR;
			surface_info.dpy		= reinterpret_cast<Display*>( static_cast<std::uintptr_t>( surface.display ) );
			surface_info.window		= static_cast<Window>( surface.handle );

			VkResult const res = create_xlib( instance, &surface_info, allocator, out_surface );
			if ( res != VK_SUCCESS || *out_surface == VK_NULL_HANDLE ) {
				throw std::runtime_error( "Create Xlib Vulkan surface (VkResult=" + std::to_string( static_cast<int>( res ) ) + ")" );
			}
			return true;
		}
		case platform::native_surface_kind::x11_xcb: {
			auto const create_xcb = reinterpret_cast<PFN_vkCreateXcbSurfaceKHR>(
				vkGetInstanceProcAddr( instance, "vkCreateXcbSurfaceKHR" )
			);
			if ( !create_xcb )
				throw std::runtime_error( "vkCreateXcbSurfaceKHR entry point unavailable" );

			VkXcbSurfaceCreateInfoKHR surface_info = {};
			surface_info.sType		= VK_STRUCTURE_TYPE_XCB_SURFACE_CREATE_INFO_KHR;
			surface_info.connection	= reinterpret_cast<xcb_connection_t*>( static_cast<std::uintptr_t>( surface.display ) );
			surface_info.window		= static_cast<xcb_window_t>( surface.handle );

			VkResult const res = create_xcb( instance, &surface_info, allocator, out_surface );
			if ( res != VK_SUCCESS || *out_surface == VK_NULL_HANDLE ) {
				throw std::runtime_error( "Create Xcb Vulkan surface (VkResult=" + std::to_string( static_cast<int>( res ) ) + ")" );
			}
			return true;
		}
#endif
		default:
			throw std::runtime_error( "Unsupported native_surface_kind for Vulkan surface creation" );
	}
}

bool query_surface_extent( platform::native_surface_handle const& surface, u32& out_width, u32& out_height )
{
	out_width = 0;
	out_height = 0;
	if ( !surface.valid() )
		return false;

	if ( SDL_Window* const sdl_win = lookup_sdl_window( surface ) ) {
		int w = 0;
		int h = 0;
		if ( !SDL_GetWindowSizeInPixels( sdl_win, &w, &h ) )
			return false;
		out_width	= w > 0 ? static_cast<u32>( w ) : 0u;
		out_height	= h > 0 ? static_cast<u32>( h ) : 0u;
		return true;
	}

#if XRAY_PLATFORM_WINDOWS
	if ( surface.kind == platform::native_surface_kind::win32_hwnd ) {
		HWND const hwnd = static_cast<HWND>( surface.as_win32_hwnd_raw() );
		if ( !hwnd || !IsWindow( hwnd ) )
			return false;
		RECT rect = {};
		if ( !GetClientRect( hwnd, &rect ) )
			return false;
		out_width	= rect.right > rect.left ? static_cast<u32>( rect.right - rect.left ) : 0u;
		out_height	= rect.bottom > rect.top ? static_cast<u32>( rect.bottom - rect.top ) : 0u;
		return true;
	}
#elif XRAY_PLATFORM_LINUX
	if ( surface.kind == platform::native_surface_kind::x11_xlib && surface.display != 0 && surface.handle != 0 ) {
		if ( auto xget = load_xget_geometry() ) {
			Window root = 0;
			int x = 0, y = 0;
			unsigned int w = 0, h = 0, border = 0, depth = 0;
			if ( xget(
					reinterpret_cast<Display*>( static_cast<std::uintptr_t>( surface.display ) ),
					static_cast<Drawable>( surface.handle ),
					&root, &x, &y, &w, &h, &border, &depth ) ) {
				out_width	= static_cast<u32>( w );
				out_height	= static_cast<u32>( h );
				return true;
			}
		}
	}
#endif

	{
		std::lock_guard<std::mutex> lock( property_mutex() );
		auto const it = extent_registry().find( surface );
		if ( it != extent_registry().end() ) {
			out_width	= it->second.first;
			out_height	= it->second.second;
			return true;
		}
	}

	if ( surface.kind == platform::native_surface_kind::offscreen && surface.handle != 0 ) {
		out_width = 960;
		out_height = 640;
		return true;
	}

	return false;
}

void set_surface_extent( platform::native_surface_handle const& surface, u32 const width, u32 const height )
{
	if ( !surface.valid() )
		return;

	std::lock_guard<std::mutex> lock( property_mutex() );
	extent_registry()[surface] = std::make_pair( width, height );
}

bool is_surface_alive( platform::native_surface_handle const& surface )
{
	if ( !surface.valid() )
		return false;

	if ( SDL_Window* const sdl_win = lookup_sdl_window( surface ) )
		return SDL_GetWindowID( sdl_win ) != 0;

	if ( surface.kind == platform::native_surface_kind::offscreen )
		return surface.handle != 0;

#if XRAY_PLATFORM_WINDOWS
	if ( surface.kind == platform::native_surface_kind::win32_hwnd ) {
		HWND const hwnd = static_cast<HWND>( surface.as_win32_hwnd_raw() );
		return hwnd && IsWindow( hwnd ) != FALSE;
	}
#elif XRAY_PLATFORM_LINUX
	if ( surface.kind == platform::native_surface_kind::wayland ||
		 surface.kind == platform::native_surface_kind::x11_xlib ||
		 surface.kind == platform::native_surface_kind::x11_xcb ) {
		return surface.display != 0 && surface.handle != 0;
	}
#endif

	return false;
}

bool is_surface_minimized( platform::native_surface_handle const& surface )
{
	if ( !surface.valid() )
		return false;

	if ( SDL_Window* const sdl_win = lookup_sdl_window( surface ) )
		return ( SDL_GetWindowFlags( sdl_win ) & SDL_WINDOW_MINIMIZED ) != 0;

#if XRAY_PLATFORM_WINDOWS
	if ( surface.kind == platform::native_surface_kind::win32_hwnd ) {
		HWND const hwnd = static_cast<HWND>( surface.as_win32_hwnd_raw() );
		return hwnd && IsIconic( GetAncestor( hwnd, GA_ROOT ) ) != FALSE;
	}
#endif

	return get_surface_property( surface, "XRAY_SURFACE_MINIMIZED" ) != 0;
}

bool set_surface_property( platform::native_surface_handle const& surface, char const* const key, u64 const value )
{
	if ( !surface.valid() || !key || !*key )
		return false;

	std::lock_guard<std::mutex> lock( property_mutex() );
	property_entry& entry = property_registry()[surface][key];
	entry.is_surface	= false;
	entry.token			= value;
	entry.surface_value	= {};
	return true;
}

u64 get_surface_property( platform::native_surface_handle const& surface, char const* const key )
{
	if ( !surface.valid() || !key || !*key )
		return 0;

	std::lock_guard<std::mutex> lock( property_mutex() );
	auto const sit = property_registry().find( surface );
	if ( sit == property_registry().end() )
		return 0;
	auto const kit = sit->second.find( key );
	if ( kit == sit->second.end() || kit->second.is_surface )
		return 0;
	return kit->second.token;
}

bool set_surface_handle_property( platform::native_surface_handle const& surface, char const* const key, platform::native_surface_handle const& value )
{
	if ( !surface.valid() || !key || !*key )
		return false;

	std::lock_guard<std::mutex> lock( property_mutex() );
	property_entry& entry = property_registry()[surface][key];
	entry.is_surface	= true;
	entry.token			= 0;
	entry.surface_value	= value;
	return true;
}

platform::native_surface_handle get_surface_handle_property( platform::native_surface_handle const& surface, char const* const key )
{
	if ( !surface.valid() || !key || !*key )
		return {};

	std::lock_guard<std::mutex> lock( property_mutex() );
	auto const sit = property_registry().find( surface );
	if ( sit == property_registry().end() )
		return {};
	auto const kit = sit->second.find( key );
	if ( kit == sit->second.end() || !kit->second.is_surface )
		return {};
	return kit->second.surface_value;
}

void remove_surface_property( platform::native_surface_handle const& surface, char const* const key )
{
	if ( !surface.valid() || !key || !*key )
		return;

	std::lock_guard<std::mutex> lock( property_mutex() );
	auto const sit = property_registry().find( surface );
	if ( sit == property_registry().end() )
		return;
	sit->second.erase( key );
	if ( sit->second.empty() )
		property_registry().erase( sit );
}

void clear_surface_properties( platform::native_surface_handle const& surface )
{
	if ( !surface.valid() )
		return;

	std::lock_guard<std::mutex> lock( property_mutex() );
	property_registry().erase( surface );
	extent_registry().erase( surface );
}

namespace {

struct offscreen_mailbox_entry {
	std::vector<u8>	bgra;
	u32				width = 0;
	u32				height = 0;
	u64				serial = 0;
};

std::mutex& offscreen_mutex()
{
	static std::mutex m;
	return m;
}

std::unordered_map<u64, offscreen_mailbox_entry>& offscreen_mailbox()
{
	static std::unordered_map<u64, offscreen_mailbox_entry> reg;
	return reg;
}

} // namespace

void publish_offscreen_frame(
		platform::native_surface_handle const& surface,
		void const* const bgra_pixels,
		u32 const width,
		u32 const height
	)
{
	if ( !surface.valid() || !bgra_pixels || width == 0 || height == 0 )
		return;

	size_t const byte_count = static_cast<size_t>( width ) * height * 4;
	std::lock_guard<std::mutex> lock( offscreen_mutex() );
	offscreen_mailbox_entry& entry = offscreen_mailbox()[surface.handle];
	entry.bgra.resize( byte_count );
	std::memcpy( entry.bgra.data(), bgra_pixels, byte_count );
	entry.width = width;
	entry.height = height;
	++entry.serial;
}

} // namespace surface_adapter
} // namespace vulkan
} // namespace render
} // namespace xray

extern "C" {

void xray_vulkan_offscreen_set_extent( u64 const handle, u32 const width, u32 const height )
{
	if ( !handle || !width || !height )
		return;
	xray::render::vulkan::surface_adapter::set_surface_extent(
		xray::platform::native_surface_handle::from_offscreen( handle ),
		width,
		height
	);
}

int xray_vulkan_offscreen_poll_frame(
		u64 const handle,
		u64* const in_out_serial,
		u32* const out_width,
		u32* const out_height,
		u8* const out_bgra,
		u32 const capacity_bytes
	)
{
	if ( !handle || !in_out_serial || !out_width || !out_height )
		return 0;

	using namespace xray::render::vulkan::surface_adapter;
	std::lock_guard<std::mutex> lock( offscreen_mutex() );
	auto const it = offscreen_mailbox().find( handle );
	if ( it == offscreen_mailbox().end() )
		return 0;

	offscreen_mailbox_entry const& entry = it->second;
	if ( entry.serial == *in_out_serial || entry.width == 0 || entry.height == 0 || entry.bgra.empty() )
		return 0;

	*out_width = entry.width;
	*out_height = entry.height;
	size_t const required = entry.bgra.size();
	if ( !out_bgra || static_cast<size_t>( capacity_bytes ) < required )
		return -1;

	std::memcpy( out_bgra, entry.bgra.data(), required );
	*in_out_serial = entry.serial;
	return 1;
}

void xray_vulkan_offscreen_release( u64 const handle )
{
	if ( !handle )
		return;

	using namespace xray::render::vulkan::surface_adapter;
	{
		std::lock_guard<std::mutex> lock( offscreen_mutex() );
		offscreen_mailbox().erase( handle );
	}
	clear_surface_properties( xray::platform::native_surface_handle::from_offscreen( handle ) );
}

} // extern "C"
