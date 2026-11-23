////////////////////////////////////////////////////////////////////////////
//	RHI Types
//	Abstraction of render hardware resources to support multiple backends.
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_RHI_TYPES_H_INCLUDED
#define XRAY_RENDER_RHI_TYPES_H_INCLUDED

#include <vector>
#include <cstddef>
#include <xray/type_extensions.h>
#include <xray/render/core/sources/rhi/rhi_comparison.h>

namespace xray {
namespace render {
namespace rhi {

// -----------------------------------------------------------------------
// Resource Handles
// -----------------------------------------------------------------------
// Opaque handles to hardware resources. 
// The actual implementation is managed by the rhi::backend.
typedef u64 rhi_buffer_handle;
typedef u64 rhi_texture_handle;
typedef u64 rhi_shader_handle;
typedef u64 rhi_pipeline_handle;
typedef u64 rhi_sampler_handle;

#define RHI_INVALID_HANDLE 0

// -----------------------------------------------------------------------
// Enums
// -----------------------------------------------------------------------

enum class buffer_type : u8 {
	vertex,
	index,
	constant,
	structured,
	indirect
};

enum class texture_type : u8 {
	type_1d,
	type_2d,
	type_3d,
	type_cube,
	type_cube_array,
    type_2d_array
};

enum class format : u32 {
	unknown = 0,
	// Common formats
	r8g8b8a8_unorm,
	r8g8b8a8_srgb,
	r16g16b16a16_float,
	r32g32b32a32_float,
	d24_s8_unorm,
	d32_float,
	depth_stencil,
	r32_float,
	r32g32_float,
	r32g32b32_float,
	bc1_unorm,
	bc1_srgb,
	bc2_unorm,
	bc2_srgb,
	bc3_unorm,
	bc3_srgb,
	r16g16b16a16_unorm,
	r16g16_unorm,
	
	// Mapping to DXGI formats will happen in the backend
};

enum class shader_stage : u8 {
	vertex,
	pixel,
	geometry,
	hull,
	domain,
	compute
};

enum class index_format : u8 { uint16, uint32 };

enum class texture_usage : u8 { sampled, render_target, storage, depth_stencil };
struct texture_desc {
    texture_type type = texture_type::type_2d;
    u32 width = 1, height = 1, mip_levels = 1, array_layers = 1;
    format pixel_format = format::r8g8b8a8_unorm;
    texture_usage usage = texture_usage::sampled;
    u32 samples = 1;
};
struct texture_subresource_data {
    u32 mip_level = 0, layer = 0;
    void const* data = nullptr;
    size_t size = 0, row_pitch = 0;
};
enum class sampler_filter : u8 { nearest, linear };
enum class sampler_address : u8 { repeat, clamp, mirror };
struct sampler_desc {
    sampler_filter min_filter = sampler_filter::linear, mag_filter = sampler_filter::linear;
    sampler_filter mip_filter = sampler_filter::linear;
    sampler_address address_u = sampler_address::repeat, address_v = sampler_address::repeat;
    bool comparison = false;
    comparison_func compare = comparison_func::less_equal;
    float min_lod = 0, max_lod = 16, lod_bias = 0, anisotropy = 1;
};
static_assert(sizeof(sampler_desc)==24); // Preserve the 64-byte material userdata contract.

struct vertex_attribute {
	u32 location = 0;
	format type = format::unknown;
	u32 offset = 0;
	bool operator==(vertex_attribute const&) const = default;
};

// Stage 2 supports one interleaved, per-vertex binding (binding 0).
struct vertex_input_layout {
	u32 stride = 0;
	std::vector<vertex_attribute> attributes;
	bool operator==(vertex_input_layout const&) const = default;
};

enum class resource_state : u8 {
	undefined,
	generic,
	shader_resource,
	render_target,
	depth_write,
	depth_read,
	copy_src,
	copy_dst,
	unordered_access
};

enum class attachment_load : u8 { load, clear, discard };
enum class attachment_store : u8 { store, discard };
struct color_attachment {
    rhi_texture_handle texture = 0;
    attachment_load load = attachment_load::clear;
    attachment_store store = attachment_store::store;
    float clear[4] = {0, 0, 0, 0};
};
struct depth_attachment {
    rhi_texture_handle texture = 0;
    attachment_load load = attachment_load::clear;
    attachment_store store = attachment_store::store;
    float clear_depth = 1;
    u32 clear_stencil = 0;
};
struct render_pass_desc {
    std::vector<color_attachment> colors;
    depth_attachment depth;
};

} // namespace rhi
} // namespace render
} // namespace xray

#endif // XRAY_RENDER_RHI_TYPES_H_INCLUDED
