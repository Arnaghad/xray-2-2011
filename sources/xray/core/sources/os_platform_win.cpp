////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Windows x64 implementation of xray::os PAL
////////////////////////////////////////////////////////////////////////////

#undef WIN32_LEAN_AND_MEAN
#undef NONLS
#include <windows.h>
#include <winnls.h>
#include <objbase.h>

#include "pch.h"
#include <xray/os_platform.h>
#include <xray/os_include.h>

#include <atomic>
#include <string>

namespace xray {
namespace os {

namespace {

std::string format_win32_error( DWORD const code )
{
	if ( code == 0 )
		return {};

	LPSTR buffer = nullptr;
	DWORD const len = FormatMessageA(
		FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
		nullptr,
		code,
		MAKELANGID( LANG_NEUTRAL, SUBLANG_DEFAULT ),
		reinterpret_cast<LPSTR>( &buffer ),
		0,
		nullptr
	);
	std::string msg = ( len > 0 && buffer ) ? std::string( buffer, len ) : ( "Win32 error " + std::to_string( code ) );
	if ( buffer )
		LocalFree( buffer );
	while ( !msg.empty() && ( msg.back() == '\r' || msg.back() == '\n' ) )
		msg.pop_back();
	return msg;
}

} // namespace

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

bool dynamic_library::open( std::filesystem::path const& path, bool const search_library_dir )
{
	close();
	m_last_error.clear();
	if ( path.empty() ) {
		m_last_error = "Empty library path";
		return false;
	}

	HMODULE mod = nullptr;
	if ( search_library_dir && path.is_absolute() ) {
		mod = LoadLibraryExW(
			path.c_str(),
			nullptr,
			LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR
		);
	}
	if ( !mod )
		mod = LoadLibraryW( path.c_str() );

	if ( !mod ) {
		m_last_error = format_win32_error( GetLastError() );
		return false;
	}

	m_handle = static_cast<void*>( mod );
	m_owned = true;
	return true;
}

bool dynamic_library::open_existing( std::filesystem::path const& path )
{
	close();
	m_last_error.clear();
	if ( path.empty() )
		return false;

	HMODULE const mod = GetModuleHandleW( path.c_str() );
	if ( !mod ) {
		m_last_error = format_win32_error( GetLastError() );
		return false;
	}

	m_handle = static_cast<void*>( mod );
	m_owned = false;
	return true;
}

void dynamic_library::close() noexcept
{
	if ( m_handle && m_owned )
		FreeLibrary( static_cast<HMODULE>( m_handle ) );
	m_handle = nullptr;
	m_owned = false;
}

void* dynamic_library::symbol( pcstr const name ) const noexcept
{
	if ( !m_handle || !name )
		return nullptr;
	return reinterpret_cast<void*>( GetProcAddress( static_cast<HMODULE>( m_handle ), name ) );
}

std::string dynamic_library::format_library_name( pcstr const base_name, bool const with_lib_prefix )
{
	if ( !base_name || !*base_name )
		return {};
	std::string result;
	if ( with_lib_prefix )
		result += "lib";
	result += base_name;
	result += ".dll";
	return result;
}

std::filesystem::path executable_path()
{
	std::wstring buffer( 32768, L'\0' );
	DWORD const len = GetModuleFileNameW( nullptr, buffer.data(), static_cast<DWORD>( buffer.size() ) );
	if ( len == 0 || len >= buffer.size() )
		return {};
	buffer.resize( len );
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

	int const wide_len = MultiByteToWideChar( CP_UTF8, 0, name, -1, nullptr, 0 );
	if ( wide_len <= 0 )
		return false;
	std::wstring wide_name( static_cast<std::size_t>( wide_len ), L'\0' );
	MultiByteToWideChar( CP_UTF8, 0, name, -1, wide_name.data(), wide_len );

	DWORD const val_len = GetEnvironmentVariableW( wide_name.c_str(), nullptr, 0 );
	if ( val_len == 0 )
		return false;

	std::wstring wide_val( val_len, L'\0' );
	DWORD const got = GetEnvironmentVariableW( wide_name.c_str(), wide_val.data(), val_len );
	if ( got == 0 )
		return false;
	wide_val.resize( got );

	if ( !wide_val.empty() ) {
		int const utf8_len = WideCharToMultiByte( CP_UTF8, 0, wide_val.data(), static_cast<int>( wide_val.size() ), nullptr, 0, nullptr, nullptr );
		if ( utf8_len > 0 ) {
			out_value.resize( static_cast<std::size_t>( utf8_len ) );
			WideCharToMultiByte( CP_UTF8, 0, wide_val.data(), static_cast<int>( wide_val.size() ), out_value.data(), utf8_len, nullptr, nullptr );
		}
	}
	return true;
}

u32 current_process_id() noexcept
{
	return static_cast<u32>( GetCurrentProcessId() );
}

namespace {

std::wstring utf8_to_wide_win( std::string const& value )
{
	if ( value.empty() )
		return {};
	int const needed = MultiByteToWideChar( CP_UTF8, 0, value.data(), static_cast<int>( value.size() ), nullptr, 0 );
	if ( needed <= 0 )
		return {};
	std::wstring wide( static_cast<std::size_t>( needed ), L'\0' );
	MultiByteToWideChar( CP_UTF8, 0, value.data(), static_cast<int>( value.size() ), wide.data(), needed );
	return wide;
}

std::wstring quote_win_arg( std::wstring const& arg )
{
	if ( !arg.empty() && arg.find_first_of( L" \t\n\v\"" ) == std::wstring::npos )
		return arg;

	std::wstring quoted = L"\"";
	for ( auto it = arg.begin(); ; ++it ) {
		std::size_t backslashes = 0;
		while ( it != arg.end() && *it == L'\\' ) {
			++it;
			++backslashes;
		}
		if ( it == arg.end() ) {
			quoted.append( backslashes * 2, L'\\' );
			break;
		}
		if ( *it == L'"' ) {
			quoted.append( backslashes * 2 + 1, L'\\' );
			quoted.push_back( L'"' );
		} else {
			quoted.append( backslashes, L'\\' );
			quoted.push_back( *it );
		}
	}
	quoted.push_back( L'"' );
	return quoted;
}

} // namespace

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

