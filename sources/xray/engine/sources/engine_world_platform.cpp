////////////////////////////////////////////////////////////////////////////
//	Created 	: 21.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/os_platform.h>
#include <xray/platform_surface.h>
#include <xray/engine/capture_control_bus.h>
#include <SDL3/SDL.h>
#include "engine_world.h"
#include "apc.h"
#include <xray/input/world.h>
#include <xray/input/mouse.h>
#include <xray/input_event_queue.h>
#include <xray/editor/world/world.h>
#include <xray/render/facade/engine_renderer.h>
#include <xray/core/core.h>
#include <xray/fs_utils.h>
#include <xray/resources_fs.h>
#include <xray/render/api.h>
#if XRAY_PLATFORM_WINDOWS
#	include <xray/os_include.h>
#endif
#if defined(USE_VULKAN_RENDERER)
#	include <xray/render/vulkan/platform_surface.h>
#	include <xray/render/vulkan/sources/vulkan_backend.h>
#	include <xray/render/core/sources/rhi/rhi_device.h>
#	include <xray/render/world.h>
#	include <xray/render/vulkan/diagnostics.h>
#	include <xray/engine/m8_control_protocol.h>
#	include "m8_output_capture.h"
#	include "m9_performance_capture.h"
#	include <filesystem>
#	include <fstream>
#	include <iostream>
#endif

static xray::command_line::key	bhwWindowed("windowed", "", "", "windowed mode");
static xray::command_line::key s_render_assets("render_assets", "", "render", "prepared resource root (resources and resources.converted)");
static xray::command_line::key s_render_capture("render_capture", "", "render", "hidden nonactivating capture directory; bounded control-level run");
static xray::command_line::key s_render_performance("render_performance", "", "render", "background manifest performance: warmup,stress seconds");

using xray::engine::engine_world;

static pcstr s_window_id					= XRAY_ENGINE_ID " | Vulkan";

xray::command_line::key	s_no_log_file		("no_log_file", "no_log_file", "logging", "disables writing log to a file", "<no arguments>");

static xray::engine::engine_world*			s_world			= 0;
static SDL_Window*							s_sdl_window	= nullptr;
static bool									s_sdl_owns_subs	= false;

xray::platform::native_surface_handle new_window	( )
{
	if ( s_render_capture.is_set() ) {
		CHECK_OR_EXIT( SDL_InitSubSystem( SDL_INIT_VIDEO ), "Cannot initialize SDL3 video for background render capture" );
		s_sdl_owns_subs = true;
		s_sdl_window = SDL_CreateWindow(
			s_window_id,
			960,
			640,
			SDL_WINDOW_VULKAN | SDL_WINDOW_HIDDEN | SDL_WINDOW_BORDERLESS | SDL_WINDOW_HIGH_PIXEL_DENSITY
		);
		CHECK_OR_EXIT( s_sdl_window != nullptr, "Cannot create background SDL3 Vulkan window" );
		SDL_SetWindowFocusable( s_sdl_window, false );
		auto const surface = xray::platform::native_surface_handle::from_sdl_window( s_sdl_window );
#if defined(USE_VULKAN_RENDERER)
		xray::render::vulkan::surface_adapter::register_sdl_window( surface, s_sdl_window );
#endif
		CHECK_OR_EXIT(
			xray::engine::capture_control_bus::set_flag( surface, "XRAY_VULKAN_TEST_CAPTURE" ),
			"Cannot mark background render surface for capture"
		);
		return surface;
	}

	CHECK_OR_EXIT( SDL_InitSubSystem( SDL_INIT_VIDEO | SDL_INIT_GAMEPAD ), "Cannot initialize SDL3 video/gamepad" );
	s_sdl_owns_subs = true;

	int screen_size_x = 1280;
	int screen_size_y = 720;
	if ( SDL_DisplayID const primary = SDL_GetPrimaryDisplay() ) {
		if ( SDL_DisplayMode const* const mode = SDL_GetCurrentDisplayMode( primary ) ) {
			if ( mode->w > 0 && mode->h > 0 ) {
				screen_size_x = mode->w;
				screen_size_y = mode->h;
			}
		}
	}

	int win_w = screen_size_x;
	int win_h = screen_size_y;
	SDL_WindowFlags win_flags = SDL_WINDOW_VULKAN | SDL_WINDOW_HIDDEN | SDL_WINDOW_HIGH_PIXEL_DENSITY;

	if ( bhwWindowed || ( s_world && s_world->command_line_editor() ) ) {
		int const window_sizes_x[] = { 1024, 800, 640 };
		int const window_sizes_y[] = { 768, 600, 480 };
		for ( u32 i = 0; i < xray::array_size( window_sizes_x ); ++i ) {
			if ( window_sizes_x[i] < screen_size_x && window_sizes_y[i] < screen_size_y ) {
				win_w = window_sizes_x[i];
				win_h = window_sizes_y[i];
				break;
			}
		}
		win_flags |= SDL_WINDOW_RESIZABLE;
	} else {
		win_flags |= SDL_WINDOW_FULLSCREEN;
	}

	s_sdl_window = SDL_CreateWindow( s_window_id, win_w, win_h, win_flags );
	CHECK_OR_EXIT( s_sdl_window != nullptr, "SDL_CreateWindow failed" );

	auto const surface = xray::platform::native_surface_handle::from_sdl_window( s_sdl_window );
#if defined(USE_VULKAN_RENDERER)
	xray::render::vulkan::surface_adapter::register_sdl_window( surface, s_sdl_window );
#endif
	return surface;
}

