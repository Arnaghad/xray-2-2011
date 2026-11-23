#ifndef XRAY_VULKAN_TERRAIN_RENDERER_H_INCLUDED
#define XRAY_VULKAN_TERRAIN_RENDERER_H_INCLUDED
#include <xray/render/vulkan/terrain_resources.h>
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include "material_contract.h"
namespace xray { namespace render { namespace vulkan {
struct terrain_draw {
    render_model_instance_ptr model;
    std::shared_ptr<terrain_cell_data const> cell;
    math::float4x4 transform;
    std::vector<std::shared_ptr<texture_data const>> diffuse;
    std::vector<std::shared_ptr<texture_data const>> normal,specular;
    terrain_memory_profile memory_profile=terrain_memory_profile::standard_256_mib;
};
XRAY_RENDER_VULKAN_API void validate_terrain_draw(terrain_draw const&);
class terrain_renderer {
    rhi::rhi_backend* m_backend;
    rhi::rhi_shader_handle m_vs=0,m_ps=0;
    rhi::rhi_shader_handle m_deferred_vs=0,m_material_vs=0,m_gbuffer_ps=0,m_material_ps=0;
    rhi::rhi_buffer_handle m_frame=0,m_object=0;
    rhi::rhi_buffer_handle m_surface=0,m_material=0;
    rhi::rhi_sampler_handle m_sampler=0;
    struct geometry { std::weak_ptr<static_mesh_data const> mesh; rhi::rhi_buffer_handle vertices=0,indices=0; };
    struct images { std::vector<std::weak_ptr<texture_data const>> sources; rhi::rhi_texture_handle handle=0; };
    std::vector<geometry> m_geometry;
    std::vector<images> m_images;
    void release();
    void prepare_deferred();
public:
    explicit terrain_renderer(rhi::rhi_backend&);
    ~terrain_renderer();
    void prepare(std::vector<terrain_draw> const&);
    void draw(std::vector<terrain_draw> const&,frame_constants const&,float far_plane=0,bool material_pass=false);
    void collect();
};
} } }
#endif
