////////////////////////////////////////////////////////////////////////////
//	Created 	: 27.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/core_entry_point.h>
#include <xray/os_include.h>
#include <xray/platform_extensions_win.h>
#include <xray/fs/path_string_utils.h>
#include <filesystem>

namespace xray {
namespace core {

static string512	s_application		=	"";
static command_line::key s_user_data_path("user_data_path","","application","explicit writable user-data directory");

void platform::preinitialize( )
{
}

void   set_application_name ( pcstr application_name )
{
	strings::copy							(s_application, application_name);
}

pcstr   application_name ( )
{
	if ( ! * s_application )
	{
		string512	path;
		if ( GetModuleFileName( 0, path, sizeof(path) ) )
		{
			pstr const last_back_slash	=	strrchr ( path, '\\' );
			if ( last_back_slash )
				strings::copy				(s_application, last_back_slash + 1);
			else
				strings::copy				(s_application, path);
		}
	}

	return									s_application;
}

pcstr   user_name ( )
{
	static string512	s_user			=	"";
	static bool			s_initialized	=	false;

	if ( ! s_initialized )
	{
		DWORD buffer_size				=	sizeof(s_user);
		GetUserName							( s_user, & buffer_size );
		R_ASSERT							( xray::platform::unload_delay_loaded_library("advapi32.dll") );
		s_initialized					=	true;
	}

	return									s_user;
}

pcstr user_data_directory	( )
{
	static fs_new::native_path_string		s_user_data_directory;
	static bool		s_initialized		=	false;

	if ( !s_initialized )
	{
		if (s_user_data_path.is_set()) {
			fixed_string<512> requested;
			R_ASSERT(s_user_data_path.is_set_as_string(&requested) && !requested.empty());
			s_user_data_directory.assign_with_conversion(requested.c_str());
			s_initialized=true;
			return s_user_data_directory.c_str();
		}
		wchar_t executable[32768]{};
		DWORD length = GetModuleFileNameW(nullptr, executable, 32768);
		if (length > 0 && length < 32768) {
			std::error_code ec;
			auto pkg_root = std::filesystem::path(executable).parent_path().parent_path();
			if (std::filesystem::is_regular_file(pkg_root / L"settings" / L"sdk-runtime.json", ec)) {
				auto pkg_user = (pkg_root / L"user-data").lexically_normal();
				std::filesystem::create_directories(pkg_user, ec);
				auto utf8 = pkg_user.generic_u8string();
				std::string resolved(reinterpret_cast<char const*>(utf8.data()), utf8.size());
				s_user_data_directory.assign_with_conversion(resolved.c_str());
				s_initialized = true;
				return s_user_data_directory.c_str();
			}
		}
		s_user_data_directory			=	fs_new::get_current_directory();
		bool const append_result		=	fs_new::append_relative_path	(
												& s_user_data_directory, 
												fs_new::native_path_string::convert("../../user_data")
											);

		XRAY_UNREFERENCED_PARAMETER			(append_result);
		s_initialized					=	true;
	}

	return									s_user_data_directory.c_str();
}

} // namespace core
} // namespace xray
