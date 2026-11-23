#pragma once
#include "vulkan_static_renderer.h"
#include <xray/render/facade/editor_selection.h>
namespace xray::render::vulkan {
class selection_renderer {
    rhi::rhi_backend* backend;
    rhi::rhi_texture_handle mask=0;
    rhi::rhi_shader_handle vertex=0,pixel=0;
    rhi::rhi_buffer_handle constants=0;
    u32 width=0,height=0;
public:
    explicit selection_renderer(rhi::rhi_backend&);
    ~selection_renderer();
    void draw(std::vector<model_draw> const&,static_renderer&,frame_constants const&,editor_selection_settings const&,math::rectangle<math::float2> const&,u32,u32);
};
}
