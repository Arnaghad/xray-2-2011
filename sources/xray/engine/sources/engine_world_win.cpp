////////////////////////////////////////////////////////////////////////////
//	Created 	: 21.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/os_preinclude.h>
#include <windows.h>
#include <winuser.h>
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
#include <objbase.h>				// for COINIT_MULTITHREADED
#if defined(USE_VULKAN_RENDERER)
#include <xray/render/vulkan/platform_surface.h>
#include <xray/render/vulkan/sources/vulkan_backend.h>
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/world.h>
#include <xray/render/vulkan/diagnostics.h>
#include <xray/engine/m8_control_protocol.h>
#include "m8_output_capture.h"
#include "m9_performance_capture.h"
#include <filesystem>
#include <fstream>
#include <iostream>
#endif

#pragma comment( lib, "delayimp.lib" )

static xray::command_line::key	bhwWindowed("windowed", "", "", "windowed mode");
static xray::command_line::key s_render_assets("render_assets", "", "render", "prepared resource root (resources and resources.converted)");
static xray::command_line::key s_render_capture("render_capture", "", "render", "hidden nonactivating capture directory; bounded control-level run");
static xray::command_line::key s_render_performance("render_performance", "", "render", "background manifest performance: warmup,stress seconds");

using xray::engine::engine_world;

static pcstr s_window_id					= XRAY_ENGINE_ID " DX10 Renderer Window";
static pcstr s_window_class_id				= XRAY_ENGINE_ID " DX10 Renderer Window Class ID";

xray::command_line::key	s_no_log_file		("no_log_file", "no_log_file", "logging", "disables writing log to a file", "<no arguments>");

static WNDCLASSEX s_window_class;

static xray::engine::engine_world*			s_world			= 0;
static SDL_Window*							s_sdl_window	= nullptr;
static bool									s_sdl_owns_subs	= false;

static LRESULT APIENTRY message_processor	( HWND window_handle, UINT message_id, WPARAM w_param, LPARAM l_param )
{
	// Creation messages arrive before create_render installs s_world. Capture
	// windows never activate input or change the user's cursor visibility.
	if (s_render_capture.is_set() && message_id==WM_MOUSEACTIVATE) return MA_NOACTIVATE;
	if (message_id==WM_ACTIVATE && (!s_world || s_render_capture.is_set())) return 0;
	switch ( message_id ) {
		case WM_CLOSE : {
			if ( s_world && s_world->is_destruction_started() )
				break;

			return			1;
		}
		case WM_DESTROY: {
			PostQuitMessage	( 0 );

			return			( 0 );
		}
		case WM_ACTIVATE: {

			u16 fActive						= LOWORD(w_param);
			BOOL fMinimized					= (BOOL) HIWORD(w_param);
			bool const active				= ((fActive!=WA_INACTIVE) && (!fMinimized)) ? true : false;

			if (active)
			{
				s_world->on_application_activate( );
				while (	ShowCursor( FALSE ) >= 0 );
			}else
			{
				s_world->on_application_deactivate( );
				while (	ShowCursor( TRUE ) < 0 );
			}
			//if ( (w_param == WA_ACTIVE) || (w_param == WA_CLICKACTIVE) ) {
			//	if ( !s_world->editor_world() )
			//		while (	ShowCursor( FALSE ) >= 0 );

			//	s_world->on_application_activate( );
			//	
			//	break;
			//}

			//ASSERT			( w_param == WA_INACTIVE );
			//if ( !s_world->editor_world() )
			//	while (	ShowCursor( TRUE ) < 0 );

			break;
		}
	}
	
	return					( DefWindowProc( window_handle, message_id, w_param, l_param ) );
}

