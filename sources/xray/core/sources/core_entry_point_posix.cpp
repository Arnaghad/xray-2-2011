////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux core entry point & process metadata
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/core_entry_point.h>
#include <xray/os_platform.h>
#include <xray/fs/native_hdd_file_system.h>
#include <xray/fs/path_string_utils.h>

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <pwd.h>
#include <string>
#include <unistd.h>

namespace xray {
namespace core {

static string512			s_application = "";
static command_line::key	s_user_data_path( "user_data_path", "", "application", "explicit writable user-data directory" );

void platform::preinitialize()
{
}

void set_application_name( pcstr const application_name )
{
	strings::copy( s_application, application_name );
}

pcstr application_name()
{
	if ( !*s_application ) {
		auto const exe = os::executable_path();
		if ( !exe.empty() ) {
			auto const utf8 = exe.filename().u8string();
			std::string const name( reinterpret_cast<char const*>( utf8.data() ), utf8.size() );
			strings::copy( s_application, name.c_str() );
		}
	}
	return s_application;
}

pcstr user_name()
{
	static string512	s_user = "";
	static bool			s_initialized = false;

	if ( !s_initialized ) {
		if ( struct passwd* pw = ::getpwuid( ::geteuid() ); pw && pw->pw_name )
			strings::copy( s_user, pw->pw_name );
		else if ( char const* env_user = std::getenv( "USER" ); env_user && *env_user )
			strings::copy( s_user, env_user );
		else
			strings::copy( s_user, "user" );
		s_initialized = true;
	}
	return s_user;
}

pcstr user_data_directory()
{
	static fs_new::native_path_string	s_user_data_directory;
	static bool							s_initialized = false;

	if ( !s_initialized ) {
		if ( s_user_data_path.is_set() ) {
			fixed_string<512> requested;
			R_ASSERT( s_user_data_path.is_set_as_string( &requested ) && !requested.empty() );
			s_user_data_directory.assign_with_conversion( requested.c_str() );
			s_initialized = true;
			return s_user_data_directory.c_str();
		}

		auto const exe_dir = os::executable_directory();
		if ( !exe_dir.empty() ) {
			std::error_code ec;
			auto const pkg_root = exe_dir.parent_path();
			if ( std::filesystem::is_regular_file( pkg_root / "settings" / "sdk-runtime.json", ec ) ) {
				auto const pkg_user = ( pkg_root / "user-data" ).lexically_normal();
				std::filesystem::create_directories( pkg_user, ec );
				auto const utf8 = pkg_user.generic_u8string();
				std::string resolved( reinterpret_cast<char const*>( utf8.data() ), utf8.size() );
				s_user_data_directory.assign_with_conversion( resolved.c_str() );
				s_initialized = true;
				return s_user_data_directory.c_str();
			}
		}

		s_user_data_directory = fs_new::get_current_directory();
		bool const append_result = fs_new::append_relative_path(
			&s_user_data_directory,
			fs_new::native_path_string::convert( "../../user_data" )
		);
		XRAY_UNREFERENCED_PARAMETER( append_result );
		s_initialized = true;
	}
	return s_user_data_directory.c_str();
}

static fs_new::native_hdd_file_system s_hdd;

fs_new::device_file_system_interface * get_core_device_file_system()
{
	return &s_hdd;
}

pcstr get_command_line()
{
	static string512	s_command_line = "";
	static bool			s_initialized = false;

	if ( !s_initialized ) {
		std::ifstream in( "/proc/self/cmdline", std::ios::binary );
		std::string combined;
		if ( in ) {
			std::string raw( ( std::istreambuf_iterator<char>( in ) ), std::istreambuf_iterator<char>() );
			for ( std::size_t i = 0; i < raw.size(); ++i ) {
				if ( raw[i] == '\0' ) {
					if ( i + 1 < raw.size() )
						combined.push_back( ' ' );
				} else {
					combined.push_back( raw[i] );
				}
			}
		}
		strings::copy( s_command_line, combined.c_str() );
		s_initialized = true;
	}
	return s_command_line;
}

} // namespace core
} // namespace xray
