////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX recursive mutex backend for xray::threading
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/threading_mutex.h>
#include <pthread.h>

using xray::threading::mutex_tasks_unaware;

mutex_tasks_unaware::mutex_tasks_unaware()
{
	static_assert( sizeof( pthread_mutex_t ) <= sizeof( m_mutex ), "m_mutex backing storage too small for pthread_mutex_t" );
	static_assert( alignof( u64 ) >= alignof( pthread_mutex_t ), "m_mutex alignment mismatch" );

	pthread_mutexattr_t attr;
	int rc = pthread_mutexattr_init( &attr );
	R_ASSERT_U( rc == 0 );
	rc = pthread_mutexattr_settype( &attr, PTHREAD_MUTEX_RECURSIVE );
	R_ASSERT_U( rc == 0 );
	rc = pthread_mutex_init( reinterpret_cast<pthread_mutex_t*>( m_mutex ), &attr );
	R_ASSERT_U( rc == 0 );
	pthread_mutexattr_destroy( &attr );
}

mutex_tasks_unaware::~mutex_tasks_unaware()
{
	pthread_mutex_destroy( reinterpret_cast<pthread_mutex_t*>( m_mutex ) );
}

void mutex_tasks_unaware::lock() const
{
	pthread_mutex_lock( reinterpret_cast<pthread_mutex_t*>( m_mutex ) );
}

void mutex_tasks_unaware::unlock() const
{
	pthread_mutex_unlock( reinterpret_cast<pthread_mutex_t*>( m_mutex ) );
}

bool mutex_tasks_unaware::try_lock() const
{
	return pthread_mutex_trylock( reinterpret_cast<pthread_mutex_t*>( m_mutex ) ) == 0;
}