HWND new_window			( )
{
	u32 const screen_size_x	= GetSystemMetrics( SM_CXSCREEN );
	u32 const screen_size_y	= GetSystemMetrics( SM_CYSCREEN );
	if (s_render_capture.is_set()) {
		WNDCLASSEX const temp = {
			sizeof( WNDCLASSEX ), CS_CLASSDC, &message_processor, 0L, 0L,
			GetModuleHandle( 0 ), nullptr, nullptr, nullptr, nullptr, s_window_class_id, nullptr
		};
		s_window_class = temp;
		RegisterClassEx( &s_window_class );
		HWND result=CreateWindowExA(WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW|WS_EX_LAYERED|WS_EX_TRANSPARENT,
			s_window_class_id,s_window_id,WS_POPUP,0,0,960,640,nullptr,nullptr,s_window_class.hInstance,nullptr);
		CHECK_OR_EXIT(result && SetLayeredWindowAttributes(result,0,0,LWA_ALPHA)
			&& SetPropA(result,"XRAY_VULKAN_TEST_CAPTURE",HANDLE(1)),"Cannot create background render window");
		ShowWindow(result,SW_HIDE);
		return result;
	}

	if ( SDL_InitSubSystem( SDL_INIT_VIDEO | SDL_INIT_GAMEPAD ) ) {
		s_sdl_owns_subs = true;
		int win_w = static_cast<int>( screen_size_x );
		int win_h = static_cast<int>( screen_size_y );
		SDL_WindowFlags win_flags = SDL_WINDOW_VULKAN | SDL_WINDOW_HIDDEN;

		if ( bhwWindowed || ( s_world && s_world->command_line_editor() ) ) {
			u32 const window_sizes_x[] = { 1024, 800, 640 };
			u32 const window_sizes_y[] = { 768, 600, 480 };
			for ( u32 i = 0; i < xray::array_size(window_sizes_x); ++i ) {
				if ( window_sizes_x[i] < screen_size_x && window_sizes_y[i] < screen_size_y ) {
					win_w = static_cast<int>( window_sizes_x[i] );
					win_h = static_cast<int>( window_sizes_y[i] );
					break;
				}
			}
			win_flags |= SDL_WINDOW_RESIZABLE;
		} else {
			win_flags |= SDL_WINDOW_FULLSCREEN;
		}

		s_sdl_window = SDL_CreateWindow( s_window_id, win_w, win_h, win_flags );
		if ( s_sdl_window ) {
			SDL_PropertiesID const props = SDL_GetWindowProperties( s_sdl_window );
			HWND const native_hwnd = static_cast<HWND>(
				SDL_GetPointerProperty( props, SDL_PROP_WINDOW_WIN32_HWND_POINTER, nullptr )
			);
			R_ASSERT( native_hwnd );
#if defined(USE_VULKAN_RENDERER)
			xray::render::vulkan::surface_adapter::register_sdl_window( native_hwnd, s_sdl_window );
#endif
			return native_hwnd;
		}
		LOG_WARNING( "SDL_CreateWindow failed (%s), falling back to Win32 HWND", SDL_GetError() );
	} else {
		LOG_WARNING( "SDL_InitSubSystem(VIDEO|GAMEPAD) failed (%s), falling back to Win32 HWND", SDL_GetError() );
	}

	WNDCLASSEX const temp	=
	{
		sizeof( WNDCLASSEX ),
		CS_CLASSDC,
		&message_processor,
		0L,
		0L, 
		GetModuleHandle( 0 ),
		nullptr,
		nullptr,
		nullptr,
		nullptr,
		s_window_class_id,
		nullptr
	};

	s_window_class			= temp;
	RegisterClassEx			( &s_window_class );

	HWND result;

	if ( bhwWindowed || s_world && s_world->command_line_editor() ) {
		DWORD const	window_style = WS_OVERLAPPED;// | WS_CAPTION;

		u32 window_size_x		= 0;
		u32 window_size_y		= 0;

		u32 const window_sizes_x []	= { 1024, 800, 640 };
		u32 const window_sizes_y []	= { 768, 600, 480 };
		for ( u32 i=0; i<xray::array_size(window_sizes_x); ++i ) {
			if ( window_sizes_x[i] < screen_size_x && 
				window_sizes_y[i] < screen_size_y )
			{
				window_size_x	= window_sizes_x[i];
				window_size_y	= window_sizes_y[i];
				break;
			}
		}

		R_ASSERT				(window_size_x);

		RECT window_size		= { 0, 0, (LONG)window_size_x, (LONG)window_size_y };

		AdjustWindowRect(&window_size, window_style, false);

		result =
			CreateWindow (
			s_window_class_id,
			s_window_id,
			window_style,
			( screen_size_x - window_size.right ) / 2,
			( screen_size_y - window_size.bottom ) / 2,
			window_size.right - window_size.left,
			window_size.bottom - window_size.top,
			GetDesktopWindow( ),
			0,
			s_window_class.hInstance,
			0
			);
	} else {

		DWORD const	window_style = WS_EX_TOPMOST | WS_POPUP;

		result =
			CreateWindow(
			s_window_class_id,
			s_window_id,
			window_style,
			CW_USEDEFAULT,
			CW_USEDEFAULT,
			screen_size_x,
			screen_size_y,
			GetDesktopWindow(),
			0,
			s_window_class.hInstance,
			0
			);
	}

	R_ASSERT				( result );

	return					result;
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
	if (background_render_capture()) {
		auto result=xray::render::vulkan::diagnostics();
		std::cerr<<"M8 validation errors="<<result.validation_errors<<" warnings="<<result.validation_warnings<<'\n';
		CHECK_OR_EXIT(result.validation_enabled && !result.validation_errors && !result.validation_warnings,"Background Vulkan validation failed");
	}
#endif

	if ( s_sdl_window ) {
#if defined(USE_VULKAN_RENDERER)
		xray::render::vulkan::surface_adapter::unregister_sdl_window( m_render_window_handle );
#endif
		SDL_SetWindowRelativeMouseMode( s_sdl_window, false );
		SDL_DestroyWindow( s_sdl_window );
		s_sdl_window = nullptr;
	} else {
		ShowWindow				( m_main_window_handle, SW_HIDE );
	}

	if ( s_sdl_owns_subs ) {
		SDL_QuitSubSystem( SDL_INIT_VIDEO | SDL_INIT_GAMEPAD );
		s_sdl_owns_subs = false;
	}

	ASSERT					( s_world );
	s_world					= 0;
}

