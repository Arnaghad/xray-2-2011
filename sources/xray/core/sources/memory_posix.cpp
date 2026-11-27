////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux virtual memory & arena allocation backend
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "memory.h"
#include "memory_platform.h"
#include <xray/os_platform.h>
#include <xray/platform_extensions.h>

#include <algorithm>
#include <cstdint>
#include <sys/mman.h>

using xray::memory::platform::regions_type;
using xray::memory::platform::region;
using xray::Kb;
using xray::Mb;
using xray::Gb;
using xray::Tb;

static pvoid	s_single_block_arena		= nullptr;
static u64		s_single_block_arena_size	= 0;

static xray::command_line::key s_max_resources_size( "max_resources_size", "", "resources", "setup maximum size for resource arenas, Mb" );
static xray::command_line::key s_fill_arenas_with_garbage( "fill_arenas_with_garbage", "", "memory", "fills all the memory in all the arenas with garbage; could slowdown startup significantly!" );

namespace {

pvoid allocate_region_impl( u64 const size, bool const assert_on_failure = true )
{
	if ( !size )
		return nullptr;

	pvoid const result = ::mmap(
		nullptr,
		static_cast<std::size_t>( size ),
		PROT_READ | PROT_WRITE,
		MAP_PRIVATE | MAP_ANONYMOUS,
		-1,
		0
	);
	if ( result == MAP_FAILED ) {
		R_ASSERT_U( !assert_on_failure, "mmap failed allocating %llu bytes", static_cast<unsigned long long>( size ) );
		return nullptr;
	}

#ifndef MASTER_GOLD
	if ( s_fill_arenas_with_garbage.is_set() )
		xray::memory::fill32( result, static_cast<std::size_t>( size ), xray::memory::uninitialized_value<u32>(), static_cast<std::size_t>( size ) / sizeof( u32 ) );
#endif
	return result;
}

void free_region_impl( pvoid const buffer, u64 const size )
{
	if ( !buffer || !size )
		return;
	int const rc = ::munmap( buffer, static_cast<std::size_t>( size ) );
	R_ASSERT_U( rc == 0 );
}

u64 allocation_granularity()
{
	return xray::os::query_system_memory().allocation_granularity;
}

struct memory_stats {
	u64		total_available_memory = 0;
	u64		physical_memory = 0;
	u64		available_address_space = 0;
	u64		current_kernel_memory = 0;
	u64		video_memory = 0;
	u64		engine_fixed_memory = 0;
	bool	does_os_use_process_address_space_for_duplicating_video_resources = true;
};

u64 calculate_desirable_resource_arenas(
	memory_stats& stats,
	float const maximum_video_share,
	u64 const minimum_video_memory_size,
	u64 const minimum_resources_size )
{
	u64 useful_memory = xray::math::min( stats.total_available_memory, stats.physical_memory );
	CHECK_OR_EXIT( useful_memory >= stats.current_kernel_memory, "Not enough memory to run X-Ray Engine v2.0" );
	useful_memory -= stats.current_kernel_memory;
	CHECK_OR_EXIT( useful_memory >= stats.engine_fixed_memory, "Not enough memory to run X-Ray Engine v2.0" );
	useful_memory -= stats.engine_fixed_memory;
	CHECK_OR_EXIT( useful_memory >= ( minimum_video_memory_size + minimum_resources_size ), "Not enough memory to run X-Ray Engine v2.0" );

	u64 useful_video = stats.video_memory;
	u64 address_space = stats.available_address_space;
	CHECK_OR_EXIT( address_space >= stats.engine_fixed_memory, "Not enough address space to run X-Ray Engine v2.0" );
	address_space -= stats.engine_fixed_memory;

	if ( stats.does_os_use_process_address_space_for_duplicating_video_resources ) {
		if ( useful_video / address_space > maximum_video_share )
			useful_video = static_cast<u64>( address_space * maximum_video_share );
		if ( ( address_space - useful_video ) < minimum_resources_size )
			useful_video = address_space - minimum_resources_size;
	}

	if ( useful_video / useful_memory > maximum_video_share )
		useful_video = static_cast<u64>( useful_memory * maximum_video_share );
	if ( ( useful_memory - useful_video ) < minimum_resources_size )
		useful_video = useful_memory - minimum_resources_size;

	if ( stats.does_os_use_process_address_space_for_duplicating_video_resources )
		address_space -= useful_video;

	useful_memory -= useful_video;
	useful_memory = xray::math::min( useful_memory, address_space );
	useful_memory = xray::math::max( useful_memory, minimum_resources_size );

	u64 maximum_resources_size = u64( -1 );
	if ( s_max_resources_size.is_set_as_number( &maximum_resources_size ) ) {
		maximum_resources_size *= Mb;
		maximum_resources_size = xray::math::max( maximum_resources_size, minimum_resources_size );
	}
	return xray::math::min( useful_memory, maximum_resources_size );
}

bool try_to_allocate_arenas(
	regions_type& arenas,
	region& managed_arena,
	region& unmanaged_arena )
{
	u64 const granularity = allocation_granularity();
	u64 total_size = 0;
	for ( auto const& r : arenas )
		total_size += r.size;
	total_size += managed_arena.size + unmanaged_arena.size;

	u64 const aligned_total = xray::math::align_up( total_size, granularity );
	pbyte block = static_cast<pbyte>( allocate_region_impl( aligned_total, false ) );
	if ( block ) {
		s_single_block_arena = block;
		s_single_block_arena_size = aligned_total;
		for ( auto& r : arenas ) {
			if ( !r.size )
				continue;
			r.address = block;
			block += r.size;
		}
		managed_arena.address = block;
		block += managed_arena.size;
		unmanaged_arena.address = block;
		return true;
	}

	for ( auto& r : arenas ) {
		if ( !r.size )
			continue;
		r.address = allocate_region_impl( xray::math::align_up( r.size, granularity ), true );
		if ( !r.address )
			return false;
	}
	managed_arena.address = allocate_region_impl( xray::math::align_up( managed_arena.size, granularity ), true );
	unmanaged_arena.address = allocate_region_impl( xray::math::align_up( unmanaged_arena.size, granularity ), true );
	return managed_arena.address && unmanaged_arena.address;
}

} // namespace

