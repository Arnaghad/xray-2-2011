#ifndef XRAY_VULKAN_TEXTURE_FORMAT_H_INCLUDED
#define XRAY_VULKAN_TEXTURE_FORMAT_H_INCLUDED
#include <limits>
#include <stdexcept>
#include <xray/render/core/sources/rhi/rhi_types.h>

namespace xray { namespace render { namespace vulkan {
struct texture_block { size_t extent, bytes; };
inline texture_block sampled_texture_block(rhi::format format) {
    switch (format) {
        case rhi::format::r8g8b8a8_unorm: case rhi::format::r8g8b8a8_srgb: return {1,4};
        case rhi::format::bc1_unorm: case rhi::format::bc1_srgb: return {4,8};
        case rhi::format::bc2_unorm: case rhi::format::bc2_srgb:
        case rhi::format::bc3_unorm: case rhi::format::bc3_srgb: return {4,16};
        default: throw std::runtime_error("Unsupported sampled texture format");
    }
}
struct texture_mip_layout { size_t row_bytes, rows, size; };
inline texture_mip_layout sampled_mip_layout(rhi::format format,u32 width,u32 height) {
    if (!width || !height) throw std::runtime_error("Empty texture mip");
    auto block=sampled_texture_block(format);
    size_t columns=(size_t(width)-1)/block.extent+1, rows=(size_t(height)-1)/block.extent+1;
    if (columns>SIZE_MAX/block.bytes || rows>SIZE_MAX/(columns*block.bytes))
        throw std::runtime_error("Texture mip size overflow");
    return {columns*block.bytes,rows,columns*block.bytes*rows};
}
} } }
#endif