void engine_world::show_window		( bool call_application_activate )
{
	if (background_render_capture()) return;
	if ( m_editor ) {
		SetForegroundWindow	( m_editor->main_handle( ) );
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

	ShowWindow				( m_main_window_handle, SW_SHOW );
	SetForegroundWindow		( m_main_window_handle );

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

	MSG						message;
	while ( PeekMessage( &message, 0, 0, 0, PM_REMOVE ) ) {
		TranslateMessage	( &message );
		DispatchMessage		( &message );

		if ( message.message == WM_QUIT )
			return			( false );
	}

	return					( true );
}

void engine_world::run				( )
{
	if ( m_early_destruction_started )
		return;

//	m_timer.start			( );

	if ( m_editor && (threading::core_count( ) == 1) ) {
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
	if (auto path=prepared_render_assets()) return path;
	return								"../../resources";
}

pcstr engine_world::get_mounts_path		( ) const
{
	if (prepared_render_assets()) return nullptr; // explicit physical mounts below
	return								"../../mounts";
}

bool engine_world::background_render_capture() const {return s_render_capture.is_set();}

pcstr engine_world::prepared_render_assets() const {
	if (s_render_assets.is_set()) {
		static xray::fixed_string<512> const path=[] {
			xray::fixed_string<512> value;
			CHECK_OR_EXIT(s_render_assets.is_set_as_string(&value) && !value.empty(),"render_assets requires a prepared root path");
			return value;
		}();
		return path.c_str();
	}
	if (!const_cast<engine_world*>(this)->command_line_editor()) return nullptr;
	static xray::fixed_string<512> const packaged_path=[] {
		xray::fixed_string<512> value;
		wchar_t executable[32768]{};
		DWORD length=GetModuleFileNameW(nullptr,executable,32768);
		if (length>0 && length<32768) {
			std::error_code ec;
			auto candidate=std::filesystem::path(executable).parent_path().parent_path()/L"assets";
			if (std::filesystem::is_regular_file(candidate/L"scene.json",ec) && std::filesystem::is_directory(candidate/L"resources",ec)) {
				auto utf8=candidate.lexically_normal().generic_u8string();
				std::string resolved(reinterpret_cast<char const*>(utf8.data()),utf8.size());
				if (resolved.size()<value.fixed_size) value.assignf("%s",resolved.c_str());
			}
		}
		return value;
	}();
	return packaged_path.empty() ? nullptr : packaged_path.c_str();
}

void engine_world::capture_background_frame() {
#if defined(USE_VULKAN_RENDERER)
	if (!background_render_capture() || m_destruction_started) return;
	HWND window=m_render_window_handle;
	if(GetPropA(window,xray::engine::m8_outputs::mode_property)) {
		try {xray::fixed_string<512> path;
			if(!s_render_capture.is_set_as_string(&path) || path.empty())throw std::runtime_error("Output workflow requires capture directory");
			xray::engine::m8_outputs::capture(window,m_render_world->engine_renderer().frame_id(),path.c_str());
		} catch(std::exception const& error){std::cerr<<"M8 outputs capture failed: "<<error.what()<<'\n';exit(2);}
		return;
	}
	CHECK_OR_EXIT(!IsWindowVisible(window) && GetForegroundWindow()!=window && GetActiveWindow()!=window
		&& GetFocus()!=window && GetCapture()!=window,"Background renderer acquired input/visibility");
	namespace protocol=xray::engine::m8_control;
    if (xray::engine::m9_performance::current) {
        try {
            auto* backend=dynamic_cast<xray::render::vulkan::vulkan_backend*>(xray::render::rhi::rhi_device::ref().get_backend());
            if(!backend || backend->output()->hwnd!=window)throw std::runtime_error("Performance output owner mismatch");
            if(xray::engine::m9_performance::sample(*backend))exit(0);
        } catch(std::exception const& error) {std::cerr<<"M9 performance failed: "<<error.what()<<'\n';exit(2);}
        return;
    }
	auto request=reinterpret_cast<uintptr_t>(GetPropA(window,protocol::request_property));
	if (request && request==reinterpret_cast<uintptr_t>(GetPropA(window,protocol::ack_property))) return;
	auto target_frame=request ? protocol::token_frame(request) : reinterpret_cast<uintptr_t>(GetPropA(window,"XRAY_VULKAN_CONTROL_CAPTURE_FRAME"));
	if (!target_frame) return;
	auto frame_id=m_render_world->engine_renderer().frame_id();
	if (frame_id<target_frame) return;
	try {
		if (frame_id!=target_frame) throw std::runtime_error("Missed the fixed control capture frame");
		xray::fixed_string<512> path;
		if (!s_render_capture.is_set_as_string(&path) || path.empty()) throw std::runtime_error("render_capture requires a directory");
		auto directory=std::filesystem::absolute(path.c_str());
		std::filesystem::create_directories(directory);
		auto label=request ? protocol::name(protocol::token_shot(request)) : "game";
		if (!label) throw std::runtime_error("Unknown control capture shot");
		auto target=directory/(std::string(label)+".bmp");
		if (std::filesystem::exists(target)) throw std::runtime_error("Capture target already exists");
		auto* backend=dynamic_cast<xray::render::vulkan::vulkan_backend*>(xray::render::rhi::rhi_device::ref().get_backend());
		if (!backend || backend->output()->hwnd!=window) throw std::runtime_error("Background output owner mismatch");
		auto frame=backend->capture_frame();
		BITMAPFILEHEADER file={};BITMAPINFOHEADER info={};
		file.bfType=0x4d42;file.bfOffBits=sizeof(file)+sizeof(info);file.bfSize=DWORD(file.bfOffBits+frame.bgra.size());
		info.biSize=sizeof(info);info.biWidth=LONG(frame.width);info.biHeight=-LONG(frame.height);info.biPlanes=1;info.biBitCount=32;
		std::ofstream out(target,std::ios::binary);
		out.write(reinterpret_cast<char*>(&file),sizeof(file));out.write(reinterpret_cast<char*>(&info),sizeof(info));
		out.write(reinterpret_cast<char const*>(frame.bgra.data()),frame.bgra.size());out.close();
		if (!out) throw std::runtime_error("Background capture write failed");
        char sdk_capture[2]{};
        if(request && GetEnvironmentVariableA("XRAY_SDK_PROPERTY_TEST",sdk_capture,2)==1 && sdk_capture[0]=='1') {
            for(auto const& entry:{std::pair{"XRAY_SDK_CAMERA_OUTPUT","camera"},std::pair{"XRAY_SDK_ASSET_OUTPUT","asset"}})if(auto hwnd=reinterpret_cast<HWND>(GetPropA(window,entry.first))) {
                auto primary=backend->output();auto outputs=backend->outputs();auto found=std::find_if(outputs.begin(),outputs.end(),[&](auto const& value){return value->hwnd==hwnd;});
                if(found==outputs.end() || *found==primary || !(*found)->presented_frames)throw std::runtime_error("SDK preview has no independent presented output");
                backend->select_output(*found);auto image=backend->capture_frame();backend->select_output(primary);
                auto destination=directory/(std::string(label)+"_"+entry.second+".bmp");if(std::filesystem::exists(destination))throw std::runtime_error("SDK output capture already exists");
                file.bfSize=DWORD(file.bfOffBits+image.bgra.size());info.biWidth=LONG(image.width);info.biHeight=-LONG(image.height);
                std::ofstream stream(destination,std::ios::binary);stream.write(reinterpret_cast<char*>(&file),sizeof(file));stream.write(reinterpret_cast<char*>(&info),sizeof(info));
                stream.write(reinterpret_cast<char const*>(image.bgra.data()),image.bgra.size());stream.close();if(!stream)throw std::runtime_error("SDK output capture failed");
                std::cerr<<"SDK output capture: kind="<<entry.second<<" size="<<image.width<<'x'<<image.height<<" presented="<<(*found)->presented_frames<<'\n';
            }
        }
		if(!request)if(auto preview=reinterpret_cast<HWND>(GetPropA(window,protocol::preview_property))) {
			if(!IsWindow(preview) || IsWindowVisible(preview) || GetForegroundWindow()==preview || GetActiveWindow()==preview
				|| GetFocus()==preview || GetCapture()==preview)throw std::runtime_error("Background preview acquired input/visibility");
			auto primary=backend->output();auto outputs=backend->outputs();
			auto found=std::find_if(outputs.begin(),outputs.end(),[&](auto const& output){return output->hwnd==preview;});
			if(found==outputs.end() || *found==primary || !(*found)->presented_frames)throw std::runtime_error("Preview has no independent presented output");
			backend->select_output(*found);auto image=backend->capture_frame();backend->select_output(primary);
			auto preview_target=directory/"preview.bmp";if(std::filesystem::exists(preview_target))throw std::runtime_error("Preview capture already exists");
			file.bfSize=DWORD(file.bfOffBits+image.bgra.size());info.biWidth=LONG(image.width);info.biHeight=-LONG(image.height);
			std::ofstream preview_out(preview_target,std::ios::binary);
			preview_out.write(reinterpret_cast<char*>(&file),sizeof(file));preview_out.write(reinterpret_cast<char*>(&info),sizeof(info));
			preview_out.write(reinterpret_cast<char const*>(image.bgra.data()),image.bgra.size());preview_out.close();
			if(!preview_out)throw std::runtime_error("Preview capture write failed");
			std::cerr<<"M8 model preview capture: "<<image.width<<'x'<<image.height<<" independent_surface=1 frame="<<(*found)->presented_frames<<'\n';
		}
		std::cerr<<"M8 game capture: "<<frame.width<<'x'<<frame.height<<" render_thread="<<xray::threading::current_thread_id();
		if (request) {
			std::cerr<<" shot="<<label<<" frame="<<frame_id<<'\n';
			if (!SetPropA(window,protocol::ack_property,reinterpret_cast<HANDLE>(request))) throw std::runtime_error("Cannot acknowledge workflow capture");
		} else {
            std::cerr<<" content_frame=16 actor_time_ms=240\n";
            if(s_render_performance.is_set()) {
                xray::fixed_string<64> durations;
                if(!s_render_performance.is_set_as_string(&durations))throw std::runtime_error("Performance requires warmup,stress seconds");
                xray::engine::m9_performance::start(*backend,window,directory,durations.c_str());
                RemovePropA(window,"XRAY_VULKAN_CONTROL_CAPTURE_FRAME");
            } else exit(0);
        }
	} catch (std::exception const& error) {std::cerr<<"M8 capture failed: "<<error.what()<<'\n';exit(2);}
#endif
}

void engine_world::enable_game						( bool value )
{
	enable_game_impl	( value );
}

xray::math::float2 engine_world::get_render_window_size( ) const
{
	RECT			rect;
	if ( GetClientRect( m_render_window_handle, &rect) )
		return		math::float2(float(rect.right-rect.left), float(rect.bottom-rect.top) );

	return			math::float2( 10, 10 );
}
