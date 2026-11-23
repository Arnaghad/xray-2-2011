#ifndef XRAY_VULKAN_PARTICLE_RENDERER_H_INCLUDED
#define XRAY_VULKAN_PARTICLE_RENDERER_H_INCLUDED
#include <xray/render/vulkan/particle_resources.h>
#include "material_contract.h"
#include <xray/render/core/sources/rhi/rhi_backend.h>

namespace xray { namespace render { namespace vulkan {
class particle_renderer {
    struct geometry {std::shared_ptr<particle_billboard_batch const> data;rhi::rhi_buffer_handle vertices=0,indices=0;bool used=false;};
    struct image {std::shared_ptr<texture_data const> data;rhi::rhi_texture_handle handle=0;bool used=false;};
    rhi::rhi_backend* m_backend;
    rhi::rhi_shader_handle m_vs=0,m_ps=0;
    rhi::rhi_buffer_handle m_frame=0;
    rhi::rhi_sampler_handle m_sampler=0;
    std::vector<geometry> m_geometry;
    std::vector<image> m_images;
    void release();
public:
    explicit particle_renderer(rhi::rhi_backend&);
    ~particle_renderer();
    particle_renderer(particle_renderer const&)=delete;
    particle_renderer& operator=(particle_renderer const&)=delete;
    // Preflight/upload outside recording; caller retains immutable batches for draw.
    void prepare(std::vector<std::shared_ptr<particle_billboard_batch const>> const&);
    void begin_frame();
    void draw(std::vector<std::shared_ptr<particle_billboard_batch const>> const&,frame_constants const&);
    void collect();
};
} } }
#endif
