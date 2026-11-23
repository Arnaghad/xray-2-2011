#ifndef XRAY_VULKAN_DEBUG_RENDERER_H_INCLUDED
#define XRAY_VULKAN_DEBUG_RENDERER_H_INCLUDED
#include <xray/render/base/common_types.h>
#include "material_contract.h"
#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <memory>
#include <span>
#include <array>
#include <vector>

namespace xray { namespace render { namespace vulkan {
struct debug_vertex {
    std::array<float,3> position;
    std::array<float,4> color;
    bool operator==(debug_vertex const&) const = default;
};
struct debug_batch {
    std::vector<debug_vertex> vertices;
    std::vector<u16> indices;
    bool triangles=false,depth=true;
    size_t bytes() const { return vertices.size()*sizeof(debug_vertex)+indices.size()*sizeof(u16); }
};
constexpr size_t debug_frame_bytes=8*1024*1024,debug_frame_batches=256;
debug_batch snapshot_debug(std::span<vertex_colored const>,std::span<u16 const>,bool triangles,bool depth);
class debug_renderer {
    struct entry {
        std::shared_ptr<debug_batch const> data;
        rhi::rhi_buffer_handle vertices=0,indices=0;
        bool used=false;
    };
    rhi::rhi_backend* m_backend;
    rhi::rhi_shader_handle m_vs=0,m_ps=0;
    rhi::rhi_buffer_handle m_frame=0;
    std::vector<entry> m_cache;
    void release();
public:
    explicit debug_renderer(rhi::rhi_backend&);
    ~debug_renderer();
    void begin_frame() { for (auto& item : m_cache) item.used=false; }
    void draw(std::vector<std::shared_ptr<debug_batch const>> const&,frame_constants const&,bool overlay=false);
    void collect();
};
} } }
#endif