void xray::memory::platform::free_region( pvoid buffer, u64 const buffer_size )
{
	if ( s_single_block_arena_size ) {
		R_ASSERT_CMP( s_single_block_arena_size, >=, buffer_size );
		s_single_block_arena_size -= buffer_size;
		if ( s_single_block_arena_size )
			return;
		free_region_impl( s_single_block_arena, 0 ); // full release handled per-arena below if split
		return;
	}
	free_region_impl( buffer, buffer_size );
}

void xray::memory::platform::allocate_arenas(
	u64 const reserved_memory_size,
	u64 const reserved_address_space,
	regions_type& arenas,
	region& managed_arena,
	region& unmanaged_arena )
{
	auto const sys_mem = xray::os::query_system_memory();
	u64 const total_phys = sys_mem.total_physical ? sys_mem.total_physical : 4ULL * Gb;

	memory_stats stats;
	CHECK_OR_EXIT( reserved_memory_size < total_phys, "Too much memory reserved, not enough memory for engine to proceed." );
	stats.physical_memory = total_phys - xray::math::align_up( reserved_memory_size, sys_mem.page_size );
	stats.current_kernel_memory = 512 * Mb;
	if ( stats.current_kernel_memory >= stats.physical_memory )
		stats.current_kernel_memory = stats.physical_memory / 8;

	u64 const virtual_space = 8ULL * Tb;
	CHECK_OR_EXIT( reserved_address_space < virtual_space, "Too much virtual address space reserved." );
	stats.available_address_space = virtual_space - reserved_address_space;

	u64 engine_fixed_memory = 0;
	for ( auto const& r : arenas )
		engine_fixed_memory += r.size;
	stats.engine_fixed_memory = engine_fixed_memory;
	stats.does_os_use_process_address_space_for_duplicating_video_resources = true;
	stats.total_available_memory = 8ULL * Tb;

	u64 const minimal_local_video_memory_size = xray::platform::get_minimal_local_video_memory_size();
	stats.video_memory = xray::platform::get_local_video_memory_size();

	float const video_memory_share = 0.9f;
	u64 const minimum_resources_memory_size = 128 * Mb;
	u64 const granularity = allocation_granularity();
	u64 const desirable_resources_size = xray::math::align_down(
		calculate_desirable_resource_arenas( stats, video_memory_share, minimal_local_video_memory_size, minimum_resources_memory_size ),
		granularity
	);

	float const managed_to_unmanaged_share = 4.f / 1.f;
	managed_arena.size = xray::math::align_up( static_cast<u64>( ( managed_to_unmanaged_share / ( managed_to_unmanaged_share + 1.f ) ) * desirable_resources_size ), granularity );
	unmanaged_arena.size = desirable_resources_size - managed_arena.size;

	CHECK_OR_EXIT( try_to_allocate_arenas( arenas, managed_arena, unmanaged_arena ), "Not enough memory to run X-Ray Engine v2.0" );
}

void xray::memory::on_after_memory_initialized()
{
}

#if XRAY_DEBUG_ALLOCATOR
void xray::memory::register_debug_allocator( u64 const maximum_memory_size )
{
	XRAY_UNREFERENCED_PARAMETER( maximum_memory_size );
	xray::debug::g_mt_allocator->do_register( 1, "debug multithreaded allocator" );
}
#endif
