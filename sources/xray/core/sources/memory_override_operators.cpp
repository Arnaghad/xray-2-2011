////////////////////////////////////////////////////////////////////////////
//	Created		: 07.09.2011
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include <xray/macro_platform.h>

#include <string.h>	// for memcmp in fastdelegate

#define XRAY_MEMORY_OVERRIDE_OPERATORS_H_INCLUDED
#include "pch.h"
#include <xray/construction.h>
#include <xray/memory_mimalloc_allocator.h>

#if defined(XRAY_STATIC_LIBRARIES) && !defined(XRAY_DISABLE_CRT_OVERRIDES)

pvoid XRAY_CDECL operator new		( size_t buffer_size )
{
	if ( ::xray::memory::g_crt_allocator ) {
#ifdef DEBUG
		return					::xray::memory::g_crt_allocator->malloc_impl( buffer_size, "from operator new", __FUNCTION__, __FILE__, __LINE__ );
#else // #ifdef DEBUG
		return					::xray::memory::g_crt_allocator->malloc_impl( buffer_size );
#endif // #ifdef DEBUG
	}

	XRAY_UNREFERENCED_PARAMETER	( buffer_size );
	UNREACHABLE_CODE			( return 0 );
}

pvoid XRAY_CDECL operator new[]		( size_t buffer_size )
{
	if ( ::xray::memory::g_crt_allocator ) {
#ifdef DEBUG
		return					::xray::memory::g_crt_allocator->malloc_impl( buffer_size, "from operator new[]", __FUNCTION__, __FILE__, __LINE__ );
#else // #ifdef DEBUG
		return					::xray::memory::g_crt_allocator->malloc_impl( buffer_size );
#endif // #ifdef DEBUG
	}

	XRAY_UNREFERENCED_PARAMETER	( buffer_size );
	UNREACHABLE_CODE			( return 0 );
}

void XRAY_CDECL operator delete		( pvoid pointer ) noexcept
{
	if ( !pointer )
		return;

	if ( ::xray::memory::g_crt_allocator ) {
		::xray::memory::g_crt_allocator->free_impl( pointer
#ifdef DEBUG
			, __FUNCTION__, __FILE__, __LINE__
#endif // #ifdef DEBUG
		);
		return;
	}

	XRAY_UNREFERENCED_PARAMETER	( pointer );
	UNREACHABLE_CODE			( );
}

void XRAY_CDECL operator delete[]	( pvoid pointer ) noexcept
{
	if ( !pointer )
		return;

	if ( ::xray::memory::g_crt_allocator ) {
		::xray::memory::g_crt_allocator->free_impl( pointer
#ifdef DEBUG
			, __FUNCTION__, __FILE__, __LINE__
#endif // #ifdef DEBUG
		);
		return;
	}

	XRAY_UNREFERENCED_PARAMETER	( pointer );
	UNREACHABLE_CODE			( );
}

#if __cpp_sized_deallocation >= 201309L
void XRAY_CDECL operator delete( pvoid pointer, size_t size ) noexcept
{
	XRAY_UNREFERENCED_PARAMETER(size);
	::operator delete(pointer);
}

void XRAY_CDECL operator delete[]( pvoid pointer, size_t size ) noexcept
{
	XRAY_UNREFERENCED_PARAMETER(size);
	::operator delete[](pointer);
}
#endif // #if __cpp_sized_deallocation >= 201309L

#include <xray/memory_crt_allocator.h>

namespace xray {
namespace memory {

bool g_crt_allocations_are_enabled	= true;

static char s_crt_allocator_buffer	[ sizeof(xray::memory::crt_allocator) ];
static xray::threading::atomic32_type	s_crt_allocator_creation	=	0;

} // namespace memory
} // namespace xray

using xray::memory::g_crt_allocator;

static inline void check_if_CRT_allocations_enabled() {
	// R_ASSERT(xray::memory::g_crt_allocations_are_enabled, "CRT allocations disabled!");
}