xray::logging::log_file_usage	engine_world::log_file_usage ( ) const
{
	return xray::logging::log_file_usage::create_log;
}

void engine_world::initialize_core	( )
{
	pcstr debug_thread_id	= 0;
	if ( threading::g_debug_single_thread.is_set() )
		debug_thread_id		= "main";
	else if ( threading::core_count() == 1 ) {
		if ( command_line_editor() )
			debug_thread_id	= "editor + logic + render";
		else
			debug_thread_id	= "logic + render";
	}
	else
		debug_thread_id		= "render";

	pstr lua_config_device_folder_to_save_to = 0;
	STR_JOINA				( lua_config_device_folder_to_save_to, get_resources_path(), "/sources" );
	if ( !command_line_editor() )
		core::initialize	( lua_config_device_folder_to_save_to, debug_thread_id, core::perform_debug_initialization );
	else
		core::initialize	( lua_config_device_folder_to_save_to, debug_thread_id, core::delay_debug_initialization );
}

void engine_world::create_render	( )
{
	s_world					= this;

	m_render_world			= render::create_world (
		m_engine_user_module_proxy.allocator(),
		command_line_editor() ? &m_editor_allocator : 0
	);
	R_ASSERT				( m_render_world );
}

void engine_world::destroy_render	( )
{
	render::destroy_world	( m_render_world );
#if defined(USE_VULKAN_RENDERER)
	if ( background_render_capture() ) {
		auto result = xray::render::vulkan::diagnostics();
		std::cerr << "M8 validation errors=" << result.validation_errors << " warnings=" << result.validation_warnings << '\n';
		CHECK_OR_EXIT( result.validation_enabled && !result.validation_errors && !result.validation_warnings, "Background Vulkan validation failed" );
	}
#endif

	if ( s_sdl_window ) {
#if defined(USE_VULKAN_RENDERER)
		xray::render::vulkan::surface_adapter::unregister_sdl_window( m_render_window_handle );
#endif
		SDL_SetWindowRelativeMouseMode( s_sdl_window, false );
		SDL_DestroyWindow( s_sdl_window );
		s_sdl_window = nullptr;
	}
	xray::engine::capture_control_bus::clear_surface( m_render_window_handle );

	if ( s_sdl_owns_subs ) {
		SDL_QuitSubSystem( SDL_INIT_VIDEO | SDL_INIT_GAMEPAD );
		s_sdl_owns_subs = false;
	}

	ASSERT					( s_world );
	s_world					= 0;
}

void engine_world::show_window		( bool call_application_activate )
{
	if ( background_render_capture() )
		return;
	if ( m_editor ) {
#if XRAY_PLATFORM_WINDOWS
		if ( m_editor->main_handle().kind == xray::platform::native_surface_kind::win32_hwnd )
			SetForegroundWindow( static_cast<HWND>( m_editor->main_handle().as_win32_hwnd_raw() ) );
#endif
		return;
	}

	if ( s_sdl_window ) {
		SDL_ShowWindow( s_sdl_window );
		SDL_RaiseWindow( s_sdl_window );
		if ( call_application_activate ) {
			SDL_SetWindowRelativeMouseMode( s_sdl_window, true );
			on_application_activate( );
		}
		return;
	}

	if ( call_application_activate )
		on_application_activate	( );
}

