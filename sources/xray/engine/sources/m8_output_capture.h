#pragma once
#include <xray/engine/capture_control_bus.h>
#include <xray/engine/m8_output_protocol.h>
#include <xray/engine/m8_control_protocol.h>
#include <xray/render/vulkan/platform_surface.h>
#include <xray/render/vulkan/sources/vulkan_backend.h>
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/os_platform.h>
#include <SDL3/SDL.h>
#include <array>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#if XRAY_PLATFORM_WINDOWS
#	include <xray/os_include.h>
#endif

namespace xray::engine::m8_outputs {

inline bool background_window_hidden( platform::native_surface_handle const& surface ) {
	if ( !render::vulkan::surface_adapter::is_surface_alive( surface ) )
		return false;

	if ( SDL_Window* const sdl_win = render::vulkan::surface_adapter::lookup_sdl_window( surface ) ) {
		SDL_WindowFlags const flags = SDL_GetWindowFlags( sdl_win );
		bool const hidden_or_min = ( flags & ( SDL_WINDOW_HIDDEN | SDL_WINDOW_MINIMIZED ) ) != 0;
		bool const has_focus = ( flags & ( SDL_WINDOW_INPUT_FOCUS | SDL_WINDOW_MOUSE_FOCUS ) ) != 0;
		return hidden_or_min && !has_focus;
	}

#if XRAY_PLATFORM_WINDOWS
	if ( surface.kind == platform::native_surface_kind::win32_hwnd ) {
		HWND const window = static_cast<HWND>( surface.as_win32_hwnd_raw() );
		return IsWindow( window )
			&& !IsWindowVisible( window )
			&& GetForegroundWindow() != window
			&& GetActiveWindow() != window
			&& GetFocus() != window
			&& GetCapture() != window;
	}
#endif

	return true;
}

inline void protected_window( platform::native_surface_handle const& surface ) {
	if ( !render::vulkan::surface_adapter::is_surface_alive( surface ) )
		throw std::runtime_error( "Output workflow surface is no longer alive" );

	if ( SDL_Window* const sdl_win = render::vulkan::surface_adapter::lookup_sdl_window( surface ) ) {
		SDL_WindowFlags const flags = SDL_GetWindowFlags( sdl_win );
		bool const hidden_or_min = ( flags & SDL_WINDOW_HIDDEN ) != 0 || ( flags & SDL_WINDOW_MINIMIZED ) != 0;
		bool const has_focus = ( flags & ( SDL_WINDOW_INPUT_FOCUS | SDL_WINDOW_MOUSE_FOCUS ) ) != 0;
		if ( !hidden_or_min || has_focus )
			throw std::runtime_error( "Output workflow window acquired input/visibility" );
		return;
	}

#if XRAY_PLATFORM_WINDOWS
	if ( surface.kind == platform::native_surface_kind::win32_hwnd ) {
		HWND const window = static_cast<HWND>( surface.as_win32_hwnd_raw() );
		auto root = GetAncestor( window, GA_ROOT );
		BYTE alpha = 255;
		DWORD flags = 0;
		COLORREF color = 0;
		if ( !IsWindow( window ) || GetForegroundWindow() == window || GetForegroundWindow() == root
			|| GetActiveWindow() == window || GetActiveWindow() == root || GetFocus() == window || GetCapture() == window
			|| ( IsWindowVisible( window ) && !IsIconic( root ) )
			|| !GetLayeredWindowAttributes( root, &color, &alpha, &flags ) || !( flags & LWA_ALPHA ) || alpha != 0 )
			throw std::runtime_error( "Output workflow window acquired input/visibility" );
		return;
	}
#endif
}

inline void write_bmp_bgra(
		std::filesystem::path const& path,
		u32 const width,
		u32 const height,
		std::vector<unsigned char> const& bgra
	) {
	if ( std::filesystem::exists( path ) )
		throw std::runtime_error( "Output workflow capture already exists" );

	std::array<unsigned char, 54> header{};
	auto write_u16 = [&]( std::size_t off, std::uint16_t v ) {
		header[off + 0] = static_cast<unsigned char>( v & 0xffu );
		header[off + 1] = static_cast<unsigned char>( ( v >> 8 ) & 0xffu );
	};
	auto write_u32 = [&]( std::size_t off, std::uint32_t v ) {
		header[off + 0] = static_cast<unsigned char>( v & 0xffu );
		header[off + 1] = static_cast<unsigned char>( ( v >> 8 ) & 0xffu );
		header[off + 2] = static_cast<unsigned char>( ( v >> 16 ) & 0xffu );
		header[off + 3] = static_cast<unsigned char>( ( v >> 24 ) & 0xffu );
	};

	std::uint32_t const file_size = static_cast<std::uint32_t>( header.size() + bgra.size() );
	std::int32_t const neg_height = -static_cast<std::int32_t>( height );
	header[0] = 'B';
	header[1] = 'M';
	write_u32( 2, file_size );
	write_u32( 10, static_cast<std::uint32_t>( header.size() ) );
	write_u32( 14, 40u );
	write_u32( 18, width );
	write_u32( 22, static_cast<std::uint32_t>( neg_height ) );
	write_u16( 26, 1u );
	write_u16( 28, 32u );

	std::ofstream out( path, std::ios::binary );
	out.write( reinterpret_cast<char const*>( header.data() ), static_cast<std::streamsize>( header.size() ) );
	out.write( reinterpret_cast<char const*>( bgra.data() ), static_cast<std::streamsize>( bgra.size() ) );
	out.close();
	if ( !out )
		throw std::runtime_error( "Output workflow capture write failed" );
}

inline void write_image( std::filesystem::path const& path, render::vulkan::vulkan_backend::captured_frame const& image ) {
	write_bmp_bgra( path, image.width, image.height, image.bgra );
}

inline void capture( platform::native_surface_handle const& main, unsigned current_frame, pcstr directory ) {
	auto request = capture_control_bus::get_token( main, request_property );
	if ( !request || request == capture_control_bus::get_token( main, ack_property ) || current_frame < frame( request ) )
		return;
	if ( current_frame != frame( request ) || !name( phase( request ) ) )
		throw std::runtime_error( "Missed/unknown paired output capture" );

	auto step = phase( request );
	auto* backend = dynamic_cast<render::vulkan::vulkan_backend*>( render::rhi::rhi_device::ref().get_backend() );
	if ( !backend )
		throw std::runtime_error( "Output workflow backend missing" );

	auto original = backend->output();
	auto outputs = backend->outputs();
	auto preview = capture_control_bus::get_surface( main, m8_control::preview_property );
	std::shared_ptr<render::vulkan::presentation_output> primary, secondary;
	for ( auto const& output : outputs ) {
		if ( output->surface_handle == main )
			primary = output;
		if ( preview.valid() && output->surface_handle == preview )
			secondary = output;
	}

	bool single = step == shot::preview_closed || step == shot::shutdown_ready;
	if ( !primary || outputs.size() != ( single ? 1u : 2u ) || ( !single && ( !secondary || primary == secondary
		|| primary->surface == secondary->surface || primary->swapchain == secondary->swapchain ) ) )
		throw std::runtime_error( "Paired views do not own independent presentation objects" );

	protected_window( main );
	if ( secondary )
		protected_window( preview );

	if ( render::vulkan::surface_adapter::is_surface_minimized( main ) != ( step == shot::main_minimized )
		|| ( secondary && render::vulkan::surface_adapter::is_surface_minimized( preview ) != ( step == shot::preview_minimized ) ) )
		throw std::runtime_error( "Output workflow did not reach the requested minimize state" );

	auto folder = std::filesystem::absolute( directory );
	std::filesystem::create_directories( folder );
	auto take = [&]( auto const& output, char const* view ) {
		backend->select_output( output );
		auto image = backend->capture_frame();
		u32 w = 0, h = 0;
		if ( !render::vulkan::surface_adapter::query_surface_extent( output->surface_handle, w, h ) || image.width != w || image.height != h )
			throw std::runtime_error( "Paired capture extent does not match its surface" );
		write_image( folder / ( std::string( name( step ) ) + "." + view + ".bmp" ), image );
	};

	if ( step != shot::main_minimized )
		take( primary, "main" );
	if ( secondary && step != shot::preview_minimized )
		take( secondary, "preview" );

	backend->select_output( original );
	std::cerr << "M8 outputs capture: step=" << name( step ) << " frame=" << current_frame
		<< " main_frames=" << primary->presented_frames
		<< " preview_frames=" << ( secondary ? secondary->presented_frames : 0 )
		<< " outputs=" << outputs.size() << " render_thread=" << threading::current_thread_id() << '\n';

	if ( !capture_control_bus::set_token( main, ack_property, request ) )
		throw std::runtime_error( "Cannot acknowledge paired output capture" );
}

}
