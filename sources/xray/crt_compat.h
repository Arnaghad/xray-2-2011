////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / C11 compatibility shims for legacy MSVC CRT helpers
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_CRT_COMPAT_H_INCLUDED
#define XRAY_CRT_COMPAT_H_INCLUDED

#include <xray/macro_platform.h>

#if !XRAY_PLATFORM_WINDOWS
#	include <algorithm>
#	include <cstdarg>
#	include <cstdio>
#	include <cstring>
#	include <strings.h>
#	include <time.h>

#	ifndef _TRUNCATE
#		define _TRUNCATE (static_cast<std::size_t>(-1))
#	endif

#	ifndef _snprintf
#		define _snprintf std::snprintf
#	endif

#	ifndef _vsnprintf
#		define _vsnprintf std::vsnprintf
#	endif

#	ifndef _stricmp
#		define _stricmp ::strcasecmp
#	endif

#	ifndef _strnicmp
#		define _strnicmp ::strncasecmp
#	endif

inline int sprintf_s( char* const buffer, std::size_t const size_in_bytes, char const* const format, ... )
{
	if ( !buffer || !size_in_bytes || !format )
		return -1;
	std::va_list args;
	va_start( args, format );
	int const res = std::vsnprintf( buffer, size_in_bytes, format, args );
	va_end( args );
	return res;
}

template <std::size_t N>
inline int sprintf_s( char (&buffer)[N], char const* const format, ... )
{
	if ( !format )
		return -1;
	std::va_list args;
	va_start( args, format );
	int const res = std::vsnprintf( buffer, N, format, args );
	va_end( args );
	return res;
}

inline int vsprintf_s( char* const buffer, std::size_t const size_in_bytes, char const* const format, std::va_list args )
{
	if ( !buffer || !size_in_bytes || !format )
		return -1;
	return std::vsnprintf( buffer, size_in_bytes, format, args );
}

template <std::size_t N>
inline int vsprintf_s( char (&buffer)[N], char const* const format, std::va_list args )
{
	return std::vsnprintf( buffer, N, format, args );
}

inline int strcpy_s( char* const dest, std::size_t const dest_size, char const* const src )
{
	if ( !dest || !dest_size || !src )
		return 22;
	std::size_t const len = std::strlen( src );
	if ( len >= dest_size ) {
		dest[0] = '\0';
		return 34;
	}
	std::memcpy( dest, src, len + 1 );
	return 0;
}

template <std::size_t N>
inline int strcpy_s( char (&dest)[N], char const* const src )
{
	return strcpy_s( dest, N, src );
}

inline int strncpy_s( char* const dest, std::size_t const dest_size, char const* const src, std::size_t const count )
{
	if ( !dest || !dest_size || !src )
		return 22;
	std::size_t const to_copy = ( count == _TRUNCATE )
		? std::min( std::strlen( src ), dest_size - 1 )
		: std::min( { std::strlen( src ), count, dest_size - 1 } );
	std::memcpy( dest, src, to_copy );
	dest[to_copy] = '\0';
	return 0;
}

template <std::size_t N>
inline int strncpy_s( char (&dest)[N], char const* const src, std::size_t const count )
{
	return strncpy_s( dest, N, src, count );
}

inline int strcat_s( char* const dest, std::size_t const dest_size, char const* const src )
{
	if ( !dest || !dest_size || !src )
		return 22;
	std::size_t const cur = ::strnlen( dest, dest_size );
	if ( cur >= dest_size )
		return 22;
	return strcpy_s( dest + cur, dest_size - cur, src );
}

template <std::size_t N>
inline int strcat_s( char (&dest)[N], char const* const src )
{
	return strcat_s( dest, N, src );
}

inline int localtime_s( struct tm* const out_tm, time_t const* const timer )
{
	if ( !out_tm || !timer )
		return 22;
	return ::localtime_r( timer, out_tm ) ? 0 : 22;
}

inline int gmtime_s( struct tm* const out_tm, time_t const* const timer )
{
	if ( !out_tm || !timer )
		return 22;
	return ::gmtime_r( timer, out_tm ) ? 0 : 22;
}

#endif // #if !XRAY_PLATFORM_WINDOWS

#endif // #ifndef XRAY_CRT_COMPAT_H_INCLUDED
