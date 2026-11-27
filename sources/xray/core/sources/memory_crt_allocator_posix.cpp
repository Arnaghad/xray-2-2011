////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux CRT & process allocator diagnostics
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/memory_crt_allocator.h>
#include "memory_process_allocator.h"

#include <cstdlib>
#if defined(__GLIBC__)
#	include <malloc.h>
#endif

using xray::memory::crt_allocator;
using xray::memory::process_allocator;

crt_allocator::crt_allocator()
	: m_malloc_ptr( reinterpret_cast<malloc_ptr_type>( &std::malloc ) )
	, m_free_ptr( reinterpret_cast<free_ptr_type>( &std::free ) )
	, m_realloc_ptr( reinterpret_cast<realloc_ptr_type>( &std::realloc ) )
{
}

size_t crt_allocator::total_size() const
{
#if defined(__GLIBC__)
	struct mallinfo2 mi = mallinfo2();
	return static_cast<size_t>( mi.arena + mi.hblkhd );
#else
	return 0;
#endif
}

size_t crt_allocator::allocated_size() const
{
#if defined(__GLIBC__)
	struct mallinfo2 mi = mallinfo2();
	return static_cast<size_t>( mi.uordblks + mi.hblkhd );
#else
	return 0;
#endif
}

size_t process_allocator::total_size() const
{
	return 0;
}

size_t process_allocator::allocated_size() const
{
	return 0;
}
