////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Cross-platform native surface/window handle (SDL3 / Win32 / Wayland / X11)
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_PLATFORM_SURFACE_H_INCLUDED
#define XRAY_PLATFORM_SURFACE_H_INCLUDED

#include <xray/type_extensions.h>
#include <cstdint>
#include <functional>
#include <type_traits>

struct SDL_Window;

namespace xray {
namespace platform {

enum class native_surface_kind : u32 {
	none		= 0,
	sdl_window	= 1,
	win32_hwnd	= 2,
	x11_xlib	= 3,
	x11_xcb		= 4,
	wayland		= 5,
	offscreen	= 6,
};

struct native_surface_handle {
	native_surface_kind	kind	= native_surface_kind::none;
	u64					handle	= 0;	// SDL_Window* / Win32 handle / X11 Window / wl_surface*
	u64					display	= 0;	// HINSTANCE / Display* / xcb_connection_t* / wl_display*

	constexpr bool valid() const noexcept
	{
		return kind != native_surface_kind::none && handle != 0;
	}

	constexpr explicit operator bool() const noexcept
	{
		return valid();
	}

	constexpr bool operator==( native_surface_handle const& ) const noexcept = default;

	static inline native_surface_handle from_sdl_window( SDL_Window* const window ) noexcept
	{
		if ( !window )
			return {};
		native_surface_handle result;
		result.kind		= native_surface_kind::sdl_window;
		result.handle	= static_cast<u64>( reinterpret_cast<std::uintptr_t>( window ) );
		return result;
	}

	inline SDL_Window* as_sdl_window() const noexcept
	{
		if ( kind != native_surface_kind::sdl_window || handle == 0 )
			return nullptr;
		return reinterpret_cast<SDL_Window*>( static_cast<std::uintptr_t>( handle ) );
	}

	static constexpr native_surface_handle from_x11_xlib( u64 const dpy, u64 const window ) noexcept
	{
		if ( !window )
			return {};
		native_surface_handle result;
		result.kind		= native_surface_kind::x11_xlib;
		result.handle	= window;
		result.display	= dpy;
		return result;
	}

	static constexpr native_surface_handle from_x11_xcb( u64 const connection, u64 const window ) noexcept
	{
		if ( !window )
			return {};
		native_surface_handle result;
		result.kind		= native_surface_kind::x11_xcb;
		result.handle	= window;
		result.display	= connection;
		return result;
	}

	static constexpr native_surface_handle from_wayland( u64 const wl_display, u64 const wl_surface ) noexcept
	{
		if ( !wl_surface || !wl_display )
			return {};
		native_surface_handle result;
		result.kind		= native_surface_kind::wayland;
		result.handle	= wl_surface;
		result.display	= wl_display;
		return result;
	}

	static constexpr native_surface_handle from_offscreen( u64 const id ) noexcept
	{
		if ( !id )
			return {};
		native_surface_handle result;
		result.kind		= native_surface_kind::offscreen;
		result.handle	= id;
		result.display	= 0;
		return result;
	}

	static inline native_surface_handle from_win32_hwnd( void* const hwnd, void* const hinstance = nullptr ) noexcept
	{
		if ( !hwnd )
			return {};
		native_surface_handle result;
		result.kind		= native_surface_kind::win32_hwnd;
		result.handle	= static_cast<u64>( reinterpret_cast<std::uintptr_t>( hwnd ) );
		result.display	= static_cast<u64>( reinterpret_cast<std::uintptr_t>( hinstance ) );
		return result;
	}

	inline void* as_win32_hwnd_raw() const noexcept
	{
		if ( kind != native_surface_kind::win32_hwnd || handle == 0 )
			return nullptr;
		return reinterpret_cast<void*>( static_cast<std::uintptr_t>( handle ) );
	}
};

static_assert( std::is_trivially_copyable_v<native_surface_handle>,
	"native_surface_handle must stay trivially copyable for resources::user_data_variant" );

} // namespace platform
} // namespace xray

namespace std {

template <>
struct hash<xray::platform::native_surface_handle> {
	size_t operator()( xray::platform::native_surface_handle const& value ) const noexcept
	{
		size_t h1 = std::hash<u32>{}( static_cast<u32>( value.kind ) );
		size_t h2 = std::hash<u64>{}( value.handle );
		size_t h3 = std::hash<u64>{}( value.display );
		size_t seed = h1;
		seed ^= h2 + 0x9e3779b97f4a7c15ULL + ( seed << 6 ) + ( seed >> 2 );
		seed ^= h3 + 0x9e3779b97f4a7c15ULL + ( seed << 6 ) + ( seed >> 2 );
		return seed;
	}
};

} // namespace std

#endif // #ifndef XRAY_PLATFORM_SURFACE_H_INCLUDED
