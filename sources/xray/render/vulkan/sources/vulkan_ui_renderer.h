#ifndef XRAY_VULKAN_UI_RENDERER_H_INCLUDED
#define XRAY_VULKAN_UI_RENDERER_H_INCLUDED
#include <xray/render/facade/ui_renderer.h>
#include <xray/render/vulkan/material_resources.h>
#include "material_contract.h"
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <span>
namespace xray { namespace ui { struct font; } namespace render { namespace vulkan {
struct ui_vertex {
    std::array<float,3> position;
    std::array<float,4> color;
    std::array<float,2> uv;
    bool operator==(ui_vertex const&) const=default;
};
struct ui_batch {
    std::vector<ui_vertex> vertices;
    std::vector<u16> indices;
    std::shared_ptr<texture_data const> texture;
    ui::draw_options options;
    unsigned mode=2;
    bool lines=false;
    size_t bytes() const { return vertices.size()*sizeof(ui_vertex)+indices.size()*sizeof(u16); }
};
constexpr size_t ui_frame_bytes=8*1024*1024,ui_frame_batches=256;
void validate_ui(std::span<ui::vertex const>,int primitives,int points,ui::draw_options const&);
ui_batch snapshot_ui(std::span<ui::vertex const>,int primitives,int points,ui::draw_options const&);
std::array<u32,4> ui_scissor(ui::draw_options const&,std::array<float,4> const& viewport,u32 width,u32 height);
std::vector<ui::vertex> layout_ui_text(pcstr,xray::ui::font const&,math::float2 const&,math::color const&,math::color const&,u32,bool,u32,u32);
class ui_renderer {
    struct geometry {
        std::shared_ptr<ui_batch const> data;
        rhi::rhi_buffer_handle vertices=0,indices=0;
        bool used=false;
    };
    struct texture {
        std::shared_ptr<texture_data const> data;
        rhi::rhi_texture_handle handle=0;
        bool used=false;
    };
    rhi::rhi_backend* m_backend;
    rhi::rhi_shader_handle m_vs=0;
    std::array<rhi::rhi_shader_handle,3> m_ps={};
    rhi::rhi_buffer_handle m_frame=0;
    rhi::rhi_sampler_handle m_sampler=0;
    std::vector<geometry> m_geometry;
    std::vector<texture> m_textures;
    void release();
public:
    explicit ui_renderer(rhi::rhi_backend&);
    ~ui_renderer();
    void prepare(std::vector<std::shared_ptr<ui_batch const>> const&);
    void begin_frame();
    void draw(std::vector<std::shared_ptr<ui_batch const>> const&,std::array<float,4> const& viewport,u32 width,u32 height);
    void collect();
};
} } }
#endif
