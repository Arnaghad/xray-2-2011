////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Linux futex auto-reset event backend for xray::threading
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/threading_event.h>

#include <atomic>
#include <cerrno>
#include <cstdint>
#include <limits.h>
#include <linux/futex.h>
#include <sys/syscall.h>
#include <time.h>
#include <unistd.h>

namespace xray {
namespace threading {

extern XRAY_CORE_API u32 const wait_time_infinite = u32( -1 );

namespace {

std::atomic<u32>* futex_word( char* storage ) noexcept
{
	return reinterpret_cast<std::atomic<u32>*>( storage );
}

int futex_wait_bitset( std::atomic<u32>* addr, u32 expected, struct timespec const* abs_timeout ) noexcept
{
	return static_cast<int>( ::syscall( SYS_futex, addr, FUTEX_WAIT_BITSET_PRIVATE, expected, abs_timeout, nullptr, FUTEX_BITSET_MATCH_ANY ) );
}

int futex_wake_one( std::atomic<u32>* addr ) noexcept
{
	return static_cast<int>( ::syscall( SYS_futex, addr, FUTEX_WAKE_PRIVATE, 1, nullptr, nullptr, 0 ) );
}

} // namespace

event_tasks_unaware::event_tasks_unaware( bool const initial_state )
{
	static_assert( sizeof( std::atomic<u32> ) <= sizeof( m_event ), "m_event storage too small for futex word" );
	static_assert( alignof( std::atomic<u32> ) <= 16, "m_event alignment insufficient" );
	new ( m_event ) std::atomic<u32>( initial_state ? 1u : 0u );
}

event_tasks_unaware::~event_tasks_unaware()
{
	futex_word( m_event )->~atomic<u32>();
}

void event_tasks_unaware::set( bool const value )
{
	auto* state = futex_word( m_event );
	if ( value ) {
		u32 prev = state->exchange( 1u, std::memory_order_release );
		if ( prev == 0u )
			futex_wake_one( state );
	} else {
		state->store( 0u, std::memory_order_release );
	}
}

event_tasks_unaware::wait_result_enum event_tasks_unaware::wait( u32 const max_wait_time_ms )
{
	auto* state = futex_word( m_event );
	u32 expected = 1u;
	if ( state->compare_exchange_strong( expected, 0u, std::memory_order_acquire, std::memory_order_relaxed ) )
		return wait_result_signaled;

	if ( max_wait_time_ms == 0u )
		return wait_result_still_nonsignaled;

	struct timespec deadline = {};
	bool const has_timeout = ( max_wait_time_ms != wait_time_infinite );
	if ( has_timeout ) {
		::clock_gettime( CLOCK_MONOTONIC, &deadline );
		std::uint64_t const add_ns = static_cast<std::uint64_t>( max_wait_time_ms ) * 1000000ULL;
		deadline.tv_sec += static_cast<time_t>( add_ns / 1000000000ULL );
		deadline.tv_nsec += static_cast<long>( add_ns % 1000000000ULL );
		if ( deadline.tv_nsec >= 1000000000L ) {
			deadline.tv_sec += 1;
			deadline.tv_nsec -= 1000000000L;
		}
	}

	for ( ;; ) {
		expected = 1u;
		if ( state->compare_exchange_strong( expected, 0u, std::memory_order_acquire, std::memory_order_relaxed ) )
			return wait_result_signaled;

		int const rc = futex_wait_bitset( state, 0u, has_timeout ? &deadline : nullptr );
		if ( rc == -1 ) {
			if ( errno == ETIMEDOUT )
				return wait_result_still_nonsignaled;
			if ( errno == EAGAIN || errno == EINTR )
				continue;
			return wait_result_failed;
		}
	}
}

} // namespace threading
} // namespace xray
