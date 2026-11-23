#ifndef XRAY_VULKAN_SMOKE_MATERIALS_H_INCLUDED
#define XRAY_VULKAN_SMOKE_MATERIALS_H_INCLUDED
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <array>
#include "material_contract.h"
namespace xray { namespace render { namespace vulkan {
class smoke_materials {
    rhi::rhi_backend* m_backend;
    rhi::rhi_buffer_handle m_vertices = 0, m_indices = 0, m_frame = 0, m_object = 0;
    rhi::rhi_shader_handle m_vs = 0, m_ps = 0;
    rhi::rhi_texture_handle m_left = 0, m_right = 0;
    rhi::rhi_sampler_handle m_nearest = 0, m_linear = 0;
    bool m_checked = false, m_updated = false, m_recreated = false, m_sampler_changed = false;
    std::vector<unsigned char> m_left_pixels, m_right_pixels, m_mip;
    std::array<size_t,4> m_stress_capacity = {};
    rhi::rhi_texture_handle texture(bool left);
    void release();
public:
    explicit smoke_materials(rhi::rhi_backend&);
    ~smoke_materials();
    void draw(u32 presented, frame_constants const* camera = nullptr);
};
} } }
#endif
