////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux legacy FS utilities
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/fs_utils.h>
#include <xray/fs_path.h>
#include <xray/fs/case_insensitive_path.h>
#include "fs_watcher_internal.h"

#include <cerrno>
#include <cstring>
#include <fcntl.h>
#include <filesystem>
#include <string>
#include <sys/stat.h>
#include <unistd.h>

namespace xray {
namespace fs {

namespace {

pcstr cached_current_directory()
{
	static string_path s_current_directory;
	static bool s_initialized = false;
	if ( s_initialized )
		return s_current_directory;

	std::error_code ec;
	auto const cwd = std::filesystem::current_path( ec );
	if ( !ec && !cwd.empty() ) {
		auto const u8 = cwd.generic_u8string();
		std::string resolved( reinterpret_cast<char const*>( u8.data() ), u8.size() );
		while ( resolved.size() > 1 && ( resolved.back() == '/' || resolved.back() == '\\' ) )
			resolved.pop_back();
		strings::copy( s_current_directory, resolved.c_str() );
	}
	s_initialized = true;
	return s_current_directory;
}

} // namespace

pcstr get_default_user_data_folder()
{
	return "../../user_data";
}

path_string convert_to_relative_path( pcstr physical_path )
{
	if ( !physical_path || !*physical_path )
		return path_string();

	std::error_code ec;
	std::filesystem::path phys = std::filesystem::path( physical_path ).lexically_normal();
	std::filesystem::path cur = std::filesystem::path( cached_current_directory() ).lexically_normal();
	std::filesystem::path rel = phys.lexically_relative( cur );
	if ( rel.empty() )
		return physical_path;

	std::string str = rel.generic_string();
	if ( str.rfind( "./", 0 ) == 0 )
		str.erase( 0, 2 );

	path_string out_relative_path = str.c_str();
	convert_to_portable_in_place( out_relative_path.c_str() );
	return out_relative_path;
}

bool is_absolute_path( pcstr path )
{
	return path && ( path[0] == '/' || ( path[0] != '\0' && path[1] == ':' ) );
}

signalling_bool convert_to_absolute_native_path( buffer_string * out_result, pcstr relative_portable_path )
{
	verify_path_is_portable( relative_portable_path );

	path_string const relative_native = convert_to_native( relative_portable_path );
	if ( is_absolute_path( relative_native.c_str() ) ) {
		*out_result = relative_native;
		return true;
	}

	path_string out_absolute = cached_current_directory();
	while ( out_absolute.length() > 1 && ( out_absolute.last() == '\\' || out_absolute.last() == '/' ) )
		out_absolute.set_length( out_absolute.length() - 1 );

	convert_to_portable_in_place( out_absolute.c_str() );
	if ( !append_relative_path( &out_absolute, relative_portable_path ) )
		return false;

	convert_to_native_in_place( out_absolute.c_str() );
	*out_result = out_absolute;
	return true;
}

signalling_bool open_file_impl(
	FILE ** const					out_file,
	enum_flags<open_file_enum> const open_flags,
	pcstr const						portable_path,
	bool const						assert_on_fail
)
{
	ASSERT( out_file );
	verify_path_is_portable( portable_path );

	path_string absolute_native_path;
	if ( !convert_to_absolute_native_path( &absolute_native_path, portable_path ) )
		return false;

	int file_open_flags = O_CLOEXEC;
	pcstr fdopen_mode = "rb";

	bool const want_read = !!( open_flags & open_file_read );
	bool const want_write = !!( open_flags & open_file_write );
	if ( want_read && want_write ) {
		file_open_flags |= O_RDWR;
		fdopen_mode = "r+b";
	} else if ( want_read ) {
		file_open_flags |= O_RDONLY;
		fdopen_mode = "rb";
	} else if ( want_write ) {
		file_open_flags |= O_WRONLY;
		fdopen_mode = ( open_flags & open_file_append ) ? "ab" : "wb";
	} else {
		FATAL( "%s", "one or both read or write flags must be specified" );
	}

	if ( open_flags & open_file_create )
		file_open_flags |= O_CREAT;
	if ( open_flags & open_file_truncate )
		file_open_flags |= O_TRUNC;
	if ( open_flags & open_file_append )
		file_open_flags |= O_APPEND;

	int fd = ::open( absolute_native_path.c_str(), file_open_flags, 0664 );
	if ( fd < 0 && errno == ENOENT && !( open_flags & open_file_create ) ) {
		std::filesystem::path resolved;
		if ( fs_new::resolve_path_case_insensitive( std::filesystem::path( absolute_native_path.c_str() ), resolved ) )
			fd = ::open( resolved.c_str(), file_open_flags, 0664 );
	}

	if ( fd < 0 ) {
		*out_file = nullptr;
		if ( assert_on_fail ) {
			FATAL(
				"failed opening file:%s (errno=%d: %s)",
				absolute_native_path.c_str(),
				errno,
				std::strerror( errno )
			);
		}
		return false;
	}

	*out_file = ::fdopen( fd, fdopen_mode );
	if ( !*out_file ) {
		int const saved_errno = errno;
		::close( fd );
		if ( assert_on_fail ) {
			FATAL(
				"failed fdopen file:%s with mode '%s' (errno=%d: %s)",
				absolute_native_path.c_str(),
				fdopen_mode,
				saved_errno,
				std::strerror( saved_errno )
			);
		}
		return false;
	}

	return true;
}

signalling_bool calculate_file_size( file_size_type * const out_file_size, FILE * const file )
{
	ASSERT( out_file_size );
	CURE_ASSERT( file, return false );

	struct stat st = {};
	if ( ::fstat( ::fileno( file ), &st ) != 0 )
		return false;

	*out_file_size = static_cast<file_size_type>( st.st_size );
	return true;
}

bool make_dir( pcstr portable_path )
{
	verify_path_is_portable( portable_path );
	path_string absolute_native_path;
	if ( !convert_to_absolute_native_path( &absolute_native_path, portable_path ) )
		return false;

	if ( ::mkdir( absolute_native_path.c_str(), 0755 ) == 0 )
		return true;

	if ( errno == EEXIST ) {
		struct stat st = {};
		return ( ::stat( absolute_native_path.c_str(), &st ) == 0 ) && S_ISDIR( st.st_mode );
	}

	return false;
}

path_info::type_enum get_path_info( path_info * out_path_info, pcstr portable_path )
{
	verify_path_is_portable( portable_path );
	path_string absolute_native_path;
	if ( !convert_to_absolute_native_path( &absolute_native_path, portable_path ) )
		return path_info::type_nothing;

	struct stat st = {};
	if ( ::stat( absolute_native_path.c_str(), &st ) != 0 ) {
		std::filesystem::path resolved;
		if ( errno != ENOENT ||
			 !fs_new::resolve_path_case_insensitive( std::filesystem::path( absolute_native_path.c_str() ), resolved ) ||
			 ::stat( resolved.c_str(), &st ) != 0 )
		{
			if ( out_path_info )
				out_path_info->type = path_info::type_nothing;
			return path_info::type_nothing;
		}
	}

	path_info::type_enum const out_path_type = S_ISDIR( st.st_mode ) ? path_info::type_folder : path_info::type_file;
	if ( out_path_info ) {
		out_path_info->type = out_path_type;
		out_path_info->file_last_modify_time = static_cast<u32>( st.st_mtime );
		out_path_info->file_size = static_cast<file_size_type>( S_ISDIR( st.st_mode ) ? 0 : st.st_size );
	}

	return out_path_type;
}

static inline bool skip_notifications( file_type * file )
{
#if XRAY_FS_WATCHER_ENABLED
	return file->skip_notifications;
#else
	XRAY_UNREFERENCED_PARAMETER( file );
	return false;
#endif
}

void set_file_size_and_close( file_type * file, file_size_type size )
{
	FILE * const fp = file_type_to_FILE( file );
	R_ASSERT( fp );

	if ( skip_notifications( file ) ) {
		file_size_type previous_size = 0;
		bool const got_file_size = calculate_file_size( &previous_size, file );
		R_ASSERT_U( got_file_size );

		u32 notifications_to_skip = 0;
		if ( previous_size < size )
			notifications_to_skip = 1;
		else if ( previous_size > size )
			notifications_to_skip = 2;

		skip_deassociation_notifications( file, notifications_to_skip );
	}

	::fflush( fp );
	int const error = ::ftruncate( ::fileno( fp ), static_cast<off_t>( size ) );
	R_ASSERT_U( error == 0 );
	close_file( file );
}

void set_end_of_file_and_close( file_type * file )
{
	off_t const file_pos = ::ftello( file_type_to_FILE( file ) );
	set_file_size_and_close( file, static_cast<file_size_type>( file_pos ) );
}

} // namespace fs
} // namespace xray
