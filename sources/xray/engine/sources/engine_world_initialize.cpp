////////////////////////////////////////////////////////////////////////////
//	Created		: 02.12.2010
//	Author		: Tetyana Meleshchenko
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "engine_world.h"
#include "apc.h"
#include <xray/core/core.h>
#include <xray/sound/world.h>
#include <xray/sound/world_user.h>
#include <xray/engine_test_suite.h>
#include <xray/render/facade/engine_renderer.h>
#include <xray/network/api.h>
#include <xray/sound/api.h>
#include <xray/editor/world/world.h>
#include <xray/render/world.h>
#include <xray/collision/api.h>
#include <xray/core_entry_point.h>

static xray::command_line::key			s_build_resources		("build_resources", "", "application", "runs engine in build resources mode");

using xray::engine::engine_world;

static void on_mounted_resources		( xray::resources::mount_ptr * out_mount, xray::resources::mount_ptr result )
{
	CHECK_OR_EXIT						( result, "Cannot mount resources. Please reinstall an application and try again." );
	* out_mount							= result;
}

static void on_mounted_user_data		( xray::resources::mount_ptr * out_mount, xray::resources::mount_ptr result )
{
	CHECK_OR_EXIT						( result, "Cannot mount user_data folder. Please reinstall an application and try again." );
	* out_mount							= result;
}

void on_tests_resources_mount_completed ( bool *						out_completed, 
										  xray::resources::mount_ptr *	out_mount, 
										  xray::resources::mount_ptr	result )
{
	R_ASSERT_U								(result, "cannot mount test resources path!");
	* out_completed						=	true;
	* out_mount							=	result;
}

void mount_tests_resources				( xray::resources::mount_ptr * out_mount, pcstr resources_path )
{
	using namespace xray;
	fs_new::native_path_string				test_resources_path;
	test_resources_path.assignf_with_conversion	("%s/tests", resources_path, "tests");

	bool completed						=	false;
	resources::query_mount_physical			( "resources/tests", test_resources_path.c_str(), "tests", fs_new::watcher_enabled_true,
 										 	  std::bind(& on_tests_resources_mount_completed, & completed, out_mount, std::placeholders::_1), 
											  ::xray::memory::g_mt_allocator, resources::recursive_false );

	while ( !completed )
	{
		if ( threading::g_debug_single_thread.is_set() )
			resources::tick				( );
		resources::dispatch_callbacks	( );
	}
}

