////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux current directory query preserving case sensitivity
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/fs/native_path_string.h>
#include <xray/fs/path_string_utils.h>

#include <filesystem>
#include <string>

namespace xray {
namespace fs_new {

static native_path_string					s_current_directory;
static bool	s_initialized				=	false;

native_path_string const &	get_current_directory ()
{
	if ( s_initialized )
		return								s_current_directory;

	std::error_code ec;
	auto const cwd = std::filesystem::current_path( ec );
	if ( !ec && !cwd.empty() ) {
		auto const utf8 = cwd.generic_u8string();
		std::string resolved( reinterpret_cast<char const*>( utf8.data() ), utf8.size() );
		while ( resolved.size() > 1 && ( resolved.back() == '/' || resolved.back() == '\\' ) )
			resolved.pop_back();
		s_current_directory.assign_with_conversion( resolved.c_str() );
	}
	s_initialized						=	true;
	return									s_current_directory;
}

} // namespace fs_new
} // namespace xray
