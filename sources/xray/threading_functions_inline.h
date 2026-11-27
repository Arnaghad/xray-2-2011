////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Cross-platform atomic operations and memory barriers
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_THREADING_FUNCTIONS_INLINE_H_INCLUDED
#define XRAY_THREADING_FUNCTIONS_INLINE_H_INCLUDED

#include <atomic>

#if defined(_MSC_VER) && !defined(__clang__) && !defined(__GNUC__)
#	include <intrin.h>
#	pragma intrinsic(_InterlockedCompareExchange,		\
					  _InterlockedCompareExchange64,	\
					  _InterlockedExchange,				\
					  _InterlockedIncrement,			\
					  _InterlockedDecrement,			\
					  _InterlockedAnd,					\
					  _InterlockedOr,					\
					  _InterlockedXor,					\
					  _InterlockedExchangeAdd			\
					 )
#	pragma intrinsic(_InterlockedExchangePointer)
#endif

namespace xray {
namespace threading {

XRAY_THREADING_INLINE void memory_barrier()
{
	std::atomic_thread_fence( std::memory_order_seq_cst );
}

#if defined(__GNUC__) || defined(__clang__)

XRAY_THREADING_INLINE atomic32_value_type interlocked_increment( atomic32_type& value )
{
	return __atomic_add_fetch( &value, 1, __ATOMIC_SEQ_CST );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_decrement( atomic32_type& value )
{
	return __atomic_sub_fetch( &value, 1, __ATOMIC_SEQ_CST );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_exchange_add( atomic32_type& value, atomic32_value_type increment )
{
	return __atomic_fetch_add( &value, increment, __ATOMIC_SEQ_CST );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_exchange_sub( atomic32_type& value, atomic32_value_type subtract )
{
	return __atomic_fetch_sub( &value, subtract, __ATOMIC_SEQ_CST );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_and( atomic32_type& value, atomic32_value_type mask )
{
	return __atomic_fetch_and( &value, mask, __ATOMIC_SEQ_CST );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_or( atomic32_type& value, atomic32_value_type mask )
{
	return __atomic_fetch_or( &value, mask, __ATOMIC_SEQ_CST );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_exchange( atomic32_type& target, atomic32_value_type value )
{
	return __atomic_exchange_n( &target, value, __ATOMIC_SEQ_CST );
}

XRAY_THREADING_INLINE atomic_ptr_value_type interlocked_exchange_pointer( atomic_ptr_type& target, atomic_ptr_value_type const value )
{
	return __atomic_exchange_n( &target, const_cast<atomic_ptr_value_type>(value), __ATOMIC_SEQ_CST );
}

template <typename T, typename U>
XRAY_THREADING_INLINE T* interlocked_exchange_pointer( T* volatile& target, U* value )
{
	return (T*)interlocked_exchange_pointer( (atomic_ptr_type&)target, (atomic_ptr_value_type)value );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_compare_exchange( atomic32_type& target, atomic32_value_type exchange, atomic32_value_type comperand )
{
	atomic32_value_type expected = comperand;
	__atomic_compare_exchange_n( &target, &expected, exchange, false, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST );
	return expected;
}

XRAY_THREADING_INLINE atomic64_value_type interlocked_compare_exchange( atomic64_type& target, atomic64_value_type exchange, atomic64_value_type comperand )
{
	atomic64_value_type expected = comperand;
	__atomic_compare_exchange_n( &target, &expected, exchange, false, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST );
	return expected;
}

XRAY_THREADING_INLINE atomic64_value_type interlocked_exchange( atomic64_type& target, atomic64_value_type exchange )
{
	return __atomic_exchange_n( &target, exchange, __ATOMIC_SEQ_CST );
}

XRAY_THREADING_INLINE atomic_ptr_value_type interlocked_compare_exchange_pointer( atomic_ptr_type& target, atomic_ptr_value_type const exchange, atomic_ptr_value_type const comperand )
{
	atomic_ptr_value_type expected = const_cast<atomic_ptr_value_type>( comperand );
	__atomic_compare_exchange_n( &target, &expected, const_cast<atomic_ptr_value_type>(exchange), false, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST );
	return expected;
}

#elif defined(_MSC_VER)

XRAY_THREADING_INLINE long volatile* win_atomic32_ptr( atomic32_type& value )
{
	static_assert(sizeof(long) == sizeof(atomic32_value_type), "Win32 _Interlocked* requires 32-bit long");
	return reinterpret_cast<long volatile*>( &value );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_increment( atomic32_type& value )
{
	return _InterlockedIncrement( win_atomic32_ptr( value ) );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_decrement( atomic32_type& value )
{
	return _InterlockedDecrement( win_atomic32_ptr( value ) );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_exchange_add( atomic32_type& value, atomic32_value_type increment )
{
	return _InterlockedExchangeAdd( win_atomic32_ptr( value ), increment );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_exchange_sub( atomic32_type& value, atomic32_value_type subtract )
{
	return _InterlockedExchangeAdd( win_atomic32_ptr( value ), -subtract );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_and( atomic32_type& value, atomic32_value_type mask )
{
	return _InterlockedAnd( win_atomic32_ptr( value ), mask );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_or( atomic32_type& value, atomic32_value_type mask )
{
	return _InterlockedOr( win_atomic32_ptr( value ), mask );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_exchange( atomic32_type& target, atomic32_value_type value )
{
	return _InterlockedExchange( win_atomic32_ptr( target ), value );
}

XRAY_THREADING_INLINE atomic_ptr_value_type interlocked_exchange_pointer( atomic_ptr_type& target, atomic_ptr_value_type const value )
{
	return _InterlockedExchangePointer( &target, const_cast<atomic_ptr_value_type>(value) );
}

template <typename T, typename U>
XRAY_THREADING_INLINE T* interlocked_exchange_pointer( T* volatile& target, U* value )
{
	return (T*)interlocked_exchange_pointer( (atomic_ptr_type&)target, (atomic_ptr_value_type)value );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_compare_exchange( atomic32_type& target, atomic32_value_type exchange, atomic32_value_type comperand )
{
	return _InterlockedCompareExchange( win_atomic32_ptr( target ), exchange, comperand );
}

XRAY_THREADING_INLINE atomic64_value_type interlocked_compare_exchange( atomic64_type& target, atomic64_value_type exchange, atomic64_value_type comperand )
{
	return _InterlockedCompareExchange64( &target, exchange, comperand );
}

XRAY_THREADING_INLINE atomic64_value_type interlocked_exchange( atomic64_type& target, atomic64_value_type exchange )
{
	atomic64_value_type old;
	do {
		old = target;
	} while ( interlocked_compare_exchange( target, exchange, old ) != old );
	return old;
}

XRAY_THREADING_INLINE atomic_ptr_value_type interlocked_compare_exchange_pointer( atomic_ptr_type& target, atomic_ptr_value_type const exchange, atomic_ptr_value_type const comperand )
{
	return _InterlockedCompareExchangePointer( &target, const_cast<atomic_ptr_value_type>(exchange), const_cast<atomic_ptr_value_type>(comperand) );
}

#else
#	error please define atomic operations for your compiler here
#endif

} // namespace threading
} // namespace xray

#endif // #ifndef XRAY_THREADING_FUNCTIONS_INLINE_H_INCLUDED
