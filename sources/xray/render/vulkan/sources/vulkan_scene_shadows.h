#ifndef XRAY_VULKAN_SCENE_SHADOWS_H_INCLUDED
#define XRAY_VULKAN_SCENE_SHADOWS_H_INCLUDED
#include "vulkan_static_renderer.h"
#include <xray/render/vulkan/gbuffer_resources.h>

namespace xray { namespace render { namespace vulkan {
struct shadow_cascade {
    math::float4x4 view,projection;
    std::array<float,16> receiver={};
    shadow_cascade() { view.identity(); projection.identity(); }
};
struct scene_shadow_plan {
    std::array<shadow_cascade,6> cascades;
    std::array<float,16> point_receiver={};
    std::array<float,4> splits={};
    u32 count=0,resolution=0,face_mask=63;
    float transparency=0;
};
XRAY_RENDER_VULKAN_API scene_shadow_plan plan_directional_shadows(scene_light const&,math::float4x4 const& view,
    math::float4x4 const& projection,std::vector<model_draw> const& casters);
XRAY_RENDER_VULKAN_API scene_shadow_plan plan_spot_shadows(scene_light const&,math::float4x4 const& view);
XRAY_RENDER_VULKAN_API scene_shadow_plan plan_point_shadows(scene_light const&,math::float4x4 const& view);
inline u32 shadow_atlas_grid(u32 count) { return count==1 ? 1 : count<=4 ? 2 : 3; }
struct shadow_binding {
    rhi::rhi_texture_handle texture=0;
    rhi::rhi_sampler_handle sampler=0;
    scene_shadow_plan const* plan=nullptr;
};
class scene_shadows {
    rhi::rhi_backend* m_backend;
    struct atlas_entry { rhi::rhi_texture_handle texture=0;u32 extent=0;uint64_t last_use=0; };
    std::array<atlas_entry,3> m_atlases={};
    uint64_t m_use_serial=0;
    rhi::rhi_sampler_handle m_sampler=0;
public:
    explicit scene_shadows(rhi::rhi_backend&);
    ~scene_shadows();
    shadow_binding render(scene_shadow_plan const&,std::vector<model_draw> const&,static_renderer*);
};
} } }
#endif
