#ifndef XRAY_VULKAN_VERTEX_LAYOUT_H_INCLUDED
#define XRAY_VULKAN_VERTEX_LAYOUT_H_INCLUDED
#include <xray/render/core/sources/rhi/rhi_types.h>
namespace xray { namespace render { namespace vulkan {
inline u32 vertex_format_size(rhi::format type) {
    using rhi::format;
    switch (type) {
        case format::r32_float: return 4;
        case format::r32g32_float: return 8;
        case format::r32g32b32_float: return 12;
        case format::r32g32b32a32_float: return 16;
        case format::r8g8b8a8_unorm: return 4;
        default: return 0;
    }
}
inline bool valid_vertex_layout(rhi::vertex_input_layout const& layout, u32 max_stride = 2048, u32 max_attributes = 16, u32 max_offset = 2047) {
    if (layout.attributes.empty()) return layout.stride == 0;
    if (!layout.stride || layout.stride > max_stride || layout.attributes.size() > max_attributes) return false;
    for (size_t i = 0; i < layout.attributes.size(); ++i) {
        auto const& a = layout.attributes[i];
        u32 const size = vertex_format_size(a.type);
        if (!size || a.location >= max_attributes || a.offset > max_offset
            || a.offset > layout.stride || size > layout.stride - a.offset
            || a.offset % (a.type == rhi::format::r8g8b8a8_unorm ? 1 : 4)) return false;
        for (size_t j = 0; j < i; ++j) if (layout.attributes[j].location == a.location) return false;
    }
    return layout.stride % 4 == 0;
}
} } }
#endif
