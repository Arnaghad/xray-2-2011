#ifndef XRAY_VULKAN_BLOOM_H_INCLUDED
#define XRAY_VULKAN_BLOOM_H_INCLUDED
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <xray/render/facade/postprocess_settings.h>
#include <xray/math_rectangle.h>
#include <array>

namespace xray { namespace render { namespace vulkan {
class bloom_renderer {
    rhi::rhi_backend* m_backend;
    std::array<rhi::rhi_texture_handle,3> m_targets={};
    std::array<rhi::rhi_shader_handle,3> m_shaders={};
    rhi::rhi_sampler_handle m_sampler=0;
    rhi::rhi_buffer_handle m_constants=0;
    u32 m_width=0,m_height=0;
    void release();
public:
    explicit bloom_renderer(rhi::rhi_backend&);
    ~bloom_renderer();
    rhi::rhi_texture_handle render(rhi::rhi_texture_handle scene,rhi::rhi_shader_handle fullscreen_vs,
        math::rectangle<math::float2> const&,u32 width,u32 height,postprocess_settings const&);
};
} } }
#endif
