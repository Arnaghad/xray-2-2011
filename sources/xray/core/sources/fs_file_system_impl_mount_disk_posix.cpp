////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux disk mount traversal using std::filesystem
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "fs_file_system_impl.h"
#include "fs_helper.h"
#include <xray/fs_utils.h>
#include <xray/fs/case_insensitive_path.h>

#include <filesystem>
#include <string>

namespace xray {
namespace fs {

bool file_system_impl::mount_disk_file(
	fat_folder_node<> * const	work_folder,
	pcstr const					physical_path,
	u32 const					hash
)
{
	path_info::type_enum const path_type = get_path_info( nullptr, physical_path );
	file_size_type file_size = 0;
	CURE_ASSERT_CMP( path_type, ==, path_info::type_file, return false );

	if ( !calculate_file_size( &file_size, physical_path ) )
		LOGI_INFO( "file_system", "mount_disk_file '%s' cannot calculate file size", physical_path );

	path_string const file_name = file_name_from_path( physical_path );
	u32 const file_hash = crc32( file_name.c_str(), file_name.length(), hash );

	fat_node<> * const new_node = create_disk_file_node(
		file_hash,
		file_name.c_str(),
		file_name.length(),
		physical_path,
		strings::length( physical_path ),
		static_cast<u32>( file_size )
	)->cast_node();

	actualize_node( new_node, file_hash, work_folder );
	return true;
}

void file_system_impl::mount_disk_folder(
	fat_folder_node<> * const	work_folder,
	pcstr const					physical_path,
	u32 const					hash
)
{
	std::error_code ec;
	std::filesystem::path dir_path( convert_to_native( physical_path ).c_str() );
	if ( !std::filesystem::is_directory( dir_path, ec ) ) {
		std::filesystem::path resolved;
		if ( !fs_new::resolve_path_case_insensitive( dir_path, resolved ) ||
			 !std::filesystem::is_directory( resolved, ec ) )
		{
			return;
		}
		dir_path = std::move( resolved );
	}

	path_string file_spec;
	file_spec += physical_path;
	if ( file_spec.length() && file_spec.last() != '/' )
		file_spec += "/";

	std::filesystem::directory_iterator it(
		dir_path,
		std::filesystem::directory_options::skip_permission_denied,
		ec
	);
	std::filesystem::directory_iterator end;

	for ( ; it != end; it.increment( ec ) ) {
		if ( ec )
			continue;

		auto const u8_name = it->path().filename().u8string();
		std::string const entry_name( reinterpret_cast<char const*>( u8_name.data() ), u8_name.size() );
		if ( entry_name.empty() || entry_name[0] == '.' )
			continue;

		bool const found_folder = it->is_directory( ec );
		u64 const entry_size = found_folder ? 0ULL : static_cast<u64>( it->file_size( ec ) );

		u32 const saved_size = file_spec.length();
		file_spec += entry_name.c_str();

		u32 const name_length = static_cast<u32>( entry_name.size() );
		u32 const child_hash = crc32( entry_name.c_str(), name_length, hash );

		u32 const full_name_length = file_spec.length();
		fat_node<> * work_node = found_folder
			? create_disk_folder_node(
				child_hash,
				entry_name.c_str(),
				name_length,
				file_spec.c_str(),
				full_name_length
			)->cast_node()
			: create_disk_file_node(
				child_hash,
				entry_name.c_str(),
				name_length,
				file_spec.c_str(),
				full_name_length,
				static_cast<u32>( entry_size )
			)->cast_node();

		actualize_node( work_node, child_hash, work_folder );

		if ( work_node->is_folder() )
			mount_disk_folder( work_node->cast_folder(), file_spec.c_str(), child_hash );

		file_spec.set( saved_size, '\0' );
	}
}

} // namespace fs
} // namespace xray