void engine_world::initialize			( )
{
	ASSERT								( !m_initialized );
	m_initialized						= true;

	initialize_core						( );

	apc::initialize						( );
	initialize_file_system_devices		( );
	apc::run							( apc::hdd,	[]() { return apc::empty::function(); }, apc::break_process_loop, apc::wait_for_completion);
	apc::run							( apc::dvd,	[]() { return apc::empty::function(); }, apc::break_process_loop, apc::wait_for_completion);

	initialize_resources				( );

	collision::initialize				( );
	initialize_terminate_on_timeout		( );

	core::run_tests						( );

	if ( m_destruction_started )
	{
		m_early_destruction_started		= true;
		return;
	}

	if ( testing::run_tests_command_line() )
		mount_tests_resources			( & m_test_resources_mount, get_resources_path( ) );

	resources::query_mount_physical		( "user_data", 
										   core::user_data_directory( ), 
										   "user_data",
										   fs_new::watcher_enabled_false, 
										   std::bind(& on_mounted_user_data, & m_user_data_mount, std::placeholders::_1), 
										   g_allocator, 
										   resources::recursive_false );

	if (auto root=prepared_render_assets()) {
		fs_new::native_path_string source,converted;
		source.assignf_with_conversion("%s/resources",root);
		converted.assignf_with_conversion("%s/resources.converted",root);
		resources::query_mount_physical("resources",source,"prepared render assets",fs_new::watcher_enabled_false,
			std::bind(&on_mounted_resources,&m_resources_mount,std::placeholders::_1),g_allocator,resources::recursive_false);
		resources::query_mount_physical("resources.converted",converted,"prepared render configs",fs_new::watcher_enabled_false,
			std::bind(&on_mounted_resources,&m_converted_render_mount,std::placeholders::_1),g_allocator,resources::recursive_false);
		while (!m_resources_mount || !m_converted_render_mount) {resources::dispatch_callbacks();threading::yield(0);}
	} else resources::query_mount				( "resources", 
										   std::bind(& on_mounted_resources, & m_resources_mount, std::placeholders::_1), 
										   g_allocator );

	fixed_string<256>					build_resources_string;
	bool const is_build					= s_build_resources.is_set_as_string( &build_resources_string );
	if ( is_build )
		initialize_build				( build_resources_string.c_str() );

	bool const is_editor				= command_line_editor();
	if ( is_editor ) {
		initialize_editor				( );
		apc::run						( apc::editor,	[this]() { return try_load_editor(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	}

	initialize_sound					( );
	apc::run							( apc::sound,	[this]() { return initialize_sound_modules(); }, apc::continue_process_loop, apc::dont_wait_for_completion);

	initialize_network					( );
	apc::run							( apc::network,	[this]() { return initialize_network_modules(); }, apc::continue_process_loop, apc::dont_wait_for_completion);

	initialize_logic					( );
	apc::run							( apc::logic,	[this]() { return initialize_logic_thread(); }, apc::continue_process_loop, apc::dont_wait_for_completion);

	apc::wait							( apc::sound );
	R_ASSERT							( m_sound_world );

	// initialize sound users
	apc::run							( apc::logic,	std::bind(&sound::world_user::initialize, &m_sound_world->get_logic_world_user()), apc::continue_process_loop, apc::dont_wait_for_completion);
	
#ifndef MASTER_GOLD
	if ( is_editor )
		apc::run						( apc::editor,	std::bind(&sound::world_user::initialize, &m_sound_world->get_editor_world_user()), apc::continue_process_loop, apc::dont_wait_for_completion);
#endif //#ifndef MASTER_GOLD
	
	// no apc::run since we are in the render thread
	initialize_render					( );
	
#if defined(USE_VULKAN_RENDERER)
	CHECK_OR_EXIT(m_render_world->engine_renderer().initialize_game(m_render_window_handle), "Vulkan game renderer initialization failed; see Vulkan diagnostics.");
#else
	m_render_world->engine_renderer().initialize( );
#endif
	
	// initialize render users
	if ( is_editor )
		apc::run						( apc::editor,	std::bind(&render::one_way_render_channel::owner_initialize, &render_world().editor_channel() ), apc::continue_process_loop, apc::dont_wait_for_completion);
	apc::run							( apc::logic,	std::bind(&render::one_way_render_channel::owner_initialize, &render_world().logic_channel() ), apc::continue_process_loop, apc::wait_for_completion);

	// Creating the game world immediately queries scenes and can publish facade
	// commands. Renderer/cooks and both ends of its channel must already be ready.
	apc::run							( apc::logic,	[this]() { return initialize_logic_modules(); }, apc::continue_process_loop, apc::dont_wait_for_completion);

	apc::run							( apc::build,	[]() { return apc::empty::function(); }, apc::break_process_loop, apc::dont_wait_for_completion);
	apc::run							( apc::network,	[]() { return apc::empty::function(); }, apc::break_process_loop, apc::dont_wait_for_completion);
	apc::run							( apc::sound,	[]() { return apc::empty::function(); }, apc::break_process_loop, apc::dont_wait_for_completion);
	apc::run							( apc::logic,	[]() { return apc::empty::function(); }, apc::break_process_loop, apc::dont_wait_for_completion);

	if ( is_editor ) {
//		if ( s_build_resources.is_set() )
			apc::run					( apc::editor,	[]() { return apc::empty::function(); }, apc::break_process_loop, apc::dont_wait_for_completion);
//		else
//			apc::run					( apc::editor,	std::bind( &engine_world::show_window, this, true), apc::break_process_loop, apc::dont_wait_for_completion );
	}
	else
		show_window						( true );

#ifndef XRAY_STATIC_LIBRARIES
	command_line::check_keys			( );
	command_line::handle_help_key		( );
#endif // #ifndef XRAY_STATIC_LIBRARIES
	
	if ( m_editor || s_build_resources.is_set() )
		enable_game						( false );

	engine_test_suite::run_tests		( );
}

static xray::threading::atomic32_type s_resources_callbacks_have_been_dispatched;

static void thread_dispatch_callbacks	( )
{
	while ( !s_resources_callbacks_have_been_dispatched ) {
		xray::resources::dispatch_callbacks	( );
		xray::threading::yield				( 0 );
	}

	xray::resources::dispatch_callbacks		( );
}

void engine_world::finalize				( )
{
	bool const is_editor				= (m_editor != 0);
	if ( m_early_destruction_started )
	{
		finalize_resources					( );
		finalize_file_system_devices		( );
		return;
	}

	if ( !m_destruction_started )
		threading::interlocked_exchange	( m_destruction_started, 1 );

	R_ASSERT							( m_destruction_started );

	apc::run							( apc::logic,	[]() { return apc::empty::function(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run							( apc::network,	[]() { return apc::empty::function(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run							( apc::sound,	[]() { return apc::empty::function(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run							( apc::editor,	[]() { return apc::empty::function(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	if ( s_build_resources.is_set() )
		apc::run						( apc::build,	[]() { return apc::empty::function(); }, apc::continue_process_loop, apc::dont_wait_for_completion );

	apc::wait							( apc::logic );
	apc::wait							( apc::network );
	apc::wait							( apc::sound );
	apc::wait							( apc::editor );
	if ( s_build_resources.is_set() )
		apc::wait						( apc::build );

	// Producers are quiescent. Switch the renderer out of its paired
	// game/editor frame handshake before draining multiple queued game frames.
	// Otherwise closing an active Play session can end logic twice while
	// waiting for the already-stopped editor producer.
	if (m_game_enabled) enable_game(false);

	while ( !m_render_world->logic_channel().render_process_commands( false ) ) ;

	if ( is_editor )
		while ( !m_render_world->editor_channel().render_process_commands( false ) ) ;

	// clear resources
	apc::run							( apc::sound,	std::bind(&sound::world::start_destruction, m_sound_world), apc::continue_process_loop, apc::wait_for_completion );

	apc::run							( apc::logic,	[this]() { return logic_clear_resources(); }, apc::continue_process_loop, apc::dont_wait_for_completion );

#ifndef MASTER_GOLD
	if ( is_editor )
		apc::run						( apc::editor,	[this]() { return editor_clear_resources(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
#endif // #ifndef MASTER_GOLD

//////////////////////////////////////////////////////////////////////////
	apc::wait							( apc::logic );
	apc::wait							( apc::editor );

	apc::run							( apc::network,	[this]() { return network_clear_resources(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run							( apc::sound,	[this]() { return sound_clear_resources(); }, apc::continue_process_loop, apc::dont_wait_for_completion );

	while ( !m_render_world->logic_channel().render_process_commands( false ) ) ;

	if ( is_editor )
		while ( !m_render_world->editor_channel().render_process_commands( false ) ) ;

//////////////////////////////////////////////////////////////////////////

	m_render_world->clear_resources		( );
	apc::run(apc::logic,[this] {m_engine_user_world->on_render_session_closed();},
		apc::continue_process_loop,apc::wait_for_completion);
	if(is_editor)apc::run(apc::editor,[this] {m_editor->on_render_session_closed();},
		apc::continue_process_loop,apc::wait_for_completion);

	apc::wait							( apc::network );
	apc::wait							( apc::sound );

	apc::run							( apc::logic,	[this]() { return logic_dispatch_callbacks(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
#ifndef MASTER_GOLD
	if ( is_editor )
		apc::run						( apc::editor,	[this]() { return editor_dispatch_callbacks(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
#endif //#ifndef MASTER_GOLD

	apc::wait							( apc::logic );
	apc::wait							( apc::editor );

	apc::run							( apc::logic,	std::bind(&render::one_way_render_channel::owner_finalize, &m_render_world->logic_channel() ), apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run							( apc::logic,	std::bind(&sound::world_user::finalize, &m_sound_world->get_logic_world_user()), apc::continue_process_loop, apc::dont_wait_for_completion );

#ifndef MASTER_GOLD
	if ( is_editor ) {
		apc::run						( apc::editor,	std::bind(&render::one_way_render_channel::owner_finalize, &m_render_world->editor_channel() ), apc::continue_process_loop, apc::dont_wait_for_completion );
		apc::run						( apc::editor,	std::bind(&sound::world_user::finalize, &m_sound_world->get_editor_world_user()), apc::continue_process_loop, apc::dont_wait_for_completion );
	}
#endif //#ifndef MASTER_GOLD

	s_resources_callbacks_have_been_dispatched	= 0;

	apc::run_remote_only				( apc::logic,	[]() { return thread_dispatch_callbacks(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run_remote_only				( apc::network,	[]() { return thread_dispatch_callbacks(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run_remote_only				( apc::sound,	[]() { return thread_dispatch_callbacks(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run_remote_only				( apc::editor,	[]() { return thread_dispatch_callbacks(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	if ( s_build_resources.is_set() )
		apc::run_remote_only			( apc::build,	[]() { return thread_dispatch_callbacks(); }, apc::continue_process_loop, apc::dont_wait_for_completion );

	resources::wait_and_dispatch_callbacks ( true );

	// we need resources::threads_may_exit() functionality to check
	// whether we may stop dispatching resources callbacks
	threading::interlocked_exchange		( s_resources_callbacks_have_been_dispatched, 1 );

	apc::wait							( apc::logic );
	apc::wait							( apc::network );
	apc::wait							( apc::sound );
	apc::wait							( apc::editor );
	if ( s_build_resources.is_set() )
		apc::wait						( apc::build );

	apc::run							( apc::logic,	[this]() { return logic_finalize_modules(); }, apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run_remote_only				( apc::logic,	std::bind(&resources::finalize_thread_usage, false), apc::continue_process_loop, apc::dont_wait_for_completion );

	apc::run							( apc::network,	std::bind(&network::destroy_world, m_network_world), apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run_remote_only				( apc::network,	std::bind(&resources::finalize_thread_usage, false), apc::continue_process_loop, apc::dont_wait_for_completion );

	apc::run							( apc::sound,	std::bind(&sound::destroy_world, m_sound_world), apc::continue_process_loop, apc::dont_wait_for_completion );
	apc::run_remote_only				( apc::sound,	std::bind(&resources::finalize_thread_usage, false), apc::continue_process_loop, apc::dont_wait_for_completion );

	if ( s_build_resources.is_set() )
		apc::run_remote_only			( apc::build,	std::bind(&resources::finalize_thread_usage, false), apc::continue_process_loop, apc::wait_for_completion );

	apc::wait							( apc::logic );
	apc::wait							( apc::network );
	apc::wait							( apc::sound );
	apc::wait							( apc::editor );

	// for resources which were allocated in render thread,
	// but lived in other threads
 	resources::wait_and_dispatch_callbacks ( true );

#if defined(USE_VULKAN_RENDERER)
	CHECK_OR_EXIT(m_render_world->engine_renderer().finalize_game_resources(), "Vulkan game resources/callbacks must drain on the render owner before resource shutdown");
#endif
	destroy_render						( );
	// Managed HWNDs and callbacks outlive swapchain/device teardown. Native
	// level handles/cooks already drained on the editor owner above.
	if(is_editor) {
		apc::run(apc::editor,[this] {unload_editor();},apc::continue_process_loop,apc::wait_for_completion);
		apc::run_remote_only(apc::editor,std::bind(&resources::finalize_thread_usage,false),apc::continue_process_loop,apc::wait_for_completion);
	}

	finalize_resources					( );

	finalize_file_system_devices		( );

	if (is_editor) release_editor_module();

	//if ( is_editor )
	//	apc::run_remote_only			( apc::editor,	[]() { return apc::empty::function(); }, apc::break_process_loop, apc::wait_for_completion );
}
