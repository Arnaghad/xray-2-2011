#ifndef XRAY_VULKAN_ATTACHMENT_CONTRACT_H_INCLUDED
#define XRAY_VULKAN_ATTACHMENT_CONTRACT_H_INCLUDED
#include <xray/render/core/sources/rhi/rhi_types.h>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
inline bool depth_format(rhi::format f) {
    return f == rhi::format::d32_float || f == rhi::format::d24_s8_unorm;
}
inline void validate_attachment(rhi::texture_desc const& d) {
    if (d.type != rhi::texture_type::type_2d || !d.width || !d.height || d.width > 16384 || d.height > 16384
        || d.mip_levels != 1 || d.array_layers != 1 || (d.samples!=1 && d.samples!=2 && d.samples!=4 && d.samples!=8))
        throw std::runtime_error("Attachments require a bounded single-mip/layer 2D image");
    if (d.usage == rhi::texture_usage::depth_stencil && depth_format(d.pixel_format)) return;
    if (d.usage == rhi::texture_usage::render_target) switch (d.pixel_format) {
        case rhi::format::r8g8b8a8_unorm: case rhi::format::r8g8b8a8_srgb:
        case rhi::format::r16g16b16a16_float: case rhi::format::r16g16b16a16_unorm:
        case rhi::format::r16g16_unorm: case rhi::format::r32g32b32a32_float: case rhi::format::r32_float: return;
        default: break;
    }
    throw std::runtime_error("Unsupported attachment format/usage");
}
inline void validate_attachment_ops(rhi::attachment_load load, rhi::attachment_store store, bool valid) {
    if (load != rhi::attachment_load::load && load != rhi::attachment_load::clear && load != rhi::attachment_load::discard)
        throw std::runtime_error("Unknown attachment load action");
    if (store != rhi::attachment_store::store && store != rhi::attachment_store::discard)
        throw std::runtime_error("Unknown attachment store action");
    if (load == rhi::attachment_load::load && !valid) throw std::runtime_error("Cannot load undefined/discarded attachment contents");
}
} } }
#endif
