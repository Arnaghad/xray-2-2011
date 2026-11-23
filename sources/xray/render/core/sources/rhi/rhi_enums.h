////////////////////////////////////////////////////////////////////////////
//	RHI Enums
//	API-agnostic render state enumerations with conversion utilities.
//	Maps DX11 D3D_* enums → rhi::* generic enums for backend abstraction.
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_RHI_ENUMS_H_INCLUDED
#define XRAY_RENDER_RHI_ENUMS_H_INCLUDED

#include <xray/type_extensions.h>
#include <xray/render/core/sources/rhi/rhi_comparison.h>

namespace xray {
namespace render {
namespace rhi {

// -----------------------------------------------------------------------
// Primitive Topology
// Maps from D3D_PRIMITIVE_TOPOLOGY
// -----------------------------------------------------------------------

enum class topology : u8 {
	undefined = 0,
	point_list,
	line_list,
	line_strip,
	triangle_list,
	triangle_strip,
	line_list_adj,
	line_strip_adj,
	triangle_list_adj,
	triangle_strip_adj
};

// -----------------------------------------------------------------------
// Texture Filter Mode
// Maps from D3D_FILTER
// -----------------------------------------------------------------------

enum class filter_mode : u8 {
	min_mag_mip_point = 0,
	min_mag_point_mip_linear,
	min_point_mag_linear_mip_point,
	min_point_mag_mip_linear,
	min_linear_mag_mip_point,
	min_linear_mag_point_mip_linear,
	min_mag_linear_mip_point,
	min_mag_mip_linear,
	anisotropic,
	comparison_min_mag_mip_point,
	comparison_min_mag_point_mip_linear,
	comparison_min_point_mag_linear_mip_point,
	comparison_min_point_mag_mip_linear,
	comparison_min_linear_mag_mip_point,
	comparison_min_linear_mag_point_mip_linear,
	comparison_min_mag_linear_mip_point,
	comparison_min_mag_mip_linear,
	comparison_anisotropic
};

// -----------------------------------------------------------------------
// Texture Address Mode
// Maps from D3D_TEXTURE_ADDRESS_MODE
// -----------------------------------------------------------------------

enum class address_mode : u8 {
	wrap = 1,
	mirror,
	clamp,
	border,
	mirror_once
};

// -----------------------------------------------------------------------
// Comparison Function
// Maps from D3D_COMPARISON_FUNC
// -----------------------------------------------------------------------

// -----------------------------------------------------------------------
// Stencil Operation
// Maps from D3D_STENCIL_OP
// -----------------------------------------------------------------------

enum class stencil_op : u8 {
	keep = 1,
	zero,
	replace,
	incr_sat,
	decr_sat,
	invert,
	incr,
	decr
};

// -----------------------------------------------------------------------
// Blend Factor
// Maps from D3D_BLEND
// -----------------------------------------------------------------------

enum class blend : u8 {
	zero = 1,
	one,
	src_color,
	inv_src_color,
	src_alpha,
	inv_src_alpha,
	dest_alpha,
	inv_dest_alpha,
	dest_color,
	inv_dest_color,
	src_alpha_sat,
	blend_factor,
	inv_blend_factor,
	src1_color,
	inv_src1_color,
	src1_alpha,
	inv_src1_alpha
};

// -----------------------------------------------------------------------
// Blend Operation
// Maps from D3D_BLEND_OP
// -----------------------------------------------------------------------

enum class blend_op : u8 {
	add = 1,
	subtract,
	rev_subtract,
	min,
	max
};

// -----------------------------------------------------------------------
// Fill Mode
// Maps from D3D_FILL_MODE
// -----------------------------------------------------------------------

enum class fill_mode : u8 {
	wireframe = 2,	// matches D3D11_FILL_WIREFRAME = 2
	solid			// matches D3D11_FILL_SOLID = 3
};

// -----------------------------------------------------------------------
// Cull Mode
// Maps from D3D_CULL_MODE
// -----------------------------------------------------------------------

enum class cull_mode : u8 {
	none = 1,
	front,
	back
};

// -----------------------------------------------------------------------
// Depth Write Mask
// Maps from D3D_DEPTH_WRITE_MASK
// -----------------------------------------------------------------------

enum class depth_write_mask : u8 {
	zero = 0,
	all
};

// -----------------------------------------------------------------------
// Color Write Enable (bitmask)
// Maps from D3D_COLOR_WRITE_ENABLE
// -----------------------------------------------------------------------

enum color_write_enable : u8 {
	color_write_enable_red		= 1,
	color_write_enable_green	= 2,
	color_write_enable_blue		= 4,
	color_write_enable_alpha	= 8,
	color_write_enable_all		= (color_write_enable_red | color_write_enable_green | color_write_enable_blue | color_write_enable_alpha),
	color_write_enable_none		= 0
};

// -----------------------------------------------------------------------
// Clear Flags (bitmask)
// Maps from D3D_CLEAR_FLAG
// -----------------------------------------------------------------------

enum clear_flags : u8 {
	clear_depth		= 0x01,
	clear_stencil	= 0x02
};

// -----------------------------------------------------------------------
// Input Classification
// Maps from D3D_INPUT_CLASSIFICATION
// -----------------------------------------------------------------------

enum class input_classification : u8 {
	per_vertex_data = 0,
	per_instance_data
};

// -----------------------------------------------------------------------
// Buffer Usage
// Maps from D3D_USAGE
// -----------------------------------------------------------------------

enum class buffer_usage : u8 {
	gpu_default = 0,
	immutable,
	dynamic,
	staging
};

// -----------------------------------------------------------------------
// Resource Dimension
// Maps from D3D_RESOURCE_DIMENSION
// -----------------------------------------------------------------------

enum class resource_dimension : u8 {
	unknown = 0,
	buffer,
	texture_1d,
	texture_2d,
	texture_3d
};

} // namespace rhi
} // namespace render
} // namespace xray

