////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux clipboard fallback for xray::os
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/os_functions.h>
#include <mutex>
#include <string>

namespace xray {
namespace os {
namespace detail {

namespace {
	std::mutex& clipboard_mutex()
	{
		static std::mutex m;
		return m;
	}

	std::string& clipboard_storage()
	{
		static std::string text;
		return text;
	}
} // namespace

bool platform_clipboard_is_empty()
{
	std::lock_guard<std::mutex> lock( clipboard_mutex() );
	return clipboard_storage().empty();
}

void platform_clipboard_copy( pcstr const buffer )
{
	std::lock_guard<std::mutex> lock( clipboard_mutex() );
	clipboard_storage() = buffer ? buffer : "";
}

pstr platform_clipboard_paste( xray::memory::base_allocator& allocator )
{
	std::lock_guard<std::mutex> lock( clipboard_mutex() );
	auto const& src = clipboard_storage();
	if ( src.empty() )
		return nullptr;

	u32 const buffer_size = static_cast<u32>( src.size() + 1 );
	pstr const result = static_cast<pstr>( XRAY_MALLOC_IMPL( allocator, buffer_size * sizeof( char ), "buffer for pasting from clipboard" ) );
	strings::copy( result, buffer_size, src.c_str() );
	result[buffer_size - 1] = '\0';
	return result;
}

void platform_clipboard_update( pcstr const string )
{
	if ( !string )
		return;
	std::lock_guard<std::mutex> lock( clipboard_mutex() );
	clipboard_storage() += string;
}

} // namespace detail
} // namespace os
} // namespace xray
