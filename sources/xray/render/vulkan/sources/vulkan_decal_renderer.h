#ifndef XRAY_VULKAN_DECAL_RENDERER_H_INCLUDED
#define XRAY_VULKAN_DECAL_RENDERER_H_INCLUDED
#include <xray/render/vulkan/decal_resources.h>
#include <xray/render/vulkan/gbuffer_resources.h>
#include <xray/render/core/sources/rhi/rhi_backend.h>
namespace xray { namespace render { namespace vulkan {
class decal_renderer {
    rhi::rhi_backend* m_backend;
    rhi::rhi_shader_handle m_ps=0;
    rhi::rhi_buffer_handle m_constants=0;
    rhi::rhi_sampler_handle m_sampler=0;
    std::array<rhi::rhi_texture_handle,2> m_scratch={};
    u32 m_width=0,m_height=0;
    struct image {std::weak_ptr<texture_data const> data;rhi::rhi_texture_handle handle;bool used=false;};
    std::vector<image> m_images;
    void release();
public:
    explicit decal_renderer(rhi::rhi_backend&);
    ~decal_renderer();
    void prepare(std::vector<decal_draw> const&);
    void collect();
    void render(std::vector<decal_draw> const&,gbuffer_camera_data const&,math::float4x4 const& view,
        math::rectangle<math::float2> const&,u32,u32,rhi::rhi_shader_handle,
        rhi::rhi_texture_handle packed,rhi::rhi_texture_handle& normal,rhi::rhi_texture_handle& albedo);
};
} } }
#endif