	SECURITY_ATTRIBUTES sa = {};
	sa.nLength = sizeof( sa );
	sa.bInheritHandle = TRUE;

	HANDLE read_pipe = nullptr;
	HANDLE write_pipe = nullptr;
	if ( !CreatePipe( &read_pipe, &write_pipe, &sa, 0 ) ) {
		result.launch_error = format_win32_error( GetLastError() );
		return result;
	}
	SetHandleInformation( read_pipe, HANDLE_FLAG_INHERIT, 0 );

	std::wstring cmdline = quote_win_arg( executable.wstring() );
	for ( auto const& arg : args ) {
		cmdline.push_back( L' ' );
		cmdline += quote_win_arg( utf8_to_wide_win( arg ) );
	}

	STARTUPINFOW si = {};
	si.cb = sizeof( si );
	si.dwFlags = STARTF_USESTDHANDLES;
	si.hStdInput = reinterpret_cast<HANDLE>( static_cast<LONG_PTR>( -1 ) );
	si.hStdOutput = write_pipe;
	si.hStdError = write_pipe;

	PROCESS_INFORMATION pi = {};
	BOOL const ok = CreateProcessW(
		nullptr,
		cmdline.data(),
		nullptr,
		nullptr,
		TRUE,
		CREATE_NO_WINDOW,
		nullptr,
		nullptr,
		&si,
		&pi
	);

	CloseHandle( write_pipe );

	if ( !ok ) {
		DWORD const err = GetLastError();
		CloseHandle( read_pipe );
		result.launch_error = format_win32_error( err );
		return result;
	}

	char buffer[4096];
	DWORD bytes_read = 0;
	while ( ReadFile( read_pipe, buffer, sizeof( buffer ), &bytes_read, nullptr ) && bytes_read > 0 ) {
		result.output.append( buffer, static_cast<std::size_t>( bytes_read ) );
	}
	CloseHandle( read_pipe );

	WaitForSingleObject( pi.hProcess, INFINITE );
	DWORD exit_code = 1;
	GetExitCodeProcess( pi.hProcess, &exit_code );
	CloseHandle( pi.hThread );
	CloseHandle( pi.hProcess );

	result.started = true;
	result.exit_code = static_cast<int>( exit_code );
	return result;
}

system_memory_info query_system_memory() noexcept
{
	system_memory_info info;
	SYSTEM_INFO sys = {};
	GetSystemInfo( &sys );
	if ( sys.dwPageSize )
		info.page_size = sys.dwPageSize;
	if ( sys.dwAllocationGranularity )
		info.allocation_granularity = sys.dwAllocationGranularity;

	MEMORYSTATUSEX mem = {};
	mem.dwLength = sizeof( mem );
	if ( GlobalMemoryStatusEx( &mem ) ) {
		info.total_physical = mem.ullTotalPhys;
		info.available_physical = mem.ullAvailPhys;
	}
	return info;
}

scoped_com_init::scoped_com_init( bool const multithreaded ) noexcept
{
	HRESULT const hr = CoInitializeEx( nullptr, multithreaded ? COINIT_MULTITHREADED : COINIT_APARTMENTTHREADED );
	m_initialized = ( hr == S_OK || hr == S_FALSE );
}

scoped_com_init::~scoped_com_init() noexcept
{
	if ( m_initialized ) {
		CoUninitialize();
		m_initialized = false;
	}
}

} // namespace os
} // namespace xray
