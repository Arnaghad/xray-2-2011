////////////////////////////////////////////////////////////////////////////
//	Created		: 11.12.2009
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef THREADING_FUNCTIONS_WIN_INLINE_H_INCLUDED
#define THREADING_FUNCTIONS_WIN_INLINE_H_INCLUDED

#include <intrin.h>

#if 0//def MASTER_GOLD
#	include <xray/os_include.h>
#endif // #ifdef MASTER_GOLD

#pragma intrinsic(_InterlockedCompareExchange,		\
				  _InterlockedCompareExchange64,	\
				  _InterlockedExchange,				\
				  _InterlockedIncrement,			\
				  _InterlockedDecrement,			\
				  _InterlockedAnd,					\
				  _InterlockedOr,					\
				  _InterlockedXor,					\
				  _InterlockedExchangeAdd			\
				 )
#pragma intrinsic(_InterlockedExchangePointer)
#define XRAY_INTERLOCKED_EXCHANGE_POINTER(a,b)			_InterlockedExchangePointer(a,b)
#define XRAY_INTERLOCKED_COMPARE_EXCHANGE_POINTER(a,b,c)	_InterlockedCompareExchangePointer(&a,b,c)

namespace xray {
namespace threading {

XRAY_THREADING_INLINE atomic32_value_type interlocked_increment					( atomic32_type& value )
{
	return				( _InterlockedIncrement( &value ) );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_decrement					( atomic32_type& value )
{
	return				( _InterlockedDecrement( &value ) );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_exchange_add				( atomic32_type& value, atomic32_value_type increment )
{
	return				( _InterlockedExchangeAdd( &value, increment ) );
}

XRAY_THREADING_INLINE atomic32_value_type	interlocked_exchange_sub			( atomic32_type& value,  atomic32_value_type  subtract )
{
	return				( _InterlockedExchangeAdd( &value, -subtract ) );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_and						( atomic32_type& value, atomic32_value_type mask )
{
	return				( _InterlockedAnd( &value, mask ) );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_or						( atomic32_type& value, atomic32_value_type mask )
{
	return				( _InterlockedOr( &value, mask ) );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_exchange					( atomic32_type& target, atomic32_value_type value )
{
	return				_InterlockedExchange( &target, value );
}

XRAY_THREADING_INLINE atomic_ptr_value_type interlocked_exchange_pointer		( atomic_ptr_type& target, atomic_ptr_value_type const value )
{
	return				XRAY_INTERLOCKED_EXCHANGE_POINTER( &target, value );
}

template <typename T, typename U>
XRAY_THREADING_INLINE T* interlocked_exchange_pointer( T* volatile& target, U* value )
{
	return (T*)interlocked_exchange_pointer( (atomic_ptr_type&)target, (atomic_ptr_value_type)value );
}

XRAY_THREADING_INLINE atomic32_value_type interlocked_compare_exchange			( atomic32_type& target, atomic32_value_type exchange, atomic32_value_type comperand )
{
	return				_InterlockedCompareExchange( &target, exchange, comperand);
}

XRAY_THREADING_INLINE atomic64_value_type interlocked_compare_exchange			( atomic64_type& target, atomic64_value_type exchange, atomic64_value_type comperand )
{
	return				_InterlockedCompareExchange64(&target, exchange, comperand);
}

XRAY_THREADING_INLINE atomic64_value_type interlocked_exchange					( atomic64_type& target, atomic64_value_type exchange )
{
	atomic64_value_type	old;

	do {
		old				= target;
	} while ( interlocked_compare_exchange( target, exchange, old) != old );

	return				old;
}

XRAY_THREADING_INLINE atomic_ptr_value_type interlocked_compare_exchange_pointer( atomic_ptr_type& target, atomic_ptr_value_type const exchange, atomic_ptr_value_type const comperand )
{
	return				XRAY_INTERLOCKED_COMPARE_EXCHANGE_POINTER(target, exchange, comperand);
}

} // namespace threading
} // namespace xray

#endif // #ifndef THREADING_FUNCTIONS_WIN_INLINE_H_INCLUDED
