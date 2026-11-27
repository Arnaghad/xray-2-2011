#include "pch.h"
#include "managed_host.h"
#include <xray/os_platform.h>
#include <algorithm>
#include <filesystem>
#include <stdexcept>
#include <mutex>
#include <string>
#include <vector>
#include <hostfxr.h>
#include <coreclr_delegates.h>

namespace xray::editor::hosting {
namespace {

using hostfxr_string = std::basic_string<char_t>;

#if XRAY_PLATFORM_WINDOWS
#	define XRAY_HOSTFXR_TEXT(s) L##s
inline constexpr char_t const* k_hostfxr_file_name = L"hostfxr.dll";
#else
#	define XRAY_HOSTFXR_TEXT(s) s
inline constexpr char_t const* k_hostfxr_file_name = "libhostfxr.so";
#endif

hostfxr_string to_hostfxr_string( std::filesystem::path const& path )
{
#if XRAY_PLATFORM_WINDOWS
	return path.wstring();
#else
	return path.string();
#endif
}

std::filesystem::path find_hostfxr_under_root( std::filesystem::path const& root )
{
	std::error_code ec;
	std::filesystem::path const fxr_dir = root / "host" / "fxr";
	if ( !std::filesystem::is_directory( fxr_dir, ec ) )
		return {};

	std::vector<std::filesystem::path> candidates;
	for ( auto const& entry : std::filesystem::directory_iterator( fxr_dir, ec ) ) {
		if ( entry.is_directory( ec ) && std::filesystem::is_regular_file( entry.path() / k_hostfxr_file_name, ec ) )
			candidates.push_back( entry.path() );
	}
	if ( candidates.empty() )
		return {};

	std::sort( candidates.begin(), candidates.end(), []( auto const& a, auto const& b ) {
		return a.filename().string() > b.filename().string();
	} );
	return ( candidates.front() / k_hostfxr_file_name ).lexically_normal().make_preferred();
}

std::filesystem::path resolve_hostfxr( std::filesystem::path& dotnet_root_out )
{
	std::string env_root;
	if ( xray::os::get_environment_variable( "DOTNET_ROOT", env_root ) && !env_root.empty() ) {
		std::filesystem::path const root( env_root );
		std::filesystem::path const found = find_hostfxr_under_root( root );
		if ( !found.empty() ) {
			dotnet_root_out = root.lexically_normal().make_preferred();
			return found;
		}
	}

	std::filesystem::path const compiled_fallback( XRAY_HOSTFXR_PATH );
	std::error_code ec;
	if ( !compiled_fallback.empty() && std::filesystem::is_regular_file( compiled_fallback, ec ) ) {
		dotnet_root_out = compiled_fallback.parent_path().parent_path().parent_path().parent_path().make_preferred();
		return compiled_fallback;
	}

#if XRAY_PLATFORM_LINUX
	for ( char const* candidate_root : { "/usr/lib64/dotnet", "/usr/share/dotnet" } ) {
		std::filesystem::path const root( candidate_root );
		std::filesystem::path const found = find_hostfxr_under_root( root );
		if ( !found.empty() ) {
			dotnet_root_out = root.lexically_normal().make_preferred();
			return found;
		}
	}
#endif

	dotnet_root_out = compiled_fallback.parent_path().parent_path().parent_path().parent_path().make_preferred();
	return compiled_fallback;
}

void* entrypoint( char_t const* method, std::filesystem::path directory )
{
	// CoreCLR is process-owned. Keep hostfxr loaded for all restart cycles;
	// individual bridge contexts/delegates/windows are disposed independently.
	static std::mutex lock;
	std::lock_guard guard( lock );
	static xray::os::dynamic_library module;
	static load_assembly_and_get_function_pointer_fn load = nullptr;
	static std::filesystem::path deployment;

	if ( directory.empty() ) {
		directory = xray::os::executable_directory();
		if ( directory.empty() )
			throw std::runtime_error( "Cannot locate editor deployment directory" );
	}
	directory = std::filesystem::absolute( directory ).lexically_normal();
	if ( load && directory != deployment )
		throw std::runtime_error( "Editor runtime already owns another deployment" );

	if ( !load ) {
		std::filesystem::path dotnet_root;
		std::filesystem::path const hostfxr_path = resolve_hostfxr( dotnet_root );
		if ( !module.valid() ) {
			if ( !module.open_existing( hostfxr_path ) )
				module.open( hostfxr_path, false );
		}
		if ( !module.valid() )
			throw std::runtime_error( "Cannot load the configured system hostfxr" );

		auto initialize = module.symbol_as<hostfxr_initialize_for_runtime_config_fn>( "hostfxr_initialize_for_runtime_config" );
		auto get_delegate = module.symbol_as<hostfxr_get_runtime_delegate_fn>( "hostfxr_get_runtime_delegate" );
		auto close = module.symbol_as<hostfxr_close_fn>( "hostfxr_close" );
		if ( !initialize || !get_delegate || !close )
			throw std::runtime_error( "Incomplete hostfxr API" );

		auto const config = directory / "xray.editor.base.runtimeconfig.json";
		hostfxr_string const config_str = to_hostfxr_string( config );
		hostfxr_string const dotnet_root_str = to_hostfxr_string( dotnet_root );
		hostfxr_initialize_parameters parameters{ sizeof(hostfxr_initialize_parameters), nullptr, dotnet_root_str.c_str() };
		hostfxr_handle context = nullptr;
		auto const status = initialize( config_str.c_str(), &parameters, &context );
		if ( status < 0 || !context ) {
			if ( context )
				close( context );
			throw std::runtime_error( "Cannot initialize editor .NET Desktop runtime" );
		}
		void* loader = nullptr;
		auto const delegate_status = get_delegate( context, hdt_load_assembly_and_get_function_pointer, &loader );
		close( context );
		if ( delegate_status < 0 || !loader )
			throw std::runtime_error( "Cannot obtain managed assembly loader" );

		deployment = directory;
		load = reinterpret_cast<load_assembly_and_get_function_pointer_fn>( loader );
	}

	auto const assembly = directory / "xray.editor.base.dll";
	hostfxr_string const assembly_str = to_hostfxr_string( assembly );
	void* entry = nullptr;
	auto const status = load(
		assembly_str.c_str(),
		XRAY_HOSTFXR_TEXT("xray.editor_base.Bridge, xray.editor.base"),
		method,
		UNMANAGEDCALLERSONLY_METHOD,
		nullptr,
		&entry
	);
	if ( status < 0 || !entry )
		throw std::runtime_error( "Cannot load editor managed bridge factory" );
	return entry;
}

} // namespace

create_world_fn factory( std::filesystem::path directory )
{
	return reinterpret_cast<create_world_fn>( entrypoint( XRAY_HOSTFXR_TEXT("CreateWorld"), directory ) );
}

bind_sdk_fn sdk_factory( std::filesystem::path directory )
{
	return reinterpret_cast<bind_sdk_fn>( entrypoint( XRAY_HOSTFXR_TEXT("BindSdk"), directory ) );
}

bind_resources_fn resource_factory( std::filesystem::path directory )
{
	return reinterpret_cast<bind_resources_fn>( entrypoint( XRAY_HOSTFXR_TEXT("BindResources"), directory ) );
}

bind_scene_fn scene_factory( std::filesystem::path directory )
{
	return reinterpret_cast<bind_scene_fn>( entrypoint( XRAY_HOSTFXR_TEXT("BindScene"), directory ) );
}

} // namespace xray::editor::hosting