// -----------------------------------------------------------------------
// DX11 ↔ RHI conversion utilities
// Include ONLY in DX11 backend compilation units (guarded by header).
// -----------------------------------------------------------------------
#ifdef XRAY_RENDER_CORE_DX11_DEFINES_H_INCLUDED

namespace xray {
namespace render {
namespace rhi {

static inline topology map_topology(D3D_PRIMITIVE_TOPOLOGY d3d_topo) {
	switch (d3d_topo) {
		case D3D_PRIMITIVE_TOPOLOGY_POINTLIST:			return topology::point_list;
		case D3D_PRIMITIVE_TOPOLOGY_LINELIST:			return topology::line_list;
		case D3D_PRIMITIVE_TOPOLOGY_LINESTRIP:			return topology::line_strip;
		case D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST:		return topology::triangle_list;
		case D3D_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP:		return topology::triangle_strip;
		case D3D_PRIMITIVE_TOPOLOGY_LINELIST_ADJ:		return topology::line_list_adj;
		case D3D_PRIMITIVE_TOPOLOGY_LINESTRIP_ADJ:		return topology::line_strip_adj;
		case D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST_ADJ:	return topology::triangle_list_adj;
		case D3D_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP_ADJ:	return topology::triangle_strip_adj;
		default:										return topology::undefined;
	}
}

static inline filter_mode map_filter(D3D_FILTER d3d_filter) {
	switch (d3d_filter) {
		case D3D_FILTER_MIN_MAG_MIP_POINT:										return filter_mode::min_mag_mip_point;
		case D3D_FILTER_MIN_MAG_POINT_MIP_LINEAR:								return filter_mode::min_mag_point_mip_linear;
		case D3D_FILTER_MIN_POINT_MAG_LINEAR_MIP_POINT:							return filter_mode::min_point_mag_linear_mip_point;
		case D3D_FILTER_MIN_POINT_MAG_MIP_LINEAR:								return filter_mode::min_point_mag_mip_linear;
		case D3D_FILTER_MIN_LINEAR_MAG_MIP_POINT:								return filter_mode::min_linear_mag_mip_point;
		case D3D_FILTER_MIN_LINEAR_MAG_POINT_MIP_LINEAR:						return filter_mode::min_linear_mag_point_mip_linear;
		case D3D_FILTER_MIN_MAG_LINEAR_MIP_POINT:								return filter_mode::min_mag_linear_mip_point;
		case D3D_FILTER_MIN_MAG_MIP_LINEAR:										return filter_mode::min_mag_mip_linear;
		case D3D_FILTER_ANISOTROPIC:											return filter_mode::anisotropic;
		case D3D_FILTER_COMPARISON_MIN_MAG_MIP_POINT:							return filter_mode::comparison_min_mag_mip_point;
		case D3D_FILTER_COMPARISON_MIN_MAG_POINT_MIP_LINEAR:					return filter_mode::comparison_min_mag_point_mip_linear;
		case D3D_FILTER_COMPARISON_MIN_POINT_MAG_LINEAR_MIP_POINT:				return filter_mode::comparison_min_point_mag_linear_mip_point;
		case D3D_FILTER_COMPARISON_MIN_POINT_MAG_MIP_LINEAR:					return filter_mode::comparison_min_point_mag_mip_linear;
		case D3D_FILTER_COMPARISON_MIN_LINEAR_MAG_MIP_POINT:					return filter_mode::comparison_min_linear_mag_mip_point;
		case D3D_FILTER_COMPARISON_MIN_LINEAR_MAG_POINT_MIP_LINEAR:				return filter_mode::comparison_min_linear_mag_point_mip_linear;
		case D3D_FILTER_COMPARISON_MIN_MAG_LINEAR_MIP_POINT:					return filter_mode::comparison_min_mag_linear_mip_point;
		case D3D_FILTER_COMPARISON_MIN_MAG_MIP_LINEAR:							return filter_mode::comparison_min_mag_mip_linear;
		case D3D_FILTER_COMPARISON_ANISOTROPIC:									return filter_mode::comparison_anisotropic;
		default:																return filter_mode::min_mag_mip_point;
	}
}

static inline address_mode map_address_mode(D3D_TEXTURE_ADDRESS_MODE d3d_mode) {
	switch (d3d_mode) {
		case D3D_TEXTURE_ADDRESS_WRAP:			return address_mode::wrap;
		case D3D_TEXTURE_ADDRESS_MIRROR:		return address_mode::mirror;
		case D3D_TEXTURE_ADDRESS_CLAMP:			return address_mode::clamp;
		case D3D_TEXTURE_ADDRESS_BORDER:		return address_mode::border;
		case D3D_TEXTURE_ADDRESS_MIRROR_ONCE:	return address_mode::mirror_once;
		default:								return address_mode::clamp;
	}
}

static inline comparison_func map_comparison(D3D_COMPARISON_FUNC d3d_func) {
	switch (d3d_func) {
		case D3D_COMPARISON_NEVER:			return comparison_func::never;
		case D3D_COMPARISON_LESS:			return comparison_func::less;
		case D3D_COMPARISON_EQUAL:			return comparison_func::equal;
		case D3D_COMPARISON_LESS_EQUAL:		return comparison_func::less_equal;
		case D3D_COMPARISON_GREATER:		return comparison_func::greater;
		case D3D_COMPARISON_NOT_EQUAL:		return comparison_func::not_equal;
		case D3D_COMPARISON_GREATER_EQUAL:	return comparison_func::greater_equal;
		case D3D_COMPARISON_ALWAYS:			return comparison_func::always;
		default:							return comparison_func::always;
	}
}

static inline stencil_op map_stencil_op(D3D_STENCIL_OP d3d_op) {
	switch (d3d_op) {
		case D3D_STENCIL_OP_KEEP:		return stencil_op::keep;
		case D3D_STENCIL_OP_ZERO:		return stencil_op::zero;
		case D3D_STENCIL_OP_REPLACE:	return stencil_op::replace;
		case D3D_STENCIL_OP_INCR_SAT:	return stencil_op::incr_sat;
		case D3D_STENCIL_OP_DECR_SAT:	return stencil_op::decr_sat;
		case D3D_STENCIL_OP_INVERT:		return stencil_op::invert;
		case D3D_STENCIL_OP_INCR:		return stencil_op::incr;
		case D3D_STENCIL_OP_DECR:		return stencil_op::decr;
		default:						return stencil_op::keep;
	}
}

static inline blend map_blend(D3D_BLEND d3d_blend) {
	switch (d3d_blend) {
		case D3D_BLEND_ZERO:			return blend::zero;
		case D3D_BLEND_ONE:				return blend::one;
		case D3D_BLEND_SRC_COLOR:		return blend::src_color;
		case D3D_BLEND_INV_SRC_COLOR:	return blend::inv_src_color;
		case D3D_BLEND_SRC_ALPHA:		return blend::src_alpha;
		case D3D_BLEND_INV_SRC_ALPHA:	return blend::inv_src_alpha;
		case D3D_BLEND_DEST_ALPHA:		return blend::dest_alpha;
		case D3D_BLEND_INV_DEST_ALPHA:	return blend::inv_dest_alpha;
		case D3D_BLEND_DEST_COLOR:		return blend::dest_color;
		case D3D_BLEND_INV_DEST_COLOR:	return blend::inv_dest_color;
		case D3D_BLEND_SRC_ALPHA_SAT:	return blend::src_alpha_sat;
		case D3D_BLEND_BLEND_FACTOR:	return blend::blend_factor;
		case D3D_BLEND_INV_BLEND_FACTOR:return blend::inv_blend_factor;
		case D3D_BLEND_SRC1_COLOR:		return blend::src1_color;
		case D3D_BLEND_INV_SRC1_COLOR:	return blend::inv_src1_color;
		case D3D_BLEND_SRC1_ALPHA:		return blend::src1_alpha;
		case D3D_BLEND_INV_SRC1_ALPHA:	return blend::inv_src1_alpha;
		default:						return blend::one;
	}
}

static inline blend_op map_blend_op(D3D_BLEND_OP d3d_op) {
	switch (d3d_op) {
		case D3D_BLEND_OP_ADD:			return blend_op::add;
		case D3D_BLEND_OP_SUBTRACT:		return blend_op::subtract;
		case D3D_BLEND_OP_REV_SUBTRACT:	return blend_op::rev_subtract;
		case D3D_BLEND_OP_MIN:			return blend_op::min;
		case D3D_BLEND_OP_MAX:			return blend_op::max;
		default:						return blend_op::add;
	}
}

static inline fill_mode map_fill_mode(D3D_FILL_MODE d3d_mode) {
	switch (d3d_mode) {
		case D3D_FILL_WIREFRAME:	return fill_mode::wireframe;
		case D3D_FILL_SOLID:		return fill_mode::solid;
		default:					return fill_mode::solid;
	}
}

static inline cull_mode map_cull_mode(D3D_CULL_MODE d3d_mode) {
	switch (d3d_mode) {
		case D3D_CULL_NONE:		return cull_mode::none;
		case D3D_CULL_FRONT:	return cull_mode::front;
		case D3D_CULL_BACK:		return cull_mode::back;
		default:				return cull_mode::back;
	}
}

} // namespace rhi
} // namespace render
} // namespace xray

