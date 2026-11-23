#ifndef XRAY_VULKAN_SCREEN_EFFECTS_H_INCLUDED
#define XRAY_VULKAN_SCREEN_EFFECTS_H_INCLUDED
#include <xray/render/vulkan/gbuffer_resources.h>
#include <xray/render/facade/postprocess_settings.h>
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <functional>
namespace xray { namespace render { namespace vulkan {
class screen_effects {
    rhi::rhi_backend* m_backend;
    std::array<rhi::rhi_texture_handle,4> m_targets={};
    std::array<rhi::rhi_shader_handle,3> m_shaders={};
    rhi::rhi_sampler_handle m_nearest=0,m_linear=0;
    rhi::rhi_buffer_handle m_ao=0,m_composite=0;
    u32 m_width=0,m_height=0;
    void release();void resize(u32,u32);
public:
    explicit screen_effects(rhi::rhi_backend&);
    ~screen_effects();
    rhi::rhi_texture_handle ssao(rhi::rhi_texture_handle depth,rhi::rhi_texture_handle normal,gbuffer_camera_data const&,
        rhi::rhi_shader_handle,math::rectangle<math::float2> const&,u32,u32,postprocess_settings const&);
    rhi::rhi_texture_handle distort(rhi::rhi_texture_handle hdr,rhi::rhi_texture_handle depth,rhi::rhi_shader_handle,
        math::rectangle<math::float2> const&,u32,u32,postprocess_settings const&,std::function<void()> const&);
    rhi::rhi_texture_handle distortion_field(rhi::rhi_texture_handle,rhi::rhi_shader_handle,math::rectangle<math::float2> const&,u32,u32,std::function<void()> const&);
};
} } }
#endif
