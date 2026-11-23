#ifndef XRAY_VULKAN_SMOKE_GEOMETRY_H_INCLUDED
#define XRAY_VULKAN_SMOKE_GEOMETRY_H_INCLUDED
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <xray/render/core/sources/rhi/rhi_backend.h>

namespace xray { namespace render { namespace vulkan {
class smoke_geometry {
public:
    explicit smoke_geometry(rhi::rhi_backend& backend);
    ~smoke_geometry();
    void draw(u32 presented);
private:
    void create();
    void release();
    rhi::rhi_backend* m_backend;
    std::vector<u32> m_triangle_code, m_mesh_code, m_pixel_code;
    rhi::rhi_shader_handle m_triangle = 0, m_mesh = 0, m_pixel = 0;
    rhi::rhi_buffer_handle m_vertices = 0, m_indices16 = 0, m_indices32 = 0;
    bool m_recreated = false;
    bool m_contracts_checked = false;
};
} } }
#endif
