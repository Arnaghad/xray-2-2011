////////////////////////////////////////////////////////////////////////////
//	Created 	: 13.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_PLATFORM_EXTENSIONS_H_INCLUDED
#define XRAY_PLATFORM_EXTENSIONS_H_INCLUDED

#include <xray/macro_platform.h>

namespace xray {
namespace platform {

inline bool	little_endian	( )
{
	u16	const word	= 0x0001;
	return			( !!*( u8 const* )&word );
}

inline bool	big_endian		( )
{
	return			( !little_endian( ) );
}

inline pcstr platform_name ( )
{
#if XRAY_PLATFORM_WINDOWS
	return "windows";
#elif XRAY_PLATFORM_LINUX
	return "linux";
#else
#	error unknown platform
#endif
}

u64		XRAY_CORE_API	get_local_video_memory_size			( );
u64		XRAY_CORE_API	get_minimal_local_video_memory_size	( );

} // namespace platform
} // namespace xray

#endif // #ifndef XRAY_PLATFORM_EXTENSIONS_H_INCLUDED
