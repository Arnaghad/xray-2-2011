////////////////////////////////////////////////////////////////////////////
//	Created 	: 02.05.2026
//	Copyright (C) GSC Game World - 2026
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "vulkan_memory.h"
#include "aligned_allocation.h"
#include "vulkan_performance.h"
#include <mutex>
#include <algorithm>
#include <xray/logging/extensions.h>

namespace xray {
namespace render {
namespace vulkan {

memory::mimalloc_allocator_type*	g_allocator = nullptr;
namespace {
std::mutex s_host_memory_mutex;
host_memory_statistics s_host_memory;
void record_host_memory(size_t allocated, size_t freed, bool did_allocate, bool did_free) {
    std::lock_guard<std::mutex> lock(s_host_memory_mutex);
    s_host_memory.live_bytes+=allocated;
    s_host_memory.live_bytes-=freed;
    s_host_memory.allocations+=did_allocate; s_host_memory.frees+=did_free;
    s_host_memory.live_allocations+=did_allocate;
    s_host_memory.live_allocations-=did_free;
    s_host_memory.peak_bytes=std::max(s_host_memory.peak_bytes,s_host_memory.live_bytes);
}
}
host_memory_statistics host_memory_usage() {
    std::lock_guard<std::mutex> lock(s_host_memory_mutex);
    return s_host_memory;
}

static void* allocate_raw(void* context, size_t size)
{
	auto* allocator = static_cast<memory::mimalloc_allocator_type*>(context);
	return XRAY_MALLOC_IMPL(*allocator, size, "vulkan");
}

static void free_raw(void* context, void* pointer)
{
	auto* allocator = static_cast<memory::mimalloc_allocator_type*>(context);
	XRAY_FREE_IMPL(*allocator, pointer);
}

// -----------------------------------------------------------------------
// VkAllocationCallbacks implementation
// -----------------------------------------------------------------------

static void* VKAPI_CALL vk_allocation_function(
	void*                                       pUserData,
	size_t                                      size,
	size_t                                      alignment,
	VkSystemAllocationScope                     allocationScope )
{
	XRAY_UNREFERENCED_PARAMETER( allocationScope );
	auto* result=allocate_aligned({pUserData, &allocate_raw, &free_raw}, size, alignment);
    if (result) record_host_memory(size,0,true,false);
    return result;
}

static void* VKAPI_CALL vk_reallocation_function(
	void*                                       pUserData,
	void*                                       pOriginal,
	size_t                                      size,
	size_t                                      alignment,
	VkSystemAllocationScope                     allocationScope )
{
	XRAY_UNREFERENCED_PARAMETER( allocationScope );
    auto previous=pOriginal ? (static_cast<allocation_header*>(pOriginal)-1)->size : 0;
	auto* result=reallocate_aligned({pUserData, &allocate_raw, &free_raw}, pOriginal, size, alignment);
    bool released=pOriginal && (!size || result);
    if (result || released) record_host_memory(result ? size : 0,released ? previous : 0,result!=nullptr,released);
    return result;
}

static void VKAPI_CALL vk_free_function(
	void*                                       pUserData,
	void*                                       pMemory )
{
	if ( !pMemory )
		return;

    auto size=(static_cast<allocation_header*>(pMemory)-1)->size;
	free_aligned({pUserData, &allocate_raw, &free_raw}, pMemory);
    record_host_memory(0,size,false,true);
}

static void VKAPI_CALL vk_internal_allocation_notification(
	void*                                       pUserData,
	size_t                                      size,
	VkInternalAllocationType                    allocationType,
	VkSystemAllocationScope                     allocationScope )
{
	XRAY_UNREFERENCED_PARAMETER( pUserData );
	XRAY_UNREFERENCED_PARAMETER( size );
	XRAY_UNREFERENCED_PARAMETER( allocationType );
	XRAY_UNREFERENCED_PARAMETER( allocationScope );
}

static void VKAPI_CALL vk_internal_free_notification(
	void*                                       pUserData,
	size_t                                      size,
	VkInternalAllocationType                    allocationType,
	VkSystemAllocationScope                     allocationScope )
{
	XRAY_UNREFERENCED_PARAMETER( pUserData );
	XRAY_UNREFERENCED_PARAMETER( size );
	XRAY_UNREFERENCED_PARAMETER( allocationType );
	XRAY_UNREFERENCED_PARAMETER( allocationScope );
}

static VkAllocationCallbacks	s_allocation_callbacks = {};
static bool						s_callbacks_initialized = false;

VkAllocationCallbacks const* allocation_callbacks()
{
	ASSERT( g_allocator, "Vulkan allocator is not set, call set_memory_allocator first" );

	if ( !s_callbacks_initialized ) {
		s_allocation_callbacks.pUserData				= g_allocator;
		s_allocation_callbacks.pfnAllocation			= &vk_allocation_function;
		s_allocation_callbacks.pfnReallocation			= &vk_reallocation_function;
		s_allocation_callbacks.pfnFree					= &vk_free_function;
		s_allocation_callbacks.pfnInternalAllocation	= &vk_internal_allocation_notification;
		s_allocation_callbacks.pfnInternalFree			= &vk_internal_free_notification;
		s_callbacks_initialized							= true;
	}

	return &s_allocation_callbacks;
}

// -----------------------------------------------------------------------
// Entry point
// -----------------------------------------------------------------------

void set_memory_allocator( ::xray::memory::mimalloc_allocator_type& allocator )
{
	ASSERT( !g_allocator, "Vulkan allocator is already set" );
	g_allocator				= &allocator;
	s_callbacks_initialized	= false;
}

void reset_memory_allocator()
{
	g_allocator = nullptr;
	s_allocation_callbacks = {};
	s_callbacks_initialized = false;
}

} // namespace vulkan
} // namespace render
} // namespace xray
