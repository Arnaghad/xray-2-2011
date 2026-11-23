#ifndef XRAY_VULKAN_STATIC_RENDERER_H_INCLUDED
#define XRAY_VULKAN_STATIC_RENDERER_H_INCLUDED
#include <xray/render/vulkan/model_resources.h>
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <unordered_map>
#include "material_contract.h"

namespace xray { namespace render { namespace vulkan {
struct forward_lighting_context {
    std::array<float,4> light_direction={0.f,-1.f,0.f,0.f};
    std::array<float,4> light_color={1.f,1.f,1.f,0.f};
    std::array<float,4> ambient_color={.2f,.2f,.2f,100.f};
    std::array<float,4> viewport={0.f,0.f,1.f,1.f};
};
class static_renderer {
    struct geometry {
        std::weak_ptr<static_mesh_data const> mesh;
        std::weak_ptr<skeletal_mesh_data const> skin;
        uintptr_t instance_key=0;
        rhi::rhi_buffer_handle vertices=0, indices=0, skin_source=0;
        u64 skin_revision=0;
        bool used=false;
    };
    rhi::rhi_backend* m_backend;
    rhi::rhi_shader_handle m_vs=0, m_skin_cs=0;
    rhi::rhi_buffer_handle m_skin_constants=0;
    rhi::rhi_shader_handle m_gbuffer_vs=0;
    std::array<rhi::rhi_shader_handle,4> m_ps={};
    std::array<rhi::rhi_shader_handle,4> m_mask_ps={};
    rhi::rhi_shader_handle m_diagnostic_ps=0,m_diagnostic_vs=0;
    rhi::rhi_buffer_handle m_diagnostic_object=0;
    std::array<float,4> m_fragment_work={};
    std::array<rhi::rhi_shader_handle,4> m_shadow_ps={};
    rhi::rhi_shader_handle m_forward_vs=0;
    rhi::rhi_buffer_handle m_forward_object=0;
    std::unordered_map<u32,rhi::rhi_shader_handle> m_forward_ps;
    rhi::rhi_shader_handle m_distortion_ps=0;
    std::unordered_map<u32,rhi::rhi_shader_handle> m_gbuffer_ps;
    rhi::rhi_shader_handle m_material_vs=0;
    std::unordered_map<u32,rhi::rhi_shader_handle> m_material_ps;
    rhi::rhi_buffer_handle m_material_object=0;
    rhi::rhi_buffer_handle m_frame=0, m_object=0, m_surface_object=0;
    std::vector<geometry> m_geometry;
    struct texture_entry {
        std::weak_ptr<texture_data const> data;
        rhi::rhi_texture_handle handle=0;
    };
    struct sampler_entry {
        std::weak_ptr<material_configuration const> data;
        rhi::rhi_sampler_handle handle=0;
    };
    std::vector<texture_entry> m_textures;
    std::vector<sampler_entry> m_samplers;
    void prepare_pixel_shader(unsigned variant, bool gbuffer = false);
    void prepare_material_shader(unsigned variant);
    void prepare_shadow_shader(unsigned variant);
    void prepare_forward_shader(unsigned variant, u32 samples = 1);
    void release();
public:
    explicit static_renderer(rhi::rhi_backend&);
    ~static_renderer();
    void prepare(std::vector<model_draw> const&, bool gbuffer = false,bool material_pass=false,bool shadow_pass=false,bool forward_pass=false,bool distortion_pass=false,bool has_scene_depth=false,u32 scene_depth_samples=1);
    bool has_skinning(std::vector<model_draw> const&) const;
    void run_skinning(std::vector<model_draw> const&);
    void draw(std::vector<model_draw> const&, frame_constants const&, float gbuffer_far = 0,bool material_pass=false,bool shadow_pass=false,bool forward_pass=false,bool distortion_pass=false,float phase=0,bool mask_pass=false,scene_view_mode diagnostic=unlit_view_mode,u32 width=0,u32 height=0,rhi::rhi_texture_handle scene_depth=0,u32 scene_depth_samples=1,forward_lighting_context const& forward_lighting={});
    void prepare_diagnostic();
    void prepare_mask(std::vector<model_draw> const&);
    void draw_mask(std::vector<model_draw> const& models,frame_constants const& camera){draw(models,camera,0,false,false,false,false,0,true);}
    void collect();
};
} } }
#endif
