////////////////////////////////////////////////////////////////////////////
//	Module 		: threading_mutex.cpp
//	Created 	: 26.08.2006
//  Modified 	: 26.08.2006
//	Author		: Dmitriy Iassenev
//	Description : threading mutex class
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <cs/core/threading_mutex.h>
#include <cs/core/os_include.h>			// mutex_type and the others

typedef CRITICAL_SECTION mutex_type;
static_assert(sizeof(mutex_type) == 40, "Unexpected Windows x64 mutex ABI");
static_assert(alignof(threading::mutex) >= alignof(mutex_type), "Mutex storage must be aligned");

threading::mutex::mutex			()
{
	STATIC_CHECK ( sizeof(mutex_type) <= sizeof(m_mutex), buffer_size_doesnt_correspond_to_the_actual_length );

	InitializeCriticalSection	( (mutex_type*)&m_mutex );
}

threading::mutex::~mutex		()
{
	DeleteCriticalSection		( (mutex_type*)&m_mutex );
}

void threading::mutex::lock		()
{
//	string256					temp;
//	sprintf_s					(temp, "lock   0x%x[%d]\r\n", *(u32*)&m_mutex, GetCurrentThreadId());
//	OUTPUT_DEBUG_STRING			(temp);
	EnterCriticalSection		( (mutex_type*)&m_mutex );
}

void threading::mutex::unlock	()
{
//	string256					temp;
//	sprintf_s					(temp, "unlock 0x%x[%d]\r\n", *(u32*)&m_mutex, GetCurrentThreadId());
//	OUTPUT_DEBUG_STRING			(temp);
	LeaveCriticalSection		( (mutex_type*)&m_mutex );
}

bool threading::mutex::trylock	()
{
	return						!!TryEnterCriticalSection((mutex_type*)&m_mutex);
}
