////////////////////////////////////////////////////////////////////////////
//	Created 	: 25.09.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/threading_mutex.h>

#if !defined(_WIN32_WINNT) || (_WIN32_WINNT < 0x0601)
#	undef _WIN32_WINNT
#	define _WIN32_WINNT 0x0601			// for TryEnterCriticalSection
#endif
#include <xray/os_include.h>

using xray::threading::mutex_tasks_unaware;

mutex_tasks_unaware::mutex_tasks_unaware		()
{
	COMPILE_ASSERT				( sizeof( CRITICAL_SECTION ) <= sizeof( m_mutex ), please_change_buffer_size );
//	InitializeCriticalSection	( &( CRITICAL_SECTION& )m_mutex );
	BOOL const result			= 
		InitializeCriticalSectionAndSpinCount( &( CRITICAL_SECTION& )m_mutex, 10000 );

	if ( !result )
		NODEFAULT				( );
}

mutex_tasks_unaware::~mutex_tasks_unaware		()
{
	DeleteCriticalSection		( &( CRITICAL_SECTION& )m_mutex );
}

void mutex_tasks_unaware::lock	() const
{
//	debug::printf				(temp, "lock   0x%x[%d]\r\n", *(u32*)&m_mutex, GetCurrentThreadId());
	EnterCriticalSection		( &( CRITICAL_SECTION& )m_mutex );
}

void mutex_tasks_unaware::unlock	() const
{
//	debug::printf				(temp, "unlock 0x%x[%d]\r\n", *(u32*)&m_mutex, GetCurrentThreadId());
	LeaveCriticalSection		( &( CRITICAL_SECTION& )m_mutex );
}

bool mutex_tasks_unaware::try_lock	() const
{
	return						( !!TryEnterCriticalSection( &( CRITICAL_SECTION& )m_mutex ) );
}