#endif // XRAY_RENDER_CORE_DX11_DEFINES_H_INCLUDED

// -----------------------------------------------------------------------
// Vulkan ↔ RHI conversion utilities
// Include ONLY in Vulkan backend compilation units.
// -----------------------------------------------------------------------
#if defined(XRAY_RENDER_VULKAN_BUILDING) && __has_include(<vulkan/vulkan.h>)

#include <vulkan/vulkan.h>

namespace xray {
namespace render {
namespace rhi {

static inline VkPrimitiveTopology to_vk_topology(topology topo) {
	switch (topo) {
		case topology::point_list:			return VK_PRIMITIVE_TOPOLOGY_POINT_LIST;
		case topology::line_list:			return VK_PRIMITIVE_TOPOLOGY_LINE_LIST;
		case topology::line_strip:			return VK_PRIMITIVE_TOPOLOGY_LINE_STRIP;
		case topology::triangle_list:		return VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
		case topology::triangle_strip:		return VK_PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP;
		case topology::line_list_adj:		return VK_PRIMITIVE_TOPOLOGY_LINE_LIST_WITH_ADJACENCY;
		case topology::line_strip_adj:		return VK_PRIMITIVE_TOPOLOGY_LINE_STRIP_WITH_ADJACENCY;
		case topology::triangle_list_adj:	return VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST_WITH_ADJACENCY;
		case topology::triangle_strip_adj:	return VK_PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP_WITH_ADJACENCY;
		default:							return VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
	}
}

static inline VkFilter to_vk_filter_mag(filter_mode mode) {
	switch (mode) {
		case filter_mode::min_mag_mip_point:
		case filter_mode::min_mag_point_mip_linear:
		case filter_mode::min_linear_mag_mip_point:
		case filter_mode::min_linear_mag_point_mip_linear:
		case filter_mode::comparison_min_mag_mip_point:
		case filter_mode::comparison_min_mag_point_mip_linear:
		case filter_mode::comparison_min_linear_mag_mip_point:
		case filter_mode::comparison_min_linear_mag_point_mip_linear:
			return VK_FILTER_NEAREST;
		default:
			return VK_FILTER_LINEAR;
	}
}

static inline VkFilter to_vk_filter_min(filter_mode mode) {
	switch (mode) {
		case filter_mode::min_mag_mip_point:
		case filter_mode::min_mag_point_mip_linear:
		case filter_mode::min_point_mag_linear_mip_point:
		case filter_mode::min_point_mag_mip_linear:
		case filter_mode::comparison_min_mag_mip_point:
		case filter_mode::comparison_min_mag_point_mip_linear:
		case filter_mode::comparison_min_point_mag_linear_mip_point:
		case filter_mode::comparison_min_point_mag_mip_linear:
			return VK_FILTER_NEAREST;
		default:
			return VK_FILTER_LINEAR;
	}
}

static inline VkSamplerMipmapMode to_vk_mipmap_mode(filter_mode mode) {
	switch (mode) {
		case filter_mode::min_mag_mip_point:
		case filter_mode::min_point_mag_linear_mip_point:
		case filter_mode::min_linear_mag_mip_point:
		case filter_mode::min_mag_linear_mip_point:
		case filter_mode::comparison_min_mag_mip_point:
		case filter_mode::comparison_min_point_mag_linear_mip_point:
		case filter_mode::comparison_min_linear_mag_mip_point:
		case filter_mode::comparison_min_mag_linear_mip_point:
			return VK_SAMPLER_MIPMAP_MODE_NEAREST;
		default:
			return VK_SAMPLER_MIPMAP_MODE_LINEAR;
	}
}

static inline bool is_anisotropic(filter_mode mode) {
	return mode == filter_mode::anisotropic || mode == filter_mode::comparison_anisotropic;
}

static inline bool is_comparison_filter(filter_mode mode) {
	return static_cast<u8>(mode) >= static_cast<u8>(filter_mode::comparison_min_mag_mip_point);
}

static inline VkSamplerAddressMode to_vk_address_mode(address_mode mode) {
	switch (mode) {
		case address_mode::wrap:		return VK_SAMPLER_ADDRESS_MODE_REPEAT;
		case address_mode::mirror:		return VK_SAMPLER_ADDRESS_MODE_MIRRORED_REPEAT;
		case address_mode::clamp:		return VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
		case address_mode::border:		return VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_BORDER;
		case address_mode::mirror_once:	return VK_SAMPLER_ADDRESS_MODE_MIRROR_CLAMP_TO_EDGE;
		default:						return VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
	}
}

static inline VkCompareOp to_vk_compare_op(comparison_func func) {
	switch (func) {
		case comparison_func::never:			return VK_COMPARE_OP_NEVER;
		case comparison_func::less:				return VK_COMPARE_OP_LESS;
		case comparison_func::equal:			return VK_COMPARE_OP_EQUAL;
		case comparison_func::less_equal:		return VK_COMPARE_OP_LESS_OR_EQUAL;
		case comparison_func::greater:			return VK_COMPARE_OP_GREATER;
		case comparison_func::not_equal:		return VK_COMPARE_OP_NOT_EQUAL;
		case comparison_func::greater_equal:	return VK_COMPARE_OP_GREATER_OR_EQUAL;
		case comparison_func::always:			return VK_COMPARE_OP_ALWAYS;
		default:								return VK_COMPARE_OP_ALWAYS;
	}
}

static inline VkStencilOp to_vk_stencil_op(stencil_op op) {
	switch (op) {
		case stencil_op::keep:		return VK_STENCIL_OP_KEEP;
		case stencil_op::zero:		return VK_STENCIL_OP_ZERO;
		case stencil_op::replace:	return VK_STENCIL_OP_REPLACE;
		case stencil_op::incr_sat:	return VK_STENCIL_OP_INCREMENT_AND_CLAMP;
		case stencil_op::decr_sat:	return VK_STENCIL_OP_DECREMENT_AND_CLAMP;
		case stencil_op::invert:	return VK_STENCIL_OP_INVERT;
		case stencil_op::incr:		return VK_STENCIL_OP_INCREMENT_AND_WRAP;
		case stencil_op::decr:		return VK_STENCIL_OP_DECREMENT_AND_WRAP;
		default:					return VK_STENCIL_OP_KEEP;
	}
}

static inline VkBlendFactor to_vk_blend_factor(blend b) {
	switch (b) {
		case blend::zero:				return VK_BLEND_FACTOR_ZERO;
		case blend::one:				return VK_BLEND_FACTOR_ONE;
		case blend::src_color:			return VK_BLEND_FACTOR_SRC_COLOR;
		case blend::inv_src_color:		return VK_BLEND_FACTOR_ONE_MINUS_SRC_COLOR;
		case blend::src_alpha:			return VK_BLEND_FACTOR_SRC_ALPHA;
		case blend::inv_src_alpha:		return VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
		case blend::dest_alpha:			return VK_BLEND_FACTOR_DST_ALPHA;
		case blend::inv_dest_alpha:		return VK_BLEND_FACTOR_ONE_MINUS_DST_ALPHA;
		case blend::dest_color:			return VK_BLEND_FACTOR_DST_COLOR;
		case blend::inv_dest_color:		return VK_BLEND_FACTOR_ONE_MINUS_DST_COLOR;
		case blend::src_alpha_sat:		return VK_BLEND_FACTOR_SRC_ALPHA_SATURATE;
		case blend::blend_factor:		return VK_BLEND_FACTOR_CONSTANT_COLOR;
		case blend::inv_blend_factor:	return VK_BLEND_FACTOR_ONE_MINUS_CONSTANT_COLOR;
		case blend::src1_color:			return VK_BLEND_FACTOR_SRC1_COLOR;
		case blend::inv_src1_color:		return VK_BLEND_FACTOR_ONE_MINUS_SRC1_COLOR;
		case blend::src1_alpha:			return VK_BLEND_FACTOR_SRC1_ALPHA;
		case blend::inv_src1_alpha:		return VK_BLEND_FACTOR_ONE_MINUS_SRC1_ALPHA;
		default:						return VK_BLEND_FACTOR_ONE;
	}
}

static inline VkBlendOp to_vk_blend_op(blend_op op) {
	switch (op) {
		case blend_op::add:				return VK_BLEND_OP_ADD;
		case blend_op::subtract:		return VK_BLEND_OP_SUBTRACT;
		case blend_op::rev_subtract:	return VK_BLEND_OP_REVERSE_SUBTRACT;
		case blend_op::min:				return VK_BLEND_OP_MIN;
		case blend_op::max:				return VK_BLEND_OP_MAX;
		default:						return VK_BLEND_OP_ADD;
	}
}

static inline VkPolygonMode to_vk_polygon_mode(fill_mode mode) {
	switch (mode) {
		case fill_mode::wireframe:	return VK_POLYGON_MODE_LINE;
		case fill_mode::solid:		return VK_POLYGON_MODE_FILL;
		default:					return VK_POLYGON_MODE_FILL;
	}
}

static inline VkCullModeFlags to_vk_cull_mode(cull_mode mode) {
	switch (mode) {
		case cull_mode::none:	return VK_CULL_MODE_NONE;
		case cull_mode::front:	return VK_CULL_MODE_FRONT_BIT;
		case cull_mode::back:	return VK_CULL_MODE_BACK_BIT;
		default:				return VK_CULL_MODE_BACK_BIT;
	}
}

static inline VkFormat to_vk_format(format fmt) {
	switch (fmt) {
		case format::r8g8b8a8_unorm:		return VK_FORMAT_R8G8B8A8_UNORM;
		case format::r8g8b8a8_srgb:			return VK_FORMAT_R8G8B8A8_SRGB;
		case format::bc1_unorm: return VK_FORMAT_BC1_RGBA_UNORM_BLOCK;
		case format::bc1_srgb: return VK_FORMAT_BC1_RGBA_SRGB_BLOCK;
		case format::bc2_unorm: return VK_FORMAT_BC2_UNORM_BLOCK;
		case format::bc2_srgb: return VK_FORMAT_BC2_SRGB_BLOCK;
		case format::bc3_unorm: return VK_FORMAT_BC3_UNORM_BLOCK;
		case format::bc3_srgb: return VK_FORMAT_BC3_SRGB_BLOCK;
		case format::r16g16b16a16_float:	return VK_FORMAT_R16G16B16A16_SFLOAT;
		case format::r16g16b16a16_unorm: return VK_FORMAT_R16G16B16A16_UNORM;
		case format::r16g16_unorm: return VK_FORMAT_R16G16_UNORM;
		case format::r32_float: return VK_FORMAT_R32_SFLOAT;
		case format::r32g32b32a32_float:	return VK_FORMAT_R32G32B32A32_SFLOAT;
		case format::d24_s8_unorm:			return VK_FORMAT_D24_UNORM_S8_UINT;
		case format::d32_float:				return VK_FORMAT_D32_SFLOAT;
		case format::depth_stencil:			return VK_FORMAT_D24_UNORM_S8_UINT;
		default:							return VK_FORMAT_UNDEFINED;
	}
}

} // namespace rhi
} // namespace render
} // namespace xray

#endif // XRAY_RENDER_VULKAN_BUILDING

#endif // XRAY_RENDER_RHI_ENUMS_H_INCLUDED