bool engine_world::process_messages( )
{
	ASSERT					( !m_editor );

	if ( s_sdl_window ) {
		using namespace xray::input;
		SDL_Event ev;
		while ( SDL_PollEvent( &ev ) ) {
			switch ( ev.type ) {
				case SDL_EVENT_QUIT:
				case SDL_EVENT_WINDOW_CLOSE_REQUESTED:
					return false;

				case SDL_EVENT_WINDOW_FOCUS_GAINED:
					SDL_SetWindowRelativeMouseMode( s_sdl_window, true );
					on_application_activate();
					push_normalized_input_event( normalized_input_event{ normalized_event_kind::focus_gained } );
					break;

				case SDL_EVENT_WINDOW_FOCUS_LOST:
				case SDL_EVENT_WINDOW_MINIMIZED:
					SDL_SetWindowRelativeMouseMode( s_sdl_window, false );
					on_application_deactivate();
					push_normalized_input_event( normalized_input_event{ normalized_event_kind::focus_lost } );
					break;

				case SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
				case SDL_EVENT_WINDOW_RESIZED: {
#if defined(USE_VULKAN_RENDERER)
					int pixel_w = 0, pixel_h = 0;
					if ( SDL_GetWindowSizeInPixels( s_sdl_window, &pixel_w, &pixel_h ) && pixel_w > 0 && pixel_h > 0 ) {
						if ( auto* backend = dynamic_cast<xray::render::vulkan::vulkan_backend*>(
								xray::render::rhi::rhi_device::ref().get_backend() ) ) {
							backend->resize_swapchain( static_cast<u32>( pixel_w ), static_cast<u32>( pixel_h ) );
						}
					}
#endif
					break;
				}

				case SDL_EVENT_KEY_DOWN:
				case SDL_EVENT_KEY_UP:
					if ( !ev.key.repeat ) {
						normalized_input_event ne;
						ne.kind = ( ev.type == SDL_EVENT_KEY_DOWN ) ? normalized_event_kind::key_down : normalized_event_kind::key_up;
						ne.code = static_cast<s32>( ev.key.scancode );
						push_normalized_input_event( ne );
					}
					break;

				case SDL_EVENT_MOUSE_MOTION: {
					normalized_input_event ne;
					ne.kind = normalized_event_kind::mouse_motion;
					ne.x = ev.motion.xrel;
					ne.y = ev.motion.yrel;
					push_normalized_input_event( ne );
					break;
				}

				case SDL_EVENT_MOUSE_BUTTON_DOWN:
				case SDL_EVENT_MOUSE_BUTTON_UP: {
					xray::input::mouse_button btn = xray::input::mouse_button_left;
					switch ( ev.button.button ) {
						case SDL_BUTTON_LEFT:   btn = xray::input::mouse_button_left; break;
						case SDL_BUTTON_RIGHT:  btn = xray::input::mouse_button_right; break;
						case SDL_BUTTON_MIDDLE: btn = xray::input::mouse_button_middle; break;
						case SDL_BUTTON_X1:     btn = xray::input::mouse_button_extended0; break;
						case SDL_BUTTON_X2:     btn = xray::input::mouse_button_extended1; break;
						default:                btn = static_cast<xray::input::mouse_button>( 0 ); break;
					}
					if ( btn != 0 ) {
						normalized_input_event ne;
						ne.kind = ev.button.down ? normalized_event_kind::mouse_button_down : normalized_event_kind::mouse_button_up;
						ne.code = static_cast<s32>( btn );
						push_normalized_input_event( ne );
					}
					break;
				}

				case SDL_EVENT_MOUSE_WHEEL: {
					normalized_input_event ne;
					ne.kind = normalized_event_kind::mouse_wheel;
					ne.z = ev.wheel.y * 120.f;
					push_normalized_input_event( ne );
					break;
				}

				case SDL_EVENT_GAMEPAD_ADDED: {
					normalized_input_event ne;
					ne.kind = normalized_event_kind::gamepad_added;
					ne.device_id = static_cast<u32>( ev.gdevice.which );
					push_normalized_input_event( ne );
					break;
				}

				case SDL_EVENT_GAMEPAD_REMOVED: {
					normalized_input_event ne;
					ne.kind = normalized_event_kind::gamepad_removed;
					ne.device_id = static_cast<u32>( ev.gdevice.which );
					push_normalized_input_event( ne );
					break;
				}

				default:
					break;
			}
		}
		return true;
	}

	return true;
}

void engine_world::run				( )
{
	if ( m_early_destruction_started )
		return;

	if ( m_editor && ( threading::core_count( ) == 1 ) ) {
		m_editor->run		( );

		if ( !m_destruction_started )
			exit			( 0 );

		return;
	}

	for (;;) {
		if ( !m_editor && !process_messages() )
			return;

		if ( m_destruction_started )
			return;

		tick				( );
	}
}

