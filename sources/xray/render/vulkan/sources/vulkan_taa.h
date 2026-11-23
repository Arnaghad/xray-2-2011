#ifndef XRAY_VULKAN_TAA_H_INCLUDED
#define XRAY_VULKAN_TAA_H_INCLUDED
#include "material_contract.h"
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <xray/render/facade/postprocess_settings.h>
#include <xray/render/vulkan/gbuffer_resources.h>
#include <map>
#include <tuple>

namespace xray { namespace render { namespace vulkan {
class taa_renderer {
public:
    using key=std::tuple<u64,u64,float,float,float,float>;
    struct context {
        key identity;
        frame_constants camera={};
        gbuffer_camera_data gbuffer;
        temporal_constants constants={};
        float jitter_x=0,jitter_y=0;
    };
private:
    struct history {
        std::array<rhi::rhi_texture_handle,4> targets={};
        frame_constants camera={};
        std::array<float,16> previous_vp={};
        u32 width=0,height=0,samples=1,frame=0,sequence=0,index=0,reset=0;
        u64 signature=0;
        bool valid=false,initialized=false;
    };
    rhi::rhi_backend* m_backend;
    std::map<key,history> m_history;
    std::array<rhi::rhi_shader_handle,4> m_shaders={};
    rhi::rhi_buffer_handle m_constants=0;
    rhi::rhi_sampler_handle m_sampler=0;
    void release(history&);
public:
    explicit taa_renderer(rhi::rhi_backend&);
    ~taa_renderer();
    context begin(key const&,u32,u32,u32,u64,frame_constants const&,postprocess_settings const&);
    rhi::rhi_texture_handle render(context const&,rhi::rhi_texture_handle,rhi::rhi_texture_handle,rhi::rhi_shader_handle);
    void collect(u32 frame);
    void invalidate() { for (auto& [identity,value] : m_history) value.valid=false; }
};
} } }
#endif
