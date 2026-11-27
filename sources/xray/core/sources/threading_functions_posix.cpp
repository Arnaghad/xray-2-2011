////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux threading implementation for xray::threading
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "threading_functions.h"
#include <xray/debug/debug.h>

#include <algorithm>
#include <cstring>
#include <errno.h>
#include <pthread.h>
#include <sched.h>
#include <sys/syscall.h>
#include <time.h>
#include <unistd.h>

namespace xray {

namespace tasks {
	void on_current_thread_locks();
	void on_current_thread_unlocks();
} // namespace tasks

namespace threading {

void set_current_thread_affinity_impl( u32 const hardware_thread )
{
	cpu_set_t cpuset;
	CPU_ZERO( &cpuset );
	if ( hardware_thread < CPU_SETSIZE ) {
		CPU_SET( hardware_thread, &cpuset );
		pthread_setaffinity_np( pthread_self(), sizeof( cpu_set_t ), &cpuset );
	}
}

u32 actual_core_count()
{
	long const count = ::sysconf( _SC_NPROCESSORS_ONLN );
	return count > 0 ? static_cast<u32>( count ) : 1u;
}

u32 current_thread_id()
{
	return static_cast<u32>( ::syscall( SYS_gettid ) );
}

void yield_tasks_unaware( u32 const yield_time_in_ms )
{
	if ( yield_time_in_ms ) {
		struct timespec req = {};
		req.tv_sec = static_cast<time_t>( yield_time_in_ms / 1000u );
		req.tv_nsec = static_cast<long>( ( yield_time_in_ms % 1000u ) * 1000000UL );
		while ( ::nanosleep( &req, &req ) == -1 && errno == EINTR ) {}
		return;
	}
	::sched_yield();
}

void yield( u32 const yield_time_in_ms )
{
	if ( yield_time_in_ms )
		tasks::on_current_thread_locks();

	yield_tasks_unaware( yield_time_in_ms );

	if ( yield_time_in_ms )
		tasks::on_current_thread_unlocks();
}

void set_thread_name_impl( pcstr const name_for_debugger )
{
#ifndef MASTER_GOLD
	if ( !name_for_debugger || !*name_for_debugger )
		return;
	char truncated[16] = {};
	std::size_t const len = std::min<std::size_t>( std::strlen( name_for_debugger ), 15 );
	std::memcpy( truncated, name_for_debugger, len );
	truncated[len] = '\0';
	pthread_setname_np( pthread_self(), truncated );
#else
	XRAY_UNREFERENCED_PARAMETER( name_for_debugger );
#endif
}

u32 tls_create_key()
{
	pthread_key_t key = 0;
	int const rc = pthread_key_create( &key, nullptr );
	R_ASSERT_U( rc == 0 );
	return static_cast<u32>( key );
}

void tls_delete_key( u32 const key )
{
	pthread_key_delete( static_cast<pthread_key_t>( key ) );
}

void tls_set_value( u32 const key, pvoid const value )
{
	pthread_setspecific( static_cast<pthread_key_t>( key ), value );
}

pvoid tls_get_value( u32 const key )
{
	return pthread_getspecific( static_cast<pthread_key_t>( key ) );
}

u32 tls_get_invalid_key()
{
	return u32( -1 );
}

bool tls_is_valid_key( u32 const key )
{
	return key != tls_get_invalid_key();
}

void thread_entry( pvoid argument );

namespace {

struct posix_spawn_bootstrap {
	thread_entry_params*	params = nullptr;
	std::atomic<u32>		tid{ 0 };
	std::atomic<bool>		tid_ready{ false };
};

void* thread_entry_protected( pvoid argument )
{
	auto* boot = static_cast<posix_spawn_bootstrap*>( argument );
	thread_entry_params* const params = boot->params;
	boot->tid.store( current_thread_id(), std::memory_order_release );
	boot->tid_ready.store( true, std::memory_order_release );

	xray::debug::protected_call( &thread_entry, params );
	return nullptr;
}

} // namespace

thread_id_type spawn_internal( thread_entry_params& argument, u32 const stack_size )
{
	pthread_attr_t attr;
	pthread_attr_init( &attr );
	if ( stack_size > 0 ) {
		long const min_stack = ::sysconf( _SC_THREAD_STACK_MIN );
		std::size_t const effective_stack = std::max<std::size_t>(
			stack_size,
			min_stack > 0 ? static_cast<std::size_t>( min_stack ) : 65536u
		);
		pthread_attr_setstacksize( &attr, effective_stack );
	}
	pthread_attr_setdetachstate( &attr, PTHREAD_CREATE_DETACHED );

	posix_spawn_bootstrap boot;
	boot.params = &argument;

	pthread_t thread_handle = {};
	int const rc = pthread_create( &thread_handle, &attr, &thread_entry_protected, &boot );
	pthread_attr_destroy( &attr );
	R_ASSERT_U( rc == 0, "can't spawn thread!" );

	while ( !boot.tid_ready.load( std::memory_order_acquire ) )
		::sched_yield();

	return boot.tid.load( std::memory_order_acquire );
}

void increase_thread_priority()
{
	// Increasing thread priority on Linux requires CAP_SYS_NICE; keep default scheduling safely.
}

} // namespace threading
} // namespace xray