pcstr engine_world::get_resources_path	( ) const
{
	if ( auto path = prepared_render_assets() )
		return path;
	return "../../resources";
}

pcstr engine_world::get_mounts_path		( ) const
{
	if ( prepared_render_assets() )
		return nullptr; // explicit physical mounts below
	return "../../mounts";
}

bool engine_world::background_render_capture() const { return s_render_capture.is_set(); }

pcstr engine_world::prepared_render_assets() const {
	if ( s_render_assets.is_set() ) {
		static xray::fixed_string<512> const path = [] {
			xray::fixed_string<512> value;
			CHECK_OR_EXIT( s_render_assets.is_set_as_string( &value ) && !value.empty(), "render_assets requires a prepared root path" );
			return value;
		}();
		return path.c_str();
	}
	if ( !const_cast<engine_world*>( this )->command_line_editor() )
		return nullptr;
	static xray::fixed_string<512> const packaged_path = [] {
		xray::fixed_string<512> value;
		auto const exe_dir = xray::os::executable_directory();
		if ( !exe_dir.empty() ) {
			std::error_code ec;
			auto candidate = exe_dir.parent_path() / "assets";
			if ( std::filesystem::is_regular_file( candidate / "scene.json", ec )
				&& std::filesystem::is_directory( candidate / "resources", ec ) ) {
				auto utf8 = candidate.lexically_normal().generic_u8string();
				std::string resolved( reinterpret_cast<char const*>( utf8.data() ), utf8.size() );
				if ( resolved.size() < value.fixed_size )
					value.assignf( "%s", resolved.c_str() );
			}
		}
		return value;
	}();
	return packaged_path.empty() ? nullptr : packaged_path.c_str();
}

