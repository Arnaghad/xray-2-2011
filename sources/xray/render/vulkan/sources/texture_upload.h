#ifndef XRAY_VULKAN_TEXTURE_UPLOAD_H_INCLUDED
#define XRAY_VULKAN_TEXTURE_UPLOAD_H_INCLUDED
#include <algorithm>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <xray/render/core/sources/rhi/rhi_types.h>
#include "texture_format.h"
namespace xray { namespace render { namespace vulkan {
struct packed_texture_upload { std::vector<unsigned char> bytes; std::vector<size_t> offsets; };
inline packed_texture_upload pack_texture(rhi::texture_desc const& d, std::vector<rhi::texture_subresource_data> const& data) {
    bool cube=d.type==rhi::texture_type::type_cube;
    bool array=d.type==rhi::texture_type::type_2d_array;
    if ((!cube && !array && d.type!=rhi::texture_type::type_2d) || !d.width || !d.height
        || (array ? !d.array_layers || d.array_layers>16 : d.array_layers!=(cube ? 6u : 1u)) || (cube && d.width!=d.height)
        || d.usage != rhi::texture_usage::sampled || !d.mip_levels || d.samples!=1)
        throw std::runtime_error("Expected sampled 2D, 1..16-layer 2D array or square six-face cubemap");
    u32 levels = 1;
    for (u32 size = std::max(d.width, d.height); size > 1; size >>= 1) ++levels;
    if (d.mip_levels > levels || (!data.empty() && data.size() != size_t(d.mip_levels)*d.array_layers))
        throw std::runtime_error("Texture upload must contain each declared layer/mip exactly once");
    packed_texture_upload packed;
    for (u32 layer=0;layer<d.array_layers;++layer)
    for (u32 mip = 0; mip < d.mip_levels; ++mip) {
        auto layout=sampled_mip_layout(d.pixel_format,std::max(1u,d.width>>mip),std::max(1u,d.height>>mip));
        if (layout.size > SIZE_MAX - packed.bytes.size())
            throw std::runtime_error("Texture upload size overflow");
        size_t row = layout.row_bytes, h = layout.rows, offset = packed.bytes.size();
        // Every mip occupies whole texel blocks, so offsets also satisfy BC copy alignment.
        packed.offsets.push_back(offset); packed.bytes.resize(offset + layout.size);
        if (data.empty()) continue;
        rhi::texture_subresource_data const* source = nullptr;
        for (auto const& item : data) if (item.mip_level == mip && item.layer == layer) {
            if (source) throw std::runtime_error("Duplicate mip upload"); source = &item;
        }
        if (!source || !source->data) throw std::runtime_error("Missing mip data");
        size_t pitch = source->row_pitch ? source->row_pitch : row;
        if (pitch < row || (h - 1 && pitch > (SIZE_MAX - row) / (h - 1)) || source->size < pitch * (h - 1) + row)
            throw std::runtime_error("Texture row pitch/data size invalid");
        for (size_t y = 0; y < h; ++y)
            std::memcpy(packed.bytes.data() + offset + y * row, static_cast<unsigned char const*>(source->data) + y * pitch, row);
    }
    return packed;
}
} } }
#endif
