////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux monotonic timing backend
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "timing.h"
#include <time.h>

u64	 xray::timing::g_qpc_per_second				= 1000000000ULL;
bool xray::timing::g_cpu_supports_time_stamp	= false;

void xray::timing::initialize()
{
	g_qpc_per_second = 1000000000ULL;
	g_cpu_supports_time_stamp = false;
}

u64 xray::timing::query_performance_counter()
{
	struct timespec ts = {};
	::clock_gettime( CLOCK_MONOTONIC_RAW, &ts );
	return static_cast<u64>( ts.tv_sec ) * 1000000000ULL + static_cast<u64>( ts.tv_nsec );
}
