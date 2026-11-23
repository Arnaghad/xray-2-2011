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

#	pragma warning(push)
#	pragma warning(disable: 4201)
#	include <mmsystem.h>
#	pragma warning(pop)
#	pragma comment(lib, "winmm.lib")



using cs::core::platform;

platform						cs::core::g_platform;

platform::platform				( )
{
	timeBeginPeriod				( 1 );

	QueryPerformanceFrequency	( (PLARGE_INTEGER)&qpc_per_second );
}

u64 platform::get_QPC			( )
{
	u64 result;
	QueryPerformanceCounter		( (PLARGE_INTEGER)&result );
	return						result;
}
