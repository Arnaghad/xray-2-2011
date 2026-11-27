////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux directory iteration and path info
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/fs/posix_hdd_file_system.h>
#include <xray/fs/case_insensitive_path.h>
#include <xray/fs/path_string_utils.h>
#include <xray/fs/physical_path_info.h>

#include <cerrno>
#include <cstdint>
#include <dirent.h>
#include <filesystem>
#include <string>
#include <sys/stat.h>

namespace xray {
namespace fs_new {

namespace {

struct posix_search_state
{
	DIR *		dir = nullptr;
	std::string	dir_path;
};

bool stat_with_case_fallback( std::string const & path, struct stat & out_stat, std::string * out_resolved_path = nullptr )
{
	if ( ::stat( path.c_str(), &out_stat ) == 0 ) {
		if ( out_resolved_path )
			*out_resolved_path = path;
		return true;
	}

	if ( errno == ENOENT ) {
		std::filesystem::path resolved;
		if ( resolve_path_case_insensitive( std::filesystem::path( path ), resolved ) ) {
			if ( ::stat( resolved.c_str(), &out_stat ) == 0 ) {
				if ( out_resolved_path )
					*out_resolved_path = resolved.string();
				return true;
			}
		}
	}
	return false;
}

bool fill_next_entry( posix_search_state * const state, physical_path_info_data * const out_data )
{
	while ( struct dirent * entry = ::readdir( state->dir ) ) {
		if ( entry->d_name[0] == '.' )
			continue;

		std::string full_path = state->dir_path;
		if ( !full_path.empty() && full_path.back() != '/' )
			full_path.push_back( '/' );
		full_path += entry->d_name;

		struct stat st = {};
		if ( ::stat( full_path.c_str(), &st ) != 0 )
			continue;

		out_data->file_size = static_cast<file_size_type>( S_ISDIR( st.st_mode ) ? 0 : st.st_size );
		out_data->type = S_ISDIR( st.st_mode )
			? physical_path_info_data::type_folder
			: physical_path_info_data::type_file;
		out_data->last_time_of_write = static_cast<u32>( st.st_mtime );
		out_data->path = entry->d_name;
		out_data->path_type = physical_path_info_data::path_type_contains_name;
		return true;
	}
	return false;
}

std::string strip_glob_suffix( pcstr mask )
{
	std::string dir = mask ? mask : "";
	while ( !dir.empty() && dir.back() == '*' )
		dir.pop_back();
	while ( dir.size() > 1 && ( dir.back() == '/' || dir.back() == '\\' ) )
		dir.pop_back();
	if ( dir.empty() )
		dir = ".";
	return dir;
}

} // namespace

physical_path_info posix_hdd_file_system::get_physical_path_info( native_path_string const & native_physical_path )
{
	native_path_string absolute_physical_path;
	bool const convert_result = convert_to_absolute_path( &absolute_physical_path, native_physical_path, assert_on_fail_true );
	R_ASSERT_U( convert_result );

	struct stat local_stat = {};
	std::string resolved_path;
	if ( !stat_with_case_fallback( absolute_physical_path.c_str(), local_stat, &resolved_path ) )
		return physical_path_info();

	physical_path_initializer initializer;
	initializer.device = this;
	initializer.data.path = resolved_path.c_str();
	initializer.data.path_type = physical_path_info_data::path_type_contains_full_path;
	initializer.parent = nullptr;
	initializer.data.type = S_ISDIR( local_stat.st_mode )
		? physical_path_info_data::type_folder
		: physical_path_info_data::type_file;
	initializer.data.file_size = static_cast<file_size_type>( local_stat.st_size );
	initializer.data.last_time_of_write = static_cast<u32>( local_stat.st_mtime );
	return physical_path_info( initializer );
}

bool posix_hdd_file_system::find_first(
	u64 *						out_search_handle,
	physical_path_info_data *	out_data,
	pcstr						absolute_native_physical_path_mask
)
{
	std::string dir_path = strip_glob_suffix( absolute_native_physical_path_mask );
	DIR * dir = ::opendir( dir_path.c_str() );
	if ( !dir && errno == ENOENT ) {
		std::filesystem::path resolved;
		if ( resolve_path_case_insensitive( std::filesystem::path( dir_path ), resolved ) ) {
			dir_path = resolved.string();
			dir = ::opendir( dir_path.c_str() );
		}
	}

	if ( !dir ) {
		*out_search_handle = u64( -1 );
		return false;
	}

	auto * state = new posix_search_state{ dir, std::move( dir_path ) };
	if ( !fill_next_entry( state, out_data ) ) {
		::closedir( state->dir );
		delete state;
		*out_search_handle = u64( -1 );
		return true;
	}

	*out_search_handle = static_cast<u64>( reinterpret_cast<uintptr_t>( state ) );
	return true;
}

bool posix_hdd_file_system::find_next( u64 * in_out_search_handle, physical_path_info_data * out_data )
{
	R_ASSERT( *in_out_search_handle != u64( -1 ) );
	auto * state = reinterpret_cast<posix_search_state *>( static_cast<uintptr_t>( *in_out_search_handle ) );
	if ( !fill_next_entry( state, out_data ) ) {
		*in_out_search_handle = u64( -1 );
		return false;
	}
	return true;
}

void posix_hdd_file_system::find_close( u64 search_handle )
{
	if ( search_handle == u64( -1 ) || search_handle == 0 )
		return;
	auto * state = reinterpret_cast<posix_search_state *>( static_cast<uintptr_t>( search_handle ) );
	if ( state->dir )
		::closedir( state->dir );
	delete state;
}

} // namespace fs_new
} // namespace xray
