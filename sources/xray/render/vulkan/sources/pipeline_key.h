#ifndef XRAY_VULKAN_PIPELINE_KEY_H_INCLUDED
#define XRAY_VULKAN_PIPELINE_KEY_H_INCLUDED
#include <xray/render/core/sources/rhi/rhi_enums.h>
#include <xray/render/core/sources/rhi/rhi_types.h>

namespace xray { namespace render { namespace vulkan {
struct pipeline_key {
    rhi::rhi_shader_handle vs_shader = 0, ps_shader = 0, gs_shader = 0;
    rhi::topology prim_topology = rhi::topology::triangle_list;
    rhi::fill_mode fill = rhi::fill_mode::solid;
    rhi::cull_mode cull = rhi::cull_mode::back;
    bool depth_enable = true, depth_write = true;
    bool depth_bias_enable = false;
    rhi::comparison_func depth_func = rhi::comparison_func::less_equal;
    bool stencil_enable = false;
    u32 stencil_reference = 0, stencil_read_mask = 255, stencil_write_mask = 255;
    rhi::comparison_func stencil_compare = rhi::comparison_func::always;
    rhi::stencil_op stencil_pass = rhi::stencil_op::keep;
    bool blend_enable = false;
    rhi::blend src_blend = rhi::blend::one, dst_blend = rhi::blend::zero;
    rhi::blend_op blend_operation = rhi::blend_op::add;
    rhi::vertex_input_layout vertex_layout;
    u32 color_format = 0, depth_format = 0, samples = 1;
    std::vector<u32> color_formats;
    u32 material_abi = 1;
    bool operator==(pipeline_key const&) const = default;
    size_t hash() const {
        size_t h = 0;
        auto add = [&h](u64 value) { h ^= value + 0x9e3779b9u + (h << 6) + (h >> 2); };
        add(vs_shader); add(ps_shader); add(gs_shader); add(u64(prim_topology));
        add(u64(fill)); add(u64(cull)); add(depth_enable); add(depth_write); add(depth_bias_enable); add(u64(depth_func));
        add(stencil_enable); add(stencil_reference); add(stencil_read_mask); add(stencil_write_mask); add(u64(stencil_compare)); add(u64(stencil_pass));
        add(blend_enable); add(u64(src_blend)); add(u64(dst_blend)); add(u64(blend_operation));
        add(color_format); add(depth_format); add(samples); add(material_abi); add(vertex_layout.stride);
        add(color_formats.size()); for (auto f : color_formats) add(f);
        for (auto const& a : vertex_layout.attributes) { add(a.location); add(u64(a.type)); add(a.offset); }
        return h;
    }
};
struct pipeline_key_hash { size_t operator()(pipeline_key const& key) const { return key.hash(); } };
} } }
#endif
