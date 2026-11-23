////////////////////////////////////////////////////////////////////////////
//	Vulkan Resources
//	Internal data structures for managing Vulkan resources via opaque handles.
//	Phase 2: Pipeline State & Resources
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_VULKAN_RESOURCES_H_INCLUDED
#define XRAY_RENDER_VULKAN_RESOURCES_H_INCLUDED

#include <string>
#include <xray/render/core/sources/rhi/rhi_types.h>
#include <xray/render/core/sources/rhi/rhi_enums.h>
#include <vulkan/vulkan.h>
#include "handle_pool.h"

namespace xray {
namespace render {
namespace vulkan {

// -----------------------------------------------------------------------
// Resource Entry Structs
// Each struct holds the Vulkan handles for a single resource instance.
// -----------------------------------------------------------------------

struct vulkan_buffer_entry {
	VkBuffer			buffer		= VK_NULL_HANDLE;
	VkDeviceMemory		memory		= VK_NULL_HANDLE;
	VkDeviceSize		size		= 0;
	rhi::buffer_type	type		= rhi::buffer_type::vertex;
	void*				mapped		= nullptr;	// non-null for HOST_VISIBLE buffers
	bool				in_use		= false;
	std::vector<unsigned char> index_data;
};

struct vulkan_texture_entry {
	VkImage				image		= VK_NULL_HANDLE;
	VkImageView			view		= VK_NULL_HANDLE;
	VkDeviceMemory		memory		= VK_NULL_HANDLE;
	u32					width		= 0;
	u32					height		= 0;
	VkFormat			format		= VK_FORMAT_UNDEFINED;
	bool				in_use		= false;
    rhi::texture_desc description;
    VkImageView sample_view = VK_NULL_HANDLE;
    rhi::resource_state state = rhi::resource_state::undefined;
    bool contents_valid = false;
    bool depth_comparison_supported = false;
};

struct vulkan_sampler_entry {
    VkSampler sampler = VK_NULL_HANDLE;
    rhi::sampler_desc description;
    bool in_use = false;
};

struct vulkan_shader_entry {
	VkShaderModule		module		= VK_NULL_HANDLE;
	rhi::shader_stage	stage		= rhi::shader_stage::vertex;
	std::string entry_point;
    u64 descriptor_mask = 0;
	bool				in_use		= false;
};

} // namespace vulkan
} // namespace render
} // namespace xray

#endif // XRAY_RENDER_VULKAN_RESOURCES_H_INCLUDED
