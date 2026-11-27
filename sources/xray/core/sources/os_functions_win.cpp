////////////////////////////////////////////////////////////////////////////
//	Created 	: 25.09.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#undef WIN32_LEAN_AND_MEAN
#undef NOGDI
#include <windows.h>
#include "pch.h"
#include <xray/linkage_helper.h>

#include <xray/os_preinclude.h>
#undef NOCLIPBOARD
#undef NOUSER
#include <xray/os_include.h>

XRAY_DECLARE_LINKAGE_ID(core_os_functions_win)

using namespace xray;

namespace xray {
namespace os {
namespace detail {

bool platform_clipboard_is_empty				( )
{
	if ( !OpenClipboard(0) )
		return					false;

	HGLOBAL const handle		= GetClipboardData( CF_TEXT );
	if ( !handle ) {
		CloseClipboard			( );
		return					false;
	}

	pcstr const data			= static_cast<pcstr>( GlobalLock(handle) );
	u32 const string_length		= strings::length(data);

	CloseClipboard				( );
	return						( !string_length );
}

void platform_clipboard_copy					( pcstr buffer )
{
	if ( !OpenClipboard(0) )
		return;

	u32 const handle_size = ( strings::length(buffer) + 1 ) * sizeof(char);
	HGLOBAL const handle = GlobalAlloc( GHND, handle_size );
	if ( !handle ) {
		CloseClipboard			();
		return;
	}

	char* const memory			= static_cast<char*>( GlobalLock(handle) );
	strings::copy					( memory, handle_size, buffer );
	GlobalUnlock				( handle );
	EmptyClipboard				( );
	SetClipboardData			( CF_TEXT, handle );
	CloseClipboard				( );
}

pstr platform_clipboard_paste					( xray::memory::base_allocator& allocator )
{
	if ( !OpenClipboard(0) )
		return					0;

	HGLOBAL const handle		= GetClipboardData( CF_TEXT );
	if ( !handle ) {
		CloseClipboard			( );
		return					0;
	}

	pcstr const data			= static_cast<pcstr>( GlobalLock(handle) );
	u32 const string_length		= strings::length(data);
	if ( !string_length ) {
		CloseClipboard			( );
		return					0;
	}

	u32 const buffer_size		= string_length + 1;
	pstr const result			= (pstr)XRAY_MALLOC_IMPL( allocator, buffer_size*sizeof(char), "buffer for pasting from clipboard");
	strings::copy				( result, buffer_size, data );
	result[buffer_size-1]		= 0;
	GlobalUnlock				( handle );
	CloseClipboard				( );
	return						result;
}

void platform_clipboard_update					( pcstr const string )
{
	if ( !OpenClipboard(0) )
		return;

	HGLOBAL const handle		= GetClipboardData(CF_TEXT);
	if (!handle) {
		CloseClipboard			( );
		platform_clipboard_copy	( string );
		return;
	}

	LPSTR const memory			= static_cast<pstr>( GlobalLock(handle) );
	u32 const memory_length		= strings::length(memory);
	u32 const string_length		= strings::length(string);
	u32 const buffer_size		= (memory_length + string_length + 1) * sizeof(char);
	LPSTR const buffer			= (LPSTR)ALLOCA( buffer_size );
	strings::copy				( buffer, buffer_size, memory );
	GlobalUnlock				( handle );

	strings::append				( buffer, buffer_size, string );
	CloseClipboard				( );
	platform_clipboard_copy		( buffer );
}

} // namespace detail
} // namespace os
} // namespace xray