void engine_world::capture_background_frame() {
#if defined(USE_VULKAN_RENDERER)
	if ( !background_render_capture() || m_destruction_started )
		return;

	auto const window = m_render_window_handle;
	if ( xray::engine::capture_control_bus::has_flag( window, xray::engine::m8_outputs::mode_property ) ) {
		try {
			xray::fixed_string<512> path;
			if ( !s_render_capture.is_set_as_string( &path ) || path.empty() )
				throw std::runtime_error( "Output workflow requires capture directory" );
			xray::engine::m8_outputs::capture( window, m_render_world->engine_renderer().frame_id(), path.c_str() );
		} catch ( std::exception const& error ) {
			std::cerr << "M8 outputs capture failed: " << error.what() << '\n';
			exit( 2 );
		}
		return;
	}

	CHECK_OR_EXIT( xray::engine::m8_outputs::background_window_hidden( window ), "Background renderer acquired input/visibility" );
	namespace protocol = xray::engine::m8_control;
	if ( xray::engine::m9_performance::current ) {
		try {
			auto* backend = dynamic_cast<xray::render::vulkan::vulkan_backend*>( xray::render::rhi::rhi_device::ref().get_backend() );
			if ( !backend || backend->output()->surface_handle != window )
				throw std::runtime_error( "Performance output owner mismatch" );
			if ( xray::engine::m9_performance::sample( *backend ) )
				exit( 0 );
		} catch ( std::exception const& error ) {
			std::cerr << "M9 performance failed: " << error.what() << '\n';
			exit( 2 );
		}
		return;
	}

	auto request = xray::engine::capture_control_bus::get_token( window, protocol::request_property );
	if ( request && request == xray::engine::capture_control_bus::get_token( window, protocol::ack_property ) )
		return;
	auto target_frame = request
		? protocol::token_frame( request )
		: xray::engine::capture_control_bus::get_token( window, "XRAY_VULKAN_CONTROL_CAPTURE_FRAME" );
	if ( !target_frame )
		return;
	auto frame_id = m_render_world->engine_renderer().frame_id();
	if ( frame_id < target_frame )
		return;
	try {
		if ( frame_id != target_frame )
			throw std::runtime_error( "Missed the fixed control capture frame" );
		xray::fixed_string<512> path;
		if ( !s_render_capture.is_set_as_string( &path ) || path.empty() )
			throw std::runtime_error( "render_capture requires a directory" );
		auto directory = std::filesystem::absolute( path.c_str() );
		std::filesystem::create_directories( directory );
		auto label = request ? protocol::name( protocol::token_shot( request ) ) : "game";
		if ( !label )
			throw std::runtime_error( "Unknown control capture shot" );
		auto target = directory / ( std::string( label ) + ".bmp" );
		if ( std::filesystem::exists( target ) )
			throw std::runtime_error( "Capture target already exists" );
		auto* backend = dynamic_cast<xray::render::vulkan::vulkan_backend*>( xray::render::rhi::rhi_device::ref().get_backend() );
		if ( !backend || backend->output()->surface_handle != window )
			throw std::runtime_error( "Background output owner mismatch" );
		auto frame = backend->capture_frame();
		xray::engine::m8_outputs::write_image( target, frame );

		std::string sdk_test_env;
		if ( request && xray::os::get_environment_variable( "XRAY_SDK_PROPERTY_TEST", sdk_test_env ) && sdk_test_env == "1" ) {
			for ( auto const& entry : { std::pair{ "XRAY_SDK_CAMERA_OUTPUT", "camera" }, std::pair{ "XRAY_SDK_ASSET_OUTPUT", "asset" } } ) {
				auto const sub_surface = xray::engine::capture_control_bus::get_surface( window, entry.first );
				if ( sub_surface.valid() ) {
					auto primary = backend->output();
					auto outputs = backend->outputs();
					auto found = std::find_if( outputs.begin(), outputs.end(), [&]( auto const& value ) {
						return value->surface_handle == sub_surface;
					} );
					if ( found == outputs.end() || *found == primary || !( *found )->presented_frames )
						throw std::runtime_error( "SDK preview has no independent presented output" );
					backend->select_output( *found );
					auto image = backend->capture_frame();
					backend->select_output( primary );
					auto destination = directory / ( std::string( label ) + "_" + entry.second + ".bmp" );
					xray::engine::m8_outputs::write_image( destination, image );
					std::cerr << "SDK output capture: kind=" << entry.second << " size=" << image.width << 'x' << image.height
						<< " presented=" << ( *found )->presented_frames << '\n';
				}
			}
		}
		if ( !request ) {
			auto const preview = xray::engine::capture_control_bus::get_surface( window, protocol::preview_property );
			if ( preview.valid() ) {
				if ( !xray::engine::m8_outputs::background_window_hidden( preview ) )
					throw std::runtime_error( "Background preview acquired input/visibility" );
				auto primary = backend->output();
				auto outputs = backend->outputs();
				auto found = std::find_if( outputs.begin(), outputs.end(), [&]( auto const& output ) {
					return output->surface_handle == preview;
				} );
				if ( found == outputs.end() || *found == primary || !( *found )->presented_frames )
					throw std::runtime_error( "Preview has no independent presented output" );
				backend->select_output( *found );
				auto image = backend->capture_frame();
				backend->select_output( primary );
				auto preview_target = directory / "preview.bmp";
				xray::engine::m8_outputs::write_image( preview_target, image );
				std::cerr << "M8 model preview capture: " << image.width << 'x' << image.height
					<< " independent_surface=1 frame=" << ( *found )->presented_frames << '\n';
			}
		}
		std::cerr << "M8 game capture: " << frame.width << 'x' << frame.height
			<< " render_thread=" << xray::threading::current_thread_id();
		if ( request ) {
			std::cerr << " shot=" << label << " frame=" << frame_id << '\n';
			if ( !xray::engine::capture_control_bus::set_token( window, protocol::ack_property, request ) )
				throw std::runtime_error( "Cannot acknowledge workflow capture" );
		} else {
			std::cerr << " content_frame=16 actor_time_ms=240\n";
			if ( s_render_performance.is_set() ) {
				xray::fixed_string<64> durations;
				if ( !s_render_performance.is_set_as_string( &durations ) )
					throw std::runtime_error( "Performance requires warmup,stress seconds" );
				xray::engine::m9_performance::start( *backend, window, directory, durations.c_str() );
				xray::engine::capture_control_bus::remove( window, "XRAY_VULKAN_CONTROL_CAPTURE_FRAME" );
			} else {
				exit( 0 );
			}
		}
	} catch ( std::exception const& error ) {
		std::cerr << "M8 capture failed: " << error.what() << '\n';
		exit( 2 );
	}
#endif
}

void engine_world::enable_game						( bool value )
{
	enable_game_impl	( value );
}

xray::math::float2 engine_world::get_render_window_size( ) const
{
#if defined(USE_VULKAN_RENDERER)
	u32 w = 0, h = 0;
	if ( xray::render::vulkan::surface_adapter::query_surface_extent( m_render_window_handle, w, h ) && w > 0 && h > 0 )
		return math::float2( float( w ), float( h ) );
#endif
	return math::float2( 10.f, 10.f );
}
