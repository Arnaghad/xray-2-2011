#ifndef XRAY_VULKAN_GBUFFER_RENDERER_H_INCLUDED
#define XRAY_VULKAN_GBUFFER_RENDERER_H_INCLUDED
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <xray/render/vulkan/gbuffer_resources.h>
#include <xray/render/facade/scene_view_mode.h>
#include "material_contract.h"
#include "vulkan_deferred_lighting.h"
#include "vulkan_sky_renderer.h"
#include "vulkan_bloom.h"
#include "vulkan_fxaa.h"
#include "vulkan_decal_renderer.h"
#include "vulkan_screen_effects.h"

namespace xray { namespace render { namespace vulkan {
class gbuffer_renderer {
    rhi::rhi_backend* m_backend;
    std::array<rhi::rhi_texture_handle,4> m_targets={};
    std::array<rhi::rhi_texture_handle,3> m_material_targets={};
    u32 m_width=0,m_height=0;
    u32 m_samples=1;
    rhi::rhi_texture_handle m_resolved=0;
    rhi::rhi_shader_handle m_vs=0,m_ps=0,m_stencil_ps=0;
    rhi::rhi_sampler_handle m_sampler=0;
    rhi::rhi_buffer_handle m_constants=0;
    std::unique_ptr<deferred_lighting> m_lighting;
    std::unique_ptr<sky_renderer> m_sky;
    std::unique_ptr<bloom_renderer> m_bloom;
    std::unique_ptr<fxaa_renderer> m_fxaa;
    std::unique_ptr<decal_renderer> m_decals;
    bool m_decals_applied=false;
    std::unique_ptr<screen_effects> m_effects;
    void release();
public:
    explicit gbuffer_renderer(rhi::rhi_backend&);
    ~gbuffer_renderer();
    void prepare(scene_view_mode,bool shadows=false);
    void prepare_effects(){if(!m_effects)m_effects=std::make_unique<screen_effects>(*m_backend);}
    void begin(u32 width,u32 height,u32 samples=1);
    void begin_material();
    void prepare_decals(std::vector<decal_draw> const&);
    void draw_decals(std::vector<decal_draw> const&,gbuffer_camera_data const&,math::float4x4 const&,math::rectangle<math::float2> const&,u32,u32);
    void collect_decals(){if(m_decals)m_decals->collect();}
    void prepare_sky(std::shared_ptr<texture_data const> const&);
    void collect_sky() { if (m_sky) m_sky->collect(); }
    void finish(scene_view_mode,gbuffer_camera_data const&,math::rectangle<math::float2> const&,u32 width,u32 height,
        std::vector<scene_light> const& lights,lighting_environment const&,std::function<shadow_binding(size_t)> const& shadows={},sky_draw const& sky={},std::function<void(rhi::rhi_texture_handle,u32)> const& forward={},postprocess_settings const& postprocess={},
        std::function<rhi::rhi_texture_handle(rhi::rhi_texture_handle,rhi::rhi_texture_handle,rhi::rhi_shader_handle)> const& temporal={},std::function<void()> const& distortion={});
};
} } }
#endif