extern "C" {

pvoid malloc					( size_t size ) XRAY_CRT_NOEXCEPT
{
	check_if_CRT_allocations_enabled( );
	R_ASSERT					( g_crt_allocator, "CRT allocation detected. do not use CRT allocations or setup crt allocator (the last doesn't work in MASTER_GOLD configuration)" );
#ifdef DEBUG
	return						g_crt_allocator->malloc_impl( size, "from malloc", __FUNCTION__, __FILE__, __LINE__ );
#else
	return						g_crt_allocator->malloc_impl( size );
#endif
}

pvoid calloc					( size_t count, size_t element_size ) XRAY_CRT_NOEXCEPT
{
	pvoid const result			= malloc( count*element_size );
	memset						( result, 0, count*element_size );
	return						result;
}

pvoid realloc					( pvoid pointer, size_t size ) XRAY_CRT_NOEXCEPT
{
	check_if_CRT_allocations_enabled( );
	R_ASSERT					( g_crt_allocator, "CRT allocation detected. do not use CRT allocations or setup crt allocator (the last doesn't work in MASTER_GOLD configuration)" );
#ifdef DEBUG
	return						g_crt_allocator->realloc_impl( pointer, size, "from realloc", __FUNCTION__, __FILE__, __LINE__ );
#else // #ifdef DEBUG
	return						g_crt_allocator->realloc_impl( pointer, size );
#endif // #ifdef DEBUG
}

void free						( pvoid pointer ) XRAY_CRT_NOEXCEPT
{
	check_if_CRT_allocations_enabled( );
	R_ASSERT					( g_crt_allocator, "CRT allocation detected. do not use CRT allocations or setup crt allocator (the last doesn't work in MASTER_GOLD configuration)" );
#ifdef DEBUG
	g_crt_allocator->free_impl	( pointer, __FUNCTION__, __FILE__, __LINE__ );
#else // #ifdef DEBUG
	g_crt_allocator->free_impl	( pointer );
#endif // #ifdef DEBUG
}

size_t _msize (pvoid pblock)
{
	R_ASSERT					( g_crt_allocator );
	return						g_crt_allocator->usable_size( pblock );
}

pvoid _malloc_crt				( size_t size )
{
	return						malloc( size );
}

pvoid _calloc_crt				( size_t count, size_t size )
{
	return						calloc( count, size );
}

pvoid _realloc_crt				( pvoid ptr, size_t size )
{
	return						realloc( ptr, size );
}

pvoid _recalloc_crt				( pvoid ptr, size_t count, size_t size )
{
	XRAY_UNREFERENCED_PARAMETERS( ptr, count, size );
	LOGI_WARNING("memory:override", "_recalloc_crt is not implemented, returning 0");
	return 0;
}

pvoid _aligned_malloc			( size_t size, size_t alignment )
{
	XRAY_UNREFERENCED_PARAMETERS( size, alignment );
	LOGI_WARNING("memory:override", "_aligned_malloc is not implemented, returning 0");
	return 0;
}

pvoid _aligned_realloc			( pvoid ptr, size_t size, size_t alignment )
{
	XRAY_UNREFERENCED_PARAMETERS( ptr, size, alignment );
	LOGI_WARNING("memory:override", "_aligned_realloc is not implemented, returning 0");
	return 0;
}

void _aligned_free				( pvoid ptr )
{
	XRAY_UNREFERENCED_PARAMETER	( ptr );
	LOGI_WARNING("memory:override", "_aligned_free is not implemented, skipping");
}

pvoid _aligned_offset_malloc	( size_t size, size_t alignment, size_t offset )
{
	XRAY_UNREFERENCED_PARAMETERS( size, alignment, offset );
	LOGI_WARNING("memory:override", "_aligned_offset_malloc is not implemented, returning 0");
	return 0;
}

pvoid _aligned_offset_realloc	( pvoid ptr, size_t size, size_t alignment, size_t offset )
{
	XRAY_UNREFERENCED_PARAMETERS( ptr, size, alignment, offset );
	LOGI_WARNING("memory:override", "_aligned_offset_realloc is not implemented, returning 0");
	return 0;
}

} // extern "C"

#endif // static-build CRT overrides


extern "C" {

pvoid xray_ogg_malloc			( size_t size );

pvoid xray_ogg_calloc			( size_t count, size_t element_size )
{
	pvoid const result			= xray_ogg_malloc( count*element_size );
	memset						( result, 0, count*element_size );
	return						result;
}

#ifndef MASTER_GOLD
pvoid xray_ogg_malloc			( size_t size )
{
	return						XRAY_MALLOC_IMPL( xray::debug::g_mt_allocator, size, "ogg malloc" );
}

pvoid xray_ogg_realloc			( pvoid pointer, size_t size )
{
	return						XRAY_REALLOC_IMPL( xray::debug::g_mt_allocator, pointer, size, "ogg realloc" );
}

void xray_ogg_free				( pvoid pointer )
{
	XRAY_FREE_IMPL				( xray::debug::g_mt_allocator, pointer );
}
#else // #ifndef MASTER_GOLD
pvoid xray_ogg_malloc			( size_t size )
{
	return						malloc( size );
}

pvoid xray_ogg_realloc			( pvoid pointer, size_t size )
{
	return						realloc( pointer, size );
}

void xray_ogg_free				( pvoid pointer )
{
	free						( pointer );
}
#endif // #ifndef MASTER_GOLD

} // extern "C"
