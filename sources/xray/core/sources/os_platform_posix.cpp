////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux x86_64 implementation of xray::os PAL
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/os_platform.h>

#include <cerrno>
#include <cstdlib>
#include <cstring>
#include <dlfcn.h>
#include <fcntl.h>
#include <fstream>
#include <limits.h>
#include <string>
#include <sys/wait.h>
#include <unistd.h>

namespace xray {
namespace os {

dynamic_library::~dynamic_library() noexcept
{
	close();
}

dynamic_library::dynamic_library( dynamic_library&& other ) noexcept
	: m_handle( other.m_handle )
	, m_owned( other.m_owned )
	, m_last_error( std::move( other.m_last_error ) )
{
	other.m_handle = nullptr;
	other.m_owned = false;
}

dynamic_library& dynamic_library::operator=( dynamic_library&& other ) noexcept
{
	if ( this != &other ) {
		close();
		m_handle = other.m_handle;
		m_owned = other.m_owned;
		m_last_error = std::move( other.m_last_error );
		other.m_handle = nullptr;
		other.m_owned = false;
	}
	return *this;
}

bool dynamic_library::open( std::filesystem::path const& path, bool const /*search_library_dir*/ )
{
	close();
	m_last_error.clear();
	if ( path.empty() ) {
		m_last_error = "Empty library path";
		return false;
	}

	::dlerror();
	void* mod = ::dlopen( path.c_str(), RTLD_NOW | RTLD_LOCAL );
	if ( !mod ) {
		char const* err = ::dlerror();
		m_last_error = err ? err : "dlopen failed";
		return false;
	}

	m_handle = mod;
	m_owned = true;
	return true;
}

bool dynamic_library::open_existing( std::filesystem::path const& path )
{
	close();
	m_last_error.clear();
	if ( path.empty() )
		return false;

	::dlerror();
	void* mod = ::dlopen( path.c_str(), RTLD_NOW | RTLD_NOLOAD );
	if ( !mod ) {
		char const* err = ::dlerror();
		m_last_error = err ? err : "library not loaded";
		return false;
	}

	m_handle = mod;
	m_owned = true; // RTLD_NOLOAD increments reference count on glibc
	return true;
}

void dynamic_library::close() noexcept
{
	if ( m_handle && m_owned )
		::dlclose( m_handle );
	m_handle = nullptr;
	m_owned = false;
}

void* dynamic_library::symbol( pcstr const name ) const noexcept
{
	if ( !m_handle || !name )
		return nullptr;
	return ::dlsym( m_handle, name );
}

std::string dynamic_library::format_library_name( pcstr const base_name, bool const /*with_lib_prefix*/ )
{
	if ( !base_name || !*base_name )
		return {};
	return std::string( "lib" ) + base_name + ".so";
}

std::filesystem::path executable_path()
{
	char buffer[PATH_MAX] = {};
	ssize_t const len = ::readlink( "/proc/self/exe", buffer, sizeof( buffer ) - 1 );
	if ( len <= 0 )
		return {};
	buffer[len] = '\0';
	return std::filesystem::path( buffer ).lexically_normal();
}

std::filesystem::path executable_directory()
{
	auto const exe = executable_path();
	return exe.empty() ? std::filesystem::path() : exe.parent_path();
}

bool get_environment_variable( pcstr const name, std::string& out_value )
{
	out_value.clear();
	if ( !name || !*name )
		return false;
	char const* val = std::getenv( name );
	if ( !val )
		return false;
	out_value = val;
	return true;
}

u32 current_process_id() noexcept
{
	return static_cast<u32>( ::getpid() );
}

process_result run_process(
	std::filesystem::path const& executable,
	std::vector<std::string> const& args
)
{
	process_result result;
	if ( executable.empty() ) {
		result.launch_error = "Empty executable path";
		return result;
	}

	int out_pipe[2] = { -1, -1 };
	if ( ::pipe( out_pipe ) != 0 ) {
		result.launch_error = std::strerror( errno );
		return result;
	}

	int err_pipe[2] = { -1, -1 };
	if ( ::pipe2( err_pipe, O_CLOEXEC ) != 0 ) {
		int const saved = errno;
		::close( out_pipe[0] );
		::close( out_pipe[1] );
		result.launch_error = std::strerror( saved );
		return result;
	}

	std::vector<std::string> argv_storage;
	argv_storage.reserve( args.size() + 1 );
	argv_storage.push_back( executable.string() );
	for ( auto const& arg : args )
		argv_storage.push_back( arg );

	std::vector<char*> argv_ptrs;
	argv_ptrs.reserve( argv_storage.size() + 1 );
	for ( auto& item : argv_storage )
		argv_ptrs.push_back( item.data() );
	argv_ptrs.push_back( nullptr );

	bool const has_dir = executable.has_parent_path();
	pid_t const pid = ::fork();
	if ( pid < 0 ) {
		int const saved = errno;
		::close( out_pipe[0] );
		::close( out_pipe[1] );
		::close( err_pipe[0] );
		::close( err_pipe[1] );
		result.launch_error = std::strerror( saved );
		return result;
	}

	if ( pid == 0 ) {
		::close( out_pipe[0] );
		::close( err_pipe[0] );
		if ( out_pipe[1] != STDOUT_FILENO )
			::dup2( out_pipe[1], STDOUT_FILENO );
		if ( out_pipe[1] != STDERR_FILENO )
			::dup2( out_pipe[1], STDERR_FILENO );
		if ( out_pipe[1] != STDOUT_FILENO && out_pipe[1] != STDERR_FILENO )
			::close( out_pipe[1] );

		if ( has_dir )
			::execv( argv_ptrs[0], argv_ptrs.data() );
		else
			::execvp( argv_ptrs[0], argv_ptrs.data() );

		int err = errno;
		ssize_t const written = ::write( err_pipe[1], &err, sizeof( err ) );
		(void)written;
		::_exit( 127 );
	}

	::close( out_pipe[1] );
	::close( err_pipe[1] );

	int child_errno = 0;
	ssize_t err_read = 0;
	for (;;) {
		err_read = ::read( err_pipe[0], &child_errno, sizeof( child_errno ) );
		if ( err_read < 0 && errno == EINTR )
			continue;
		break;
	}
	::close( err_pipe[0] );

	char buffer[4096];
	for (;;) {
		ssize_t const n = ::read( out_pipe[0], buffer, sizeof( buffer ) );
		if ( n > 0 ) {
			result.output.append( buffer, static_cast<std::size_t>( n ) );
		} else if ( n == 0 ) {
			break;
		} else if ( errno != EINTR ) {
			break;
		}
	}
	::close( out_pipe[0] );

	int status = 0;
	pid_t waited = -1;
	for (;;) {
		waited = ::waitpid( pid, &status, 0 );
		if ( waited < 0 && errno == EINTR )
			continue;
		break;
	}

	if ( err_read == static_cast<ssize_t>( sizeof( child_errno ) ) ) {
		result.started = false;
		result.exit_code = -1;
		result.launch_error = std::strerror( child_errno );
		return result;
	}

	if ( waited < 0 ) {
		result.started = false;
		result.exit_code = -1;
		result.launch_error = std::strerror( errno );
		return result;
	}

	result.started = true;
	if ( WIFEXITED( status ) )
		result.exit_code = WEXITSTATUS( status );
	else if ( WIFSIGNALED( status ) )
		result.exit_code = 128 + WTERMSIG( status );
	else
		result.exit_code = -1;
	return result;
}

system_memory_info query_system_memory() noexcept
{
	system_memory_info info;
	long const page_sz = ::sysconf( _SC_PAGESIZE );
	if ( page_sz > 0 ) {
		info.page_size = static_cast<u64>( page_sz );
		info.allocation_granularity = static_cast<u64>( page_sz ) * 16;
	}

	std::ifstream meminfo( "/proc/meminfo" );
	std::string key;
	u64 kb = 0;
	std::string unit;
	while ( meminfo >> key >> kb >> unit ) {
		if ( key == "MemTotal:" )
			info.total_physical = kb * 1024ULL;
		else if ( key == "MemAvailable:" )
			info.available_physical = kb * 1024ULL;
	}
	if ( info.available_physical == 0 )
		info.available_physical = info.total_physical;
	return info;
}

scoped_com_init::scoped_com_init( bool const /*multithreaded*/ ) noexcept
	: m_initialized( false )
{
}

scoped_com_init::~scoped_com_init() noexcept = default;

} // namespace os
} // namespace xray
