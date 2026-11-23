#ifndef XRAY_VULKAN_DEFERRED_LIGHTING_H_INCLUDED
#define XRAY_VULKAN_DEFERRED_LIGHTING_H_INCLUDED
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <xray/render/vulkan/scene_resources.h>
#include <xray/render/vulkan/gbuffer_resources.h>
#include "material_contract.h"
#include "vulkan_scene_shadows.h"
#include <functional>

namespace xray { namespace render { namespace vulkan {
inline bool lighting_mode(scene_view_mode mode) {
    return mode==lit_view_mode || mode==lighting_view_mode || mode==lighting_diffuse_view_mode || mode==lighting_specular_view_mode || mode==lit_with_histogram_view_mode || mode==indirect_lighting_mode;
}
class deferred_lighting {
    rhi::rhi_backend* m_backend;
    std::array<rhi::rhi_texture_handle,3> m_targets={};
    rhi::rhi_shader_handle m_light=0,m_combine=0,m_environment=0;
    rhi::rhi_shader_handle m_ao_environment=0;
    rhi::rhi_buffer_handle m_constants=0;
    rhi::rhi_shader_handle m_shadow_light=0;
    rhi::rhi_buffer_handle m_shadow_constants=0;
    u32 m_width=0,m_height=0;
    u32 m_samples=1;
    void release();
public:
    explicit deferred_lighting(rhi::rhi_backend&,u32 samples=1);
    ~deferred_lighting();
    void prepare_shadows();
    rhi::rhi_texture_handle render(std::array<rhi::rhi_texture_handle,4> const& gbuffer,
        std::array<rhi::rhi_texture_handle,3> const& materials,gbuffer_camera_data const&,
        std::vector<scene_light> const&,lighting_environment const&,math::rectangle<math::float2> const&,u32,u32,
        rhi::rhi_shader_handle fullscreen_vs,rhi::rhi_sampler_handle nearest,scene_view_mode,
        std::function<shadow_binding(size_t)> const& shadows,rhi::rhi_texture_handle ao=0);
};
} } }
#endif
