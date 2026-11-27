////////////////////////////////////////////////////////////////////////////
//	Created 	: 25.09.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/os_platform.h>
#include <xray/os_functions.h>
#include <atomic>
#include <string>

namespace xray {
namespace os {

namespace detail {
	bool	platform_clipboard_is_empty();
	void	platform_clipboard_copy( pcstr buffer );
	pstr	platform_clipboard_paste( xray::memory::base_allocator& allocator );
	void	platform_clipboard_update( pcstr string );
} // namespace detail

namespace {
	std::atomic<clipboard_provider const*> g_clipboard_provider{ nullptr };
} // namespace

void set_clipboard_provider( clipboard_provider const* const provider ) noexcept
{
	g_clipboard_provider.store( provider, std::memory_order_release );
}

clipboard_provider const* get_clipboard_provider() noexcept
{
	return g_clipboard_provider.load( std::memory_order_acquire );
}

bool is_clipboard_empty()
{
	if ( auto const* provider = get_clipboard_provider(); provider && provider->is_empty )
		return provider->is_empty();
	return detail::platform_clipboard_is_empty();
}

void copy_to_clipboard( pcstr const buffer )
{
	if ( auto const* provider = get_clipboard_provider(); provider && provider->copy ) {
		provider->copy( buffer );
		return;
	}
	detail::platform_clipboard_copy( buffer );
}

pstr paste_from_clipboard( xray::memory::base_allocator& allocator )
{
	if ( auto const* provider = get_clipboard_provider(); provider && provider->paste )
		return provider->paste( allocator );
	return detail::platform_clipboard_paste( allocator );
}

void update_clipboard( pcstr const string )
{
	if ( !string )
		return;

	if ( auto const* provider = get_clipboard_provider(); provider && provider->copy && provider->paste ) {
		std::string combined;
		if ( pstr existing = provider->paste( *::xray::memory::g_mt_allocator ) ) {
			combined = existing;
			XRAY_FREE_IMPL( *::xray::memory::g_mt_allocator, existing );
		}
		combined += string;
		provider->copy( combined.c_str() );
		return;
	}
	detail::platform_clipboard_update( string );
}

} // namespace os
} // namespace xray
