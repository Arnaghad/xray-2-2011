////////////////////////////////////////////////////////////////////////////
//	Created		: 10.08.2010
//	Author		: Konstantin Slipchenko
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <float.h>

#ifdef __MINGW32__
#	define _PC_24		0x00020000
#	define MCW_PC		0x00030000
#	define _RC_NEAR		0x00000000
#	define MCW_RC		0x00000300
#	define _IC_PROJECTIVE	0x00000000
#	define _MCW_IC		0x00040000
#	define _DN_FLUSH	0x01000000
#	define _MCW_DN		0x03000000
#endif

static xray::command_line::key		s_floating_point_control_disabled("floating_point_control_disabled", "", "math", "disables floating point control flags setup for each thread");

namespace xray {
namespace math {

void on_thread_spawn	( )
{
	if ( s_floating_point_control_disabled )
		return;

#ifndef __MINGW32__
	errno_t		error;
	
	error		= _controlfp_s( 0, _PC_24, MCW_PC );
	R_ASSERT_U	( !error );

	error		= _controlfp_s( 0, _RC_NEAR, MCW_RC );
	R_ASSERT_U	( !error );

	_MM_SET_ROUNDING_MODE(_MM_ROUND_NEAREST);

	error		= _controlfp_s( 0, _IC_PROJECTIVE, _MCW_IC );
	R_ASSERT_U	( !error );

	error		= _controlfp_s( 0, _DN_FLUSH, _MCW_DN );
	R_ASSERT_U	( !error );

	_MM_SET_FLUSH_ZERO_MODE(_MM_FLUSH_ZERO_ON);
#else
	// MinGW implementation or skip for now if not critical
	_controlfp( _PC_24, MCW_PC );
	_controlfp( _RC_NEAR, MCW_RC );
	_controlfp( _IC_PROJECTIVE, _MCW_IC );
	_controlfp( _DN_FLUSH, _MCW_DN );

	_MM_SET_ROUNDING_MODE(_MM_ROUND_NEAREST);
	_MM_SET_FLUSH_ZERO_MODE(_MM_FLUSH_ZERO_ON);

#endif
}

} // namespace math
} // namespace xray
