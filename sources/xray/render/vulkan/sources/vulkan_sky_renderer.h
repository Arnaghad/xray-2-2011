#ifndef XRAY_VULKAN_SKY_RENDERER_H_INCLUDED
#define XRAY_VULKAN_SKY_RENDERER_H_INCLUDED
#include <xray/render/vulkan/sky_resources.h>
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include "material_contract.h"

namespace xray { namespace render { namespace vulkan {
struct sky_draw {
    std::shared_ptr<texture_data const> texture;
    object_constants constants={};
};
class sky_renderer {
    rhi::rhi_backend* m_backend;
    rhi::rhi_shader_handle m_vs=0,m_ps=0;
    rhi::rhi_buffer_handle m_constants=0;
    rhi::rhi_sampler_handle m_sampler=0;
    struct texture_entry { std::weak_ptr<texture_data const> data; rhi::rhi_texture_handle handle; };
    std::vector<texture_entry> m_textures;
    void release();
public:
    explicit sky_renderer(rhi::rhi_backend&);
    ~sky_renderer();
    void prepare(std::shared_ptr<texture_data const> const&);
    void collect();
    void draw(rhi::rhi_texture_handle color,rhi::rhi_texture_handle depth,sky_draw const&,
        math::rectangle<math::float2> const&,u32 width,u32 height);
};
} } }
#endif
