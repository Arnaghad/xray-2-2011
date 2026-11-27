////////////////////////////////////////////////////////////////////////////
//	Module 		: platform.cpp
//	Created 	: 27.08.2006
//  Modified 	: 27.08.2006
//	Author		: Dmitriy Iassenev
//	Description : platform class
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <cs/core/platform.h>
#include <cs/core/os_include.h>		// QueryPerformanceFrequency

#if defined(_WIN32)
#	pragma warning(push)
#	pragma warning(disable: 4201)
#	include <mmsystem.h>
#	pragma warning(pop)
#	pragma comment(lib, "winmm.lib")
#else
#	include <time.h>
#endif

using cs::core::platform;

platform						cs::core::g_platform;

platform::platform				( )
{
#if defined(_WIN32)
	timeBeginPeriod				( 1 );

	QueryPerformanceFrequency	( (PLARGE_INTEGER)&qpc_per_second );
#else
	qpc_per_second				= 1000000000ULL;
#endif
}

u64 platform::get_QPC			( )
{
#if defined(_WIN32)
	u64 result;
	QueryPerformanceCounter		( (PLARGE_INTEGER)&result );
	return						result;
#else
	struct timespec ts = {};
	::clock_gettime				( CLOCK_MONOTONIC_RAW, &ts );
	return						static_cast<u64>( ts.tv_sec ) * 1000000000ULL + static_cast<u64>( ts.tv_nsec );
#endif
}
