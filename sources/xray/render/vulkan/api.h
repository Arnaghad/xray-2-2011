#ifndef XRAY_RENDER_VULKAN_API_H_INCLUDED
#define XRAY_RENDER_VULKAN_API_H_INCLUDED

#include <xray/render/vulkan/library_linkage.h>
#include <xray/memory_extensions.h>

namespace xray {
namespace render {
namespace vulkan {

typedef memory::mimalloc_allocator_type				vulkan_allocator_type;

XRAY_RENDER_VULKAN_API	void	set_memory_allocator	( vulkan_allocator_type& allocator );
// Called only after the world and all Vulkan objects have been destroyed.
XRAY_RENDER_VULKAN_API void reset_memory_allocator();

} // namespace vulkan
} // namespace render
} // namespace xray

#endif // #ifndef XRAY_RENDER_VULKAN_API_H_INCLUDED
