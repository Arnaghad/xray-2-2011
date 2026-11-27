////////////////////////////////////////////////////////////////////////////
//	Created 	: 22.01.2009
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_TIMING_FUNCTIONS_H_INCLUDED
#define XRAY_TIMING_FUNCTIONS_H_INCLUDED

#if defined(__x86_64__) || defined(_M_X64)
#	if defined(_MSC_VER)
#		include <intrin.h>
#		pragma intrinsic(__rdtsc)
#	elif defined(__GNUC__) || defined(__clang__)
#		include <x86intrin.h>
#	endif
#endif

namespace xray {
namespace timing {

XRAY_CORE_API	extern u64	g_qpc_per_second;
XRAY_CORE_API	u64			query_performance_counter	( );

XRAY_CORE_API	extern bool	g_cpu_supports_time_stamp;

inline			u64			get_clocks					( )
{
#if defined(__x86_64__) || defined(_M_X64)
	return		( __rdtsc( ) );
#else
	return		( query_performance_counter( ) );
#endif
}

inline u64					get_QPC						( )
{
	if ( g_cpu_supports_time_stamp )
	{
		return		get_clocks( );
	}
	else
	{
		return		( query_performance_counter( ) );
	}
}

} // namespace timing
} // namespace xray

#endif // #ifndef XRAY_TIMING_FUNCTIONS_H_INCLUDED
