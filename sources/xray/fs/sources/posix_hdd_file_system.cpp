////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux HDD file system backend
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/fs/posix_hdd_file_system.h>
#include <xray/fs/case_insensitive_path.h>

#include <cerrno>
#include <cstdint>
#include <cstring>
#include <fcntl.h>
#include <filesystem>
#include <sys/stat.h>
#include <unistd.h>

namespace xray {
namespace fs_new {

namespace {

inline pvoid fd_to_handle( int const fd ) noexcept
{
	return ( fd < 0 ) ? nullptr : reinterpret_cast<pvoid>( static_cast<intptr_t>( fd ) + 1 );
}

inline int handle_to_fd( pvoid const handle ) noexcept
{
	return handle ? static_cast<int>( reinterpret_cast<intptr_t>( handle ) - 1 ) : -1;
}

} // namespace

signalling_bool posix_hdd_file_system::open(
	pvoid * const				out_handle,
	native_path_string const &	absolute_path,
	open_file_params const &	params
)
{
	int flags = O_CLOEXEC;
	if ( params.access == file_access::read )
		flags |= O_RDONLY;
	else if ( params.access == file_access::write )
		flags |= O_WRONLY;
	else if ( params.access == file_access::read_write )
		flags |= O_RDWR;

	if ( params.mode == file_mode::create_always )
		flags |= ( O_CREAT | O_TRUNC );
	else if ( params.mode == file_mode::append_or_create )
		flags |= O_CREAT;

	int fd = ::open( absolute_path.c_str(), flags, 0664 );
	if ( fd < 0 && errno == ENOENT && params.mode == file_mode::open_existing ) {
		std::filesystem::path resolved;
		if ( resolve_path_case_insensitive( std::filesystem::path( absolute_path.c_str() ), resolved ) )
			fd = ::open( resolved.c_str(), flags, 0664 );
	}

	if ( fd < 0 && params.assert_on_fail ) {
		FATAL(
			"failed opening file:%s with open flags:%s (errno=%d: %s)",
			absolute_path.c_str(),
			file_open_flags_to_string( params.mode, params.access ).c_str(),
			errno,
			std::strerror( errno )
		);
	}

	if ( fd >= 0 && params.mode == file_mode::append_or_create )
		::lseek( fd, 0, SEEK_END );

	*out_handle = fd_to_handle( fd );
	return ( fd >= 0 );
}

bool posix_hdd_file_system::get_file_size( file_size_type * out_size, pvoid const handle )
{
	int const fd = handle_to_fd( handle );
	if ( fd < 0 )
		return false;

	struct stat st = {};
	if ( ::fstat( fd, &st ) != 0 )
		return false;

	*out_size = static_cast<file_size_type>( st.st_size );
	return true;
}

bool posix_hdd_file_system::seek( pvoid const handle, file_size_type in_offset, seek_file_enum origin )
{
	int const fd = handle_to_fd( handle );
	if ( fd < 0 )
		return false;

	int whence = SEEK_SET;
	if ( origin == seek_file_begin )
		whence = SEEK_SET;
	else if ( origin == seek_file_current )
		whence = SEEK_CUR;
	else if ( origin == seek_file_end )
		whence = SEEK_END;
	else
		NODEFAULT( whence = SEEK_SET );

	return ::lseek( fd, static_cast<off_t>( in_offset ), whence ) != static_cast<off_t>( -1 );
}

file_size_type posix_hdd_file_system::tell( pvoid const handle )
{
	int const fd = handle_to_fd( handle );
	if ( fd < 0 )
		return 0;

	off_t const pos = ::lseek( fd, 0, SEEK_CUR );
	return ( pos == static_cast<off_t>( -1 ) ) ? 0 : static_cast<file_size_type>( pos );
}

void posix_hdd_file_system::setvbuf( pvoid const handle, char * buffer, int mode, file_size_type size )
{
	XRAY_UNREFERENCED_PARAMETERS( handle, buffer, mode, size );
}

void posix_hdd_file_system::flush( pvoid const handle )
{
	int const fd = handle_to_fd( handle );
	if ( fd >= 0 )
		::fsync( fd );
}

bool posix_hdd_file_system::create_folder( native_path_string const & absolute_physical_path )
{
	if ( ::mkdir( absolute_physical_path.c_str(), 0755 ) == 0 )
		return true;

	if ( errno == EEXIST ) {
		struct stat st = {};
		return ( ::stat( absolute_physical_path.c_str(), &st ) == 0 ) && S_ISDIR( st.st_mode );
	}
	return false;
}

bool posix_hdd_file_system::erase( native_path_string const & absolute_physical_path )
{
	auto try_erase = []( pcstr path ) -> bool {
		if ( ::unlink( path ) == 0 )
			return true;
		if ( errno == EISDIR || errno == EPERM )
			return ::rmdir( path ) == 0;
		return ::rmdir( path ) == 0;
	};

	if ( try_erase( absolute_physical_path.c_str() ) )
		return true;

	if ( errno == ENOENT ) {
		std::filesystem::path resolved;
		if ( resolve_path_case_insensitive( std::filesystem::path( absolute_physical_path.c_str() ), resolved ) )
			return try_erase( resolved.c_str() );
	}
	return false;
}

file_size_type posix_hdd_file_system::read( pvoid const handle, pvoid data, file_size_type size )
{
	int const fd = handle_to_fd( handle );
	if ( fd < 0 || size == 0 )
		return 0;

	u8 * dst = static_cast<u8 *>( data );
	file_size_type total = 0;
	while ( total < size ) {
		ssize_t const n = ::read( fd, dst + total, static_cast<size_t>( size - total ) );
		if ( n < 0 ) {
			if ( errno == EINTR )
				continue;
			break;
		}
		if ( n == 0 )
			break;
		total += static_cast<file_size_type>( n );
	}
	return total;
}

file_size_type posix_hdd_file_system::write( pvoid const handle, pcvoid data, file_size_type size )
{
	int const fd = handle_to_fd( handle );
	R_ASSERT( fd >= 0 );
	if ( fd < 0 || size == 0 )
		return 0;

	u8 const * src = static_cast<u8 const *>( data );
	file_size_type total = 0;
	while ( total < size ) {
		ssize_t const n = ::write( fd, src + total, static_cast<size_t>( size - total ) );
		if ( n < 0 ) {
			if ( errno == EINTR )
				continue;
			break;
		}
		if ( n == 0 )
			break;
		total += static_cast<file_size_type>( n );
	}
	return total;
}

void posix_hdd_file_system::close( pvoid const handle )
{
	int const fd = handle_to_fd( handle );
	if ( fd >= 0 )
		::close( fd );
}

} // namespace fs_new
} // namespace xray
