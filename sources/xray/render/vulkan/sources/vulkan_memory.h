////////////////////////////////////////////////////////////////////////////
//	Created 	: 02.05.2026
//	Copyright (C) GSC Game World - 2026
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_VULKAN_MEMORY_H_INCLUDED
#define XRAY_RENDER_VULKAN_MEMORY_H_INCLUDED

#include <xray/render/vulkan/api.h>
#include <xray/noncopyable.h>
#include <vulkan/vulkan.h>

namespace xray {
namespace render {
namespace vulkan {

XRAY_RENDER_VULKAN_API extern memory::mimalloc_allocator_type*	g_allocator;

#define USER_ALLOCATOR ::xray::render::vulkan::g_allocator
#include <xray/std_containers.h>
#include <xray/unique_ptr.h>
#undef USER_ALLOCATOR

// -----------------------------------------------------------------------
// VkAllocationCallbacks bridge
// Redirects all Vulkan allocation requests through the xray allocator.
// Usage: pass vulkan::allocation_callbacks() to any vkCreate* function.
// -----------------------------------------------------------------------

VkAllocationCallbacks const*	allocation_callbacks	();

} // namespace vulkan
} // namespace render
} // namespace xray

#undef NEW
#undef NEW_ARRAY
#undef DELETE
#undef DELETE_ARRAY
#undef MALLOC
#undef ALLOC
#undef REALLOC
#undef FREE

#define NEW( type )									XRAY_NEW_IMPL( ::xray::render::vulkan::g_allocator, type )
#define NEW_ARRAY(type, count)						XRAY_NEW_ARRAY_IMPL(::xray::render::vulkan::g_allocator, type, count)
#define DELETE( pointer, ... )						XRAY_DELETE_IMPL( ::xray::render::vulkan::g_allocator, pointer, __VA_ARGS__ )
#define DELETE_ARRAY(pointer)						XRAY_DELETE_ARRAY_IMPL(::xray::render::vulkan::g_allocator, pointer )
#define MALLOC( size, description )					XRAY_MALLOC_IMPL( ::xray::render::vulkan::g_allocator, size, description )
#define ALLOC( type, count )						XRAY_ALLOC_IMPL( ::xray::render::vulkan::g_allocator, type, count )
#define REALLOC( pointer, size, description )		XRAY_REALLOC_IMPL( ::xray::render::vulkan::g_allocator, pointer, size, description )
#define FREE( pointer )								XRAY_FREE_IMPL( ::xray::render::vulkan::g_allocator, pointer )

#endif // #ifndef XRAY_RENDER_VULKAN_MEMORY_H_INCLUDED
