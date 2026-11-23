////////////////////////////////////////////////////////////////////////////
//	Created 	: 10.05.2026
//	Author		: 
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/memory_mimalloc_allocator.h>
#include <mimalloc.h>

namespace xray {
namespace memory {

mimalloc_allocator::mimalloc_allocator(
#ifndef MASTER_GOLD
		bool const use_leak_detector
#endif // #ifndef MASTER_GOLD
	) 
#ifndef MASTER_GOLD
	: base_allocator( use_leak_detector )
#endif // #ifndef MASTER_GOLD
{
}

void mimalloc_allocator::initialize_impl( pvoid arena, u64 arena_size, pcstr arena_id )
{
	XRAY_UNREFERENCED_PARAMETERS( arena, arena_size, arena_id );
	// mimalloc owns heap storage independently of the registered arena. Keep a
	// non-null lower bound so base_allocator::initialized()/finalize() agree.
	m_arena_start	= (pvoid)u64(1);
	m_arena_end		= (pvoid)u64(-1);
}

void mimalloc_allocator::finalize_impl( )
{
}

pvoid mimalloc_allocator::malloc_impl( size_t size XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_DECLARATION XRAY_CORE_DEBUG_PARAMETERS_DECLARATION )
{
	XRAY_CORE_DEBUG_PARAMETERS_UNREFERENCED_GUARD;
	pvoid const result = mi_malloc( size );
	return on_malloc( result, size, 0, XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_PARAMETER );
}

pvoid mimalloc_allocator::realloc_impl( pvoid pointer, size_t new_size XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_DECLARATION XRAY_CORE_DEBUG_PARAMETERS_DECLARATION )
{
	XRAY_CORE_DEBUG_PARAMETERS_UNREFERENCED_GUARD;
	if ( pointer )
		on_free( pointer, false );

	pvoid const result = mi_realloc( pointer, new_size );
	return on_malloc( result, new_size, 0, XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_PARAMETER );
}

void mimalloc_allocator::free_impl( pvoid pointer XRAY_CORE_DEBUG_PARAMETERS_DECLARATION )
{
	XRAY_CORE_DEBUG_PARAMETERS_UNREFERENCED_GUARD;
	if ( !pointer )
		return;

	on_free( pointer );
	mi_free( pointer );
}

pvoid mimalloc_allocator::call_malloc( size_t size XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_DECLARATION XRAY_CORE_DEBUG_PARAMETERS_DECLARATION )
{
	return malloc_impl( size XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION XRAY_CORE_DEBUG_PARAMETERS );
}

pvoid mimalloc_allocator::call_realloc( pvoid pointer, size_t new_size XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_DECLARATION XRAY_CORE_DEBUG_PARAMETERS_DECLARATION )
{
	return realloc_impl( pointer, new_size XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION XRAY_CORE_DEBUG_PARAMETERS );
}

void mimalloc_allocator::call_free( pvoid pointer XRAY_CORE_DEBUG_PARAMETERS_DECLARATION )
{
	free_impl( pointer XRAY_CORE_DEBUG_PARAMETERS );
}

size_t mimalloc_allocator::usable_size_impl( pvoid pointer ) const
{
	return mi_usable_size( pointer );
}

size_t mimalloc_allocator::total_size( ) const
{
	return 0;
}

size_t mimalloc_allocator::allocated_size( ) const
{
	return 0;
}

} // namespace memory
} // namespace xray
