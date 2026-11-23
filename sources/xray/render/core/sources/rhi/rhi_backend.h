////////////////////////////////////////////////////////////////////////////
//	RHI Backend Interface
//	Abstract base class for different graphics API implementations.
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_RHI_BACKEND_H_INCLUDED
#define XRAY_RENDER_RHI_BACKEND_H_INCLUDED

#include <cstddef>
#include <stdexcept>
#include <xray/macro_extensions.h>
#include <xray/os_include.h>
#include "rhi_types.h"
#include "rhi_enums.h"

namespace xray {
namespace render {
namespace rhi {

class rhi_backend {
public:
	virtual ~rhi_backend() = default;

	// --- Device Management ---
	virtual bool			initialize	(HWND hwnd = nullptr) = 0;
	virtual void            shutdown() noexcept = 0;

	// Clear values are set before begin_frame. Commands are recorded only
	// between begin_frame and end_frame; present never begins another frame.
	virtual bool            begin_frame() = 0;
	virtual bool            end_frame() = 0;

    // begin_frame opens the presentation pass for existing callers. Explicit
    // offscreen work ends it, records passes/transitions, then resumes it.
    virtual void begin_render_pass(render_pass_desc const&) { throw std::runtime_error("Offscreen passes unsupported"); }
    virtual void end_render_pass() { throw std::runtime_error("Explicit passes unsupported"); }
    virtual void begin_presentation_pass(bool preserve_contents = false) { throw std::runtime_error("Explicit presentation pass unsupported"); }
    virtual void transition_texture(rhi_texture_handle, resource_state) { throw std::runtime_error("Texture transitions unsupported"); }
    virtual void resolve_texture(rhi_texture_handle, rhi_texture_handle) { throw std::runtime_error("Multisample resolve unsupported"); }
    virtual void dispatch_compute_buffers(rhi_shader_handle, rhi_buffer_handle const*, u32, rhi_buffer_handle, u32, u32, u32) { throw std::runtime_error("Compute dispatch unsupported"); }
    virtual void compute_to_vertex_barrier(rhi_buffer_handle) {}
    virtual u32 register_bindless_texture(rhi_texture_handle) { return 0; }
    virtual bool supports_bindless() const { return false; }

	// --- Resource Creation ---
	virtual rhi_buffer_handle	create_buffer(buffer_type type, size_t size, const void* data) = 0;
	virtual void			destroy_buffer(rhi_buffer_handle handle) = 0;
    virtual void update_buffer(rhi_buffer_handle, void const*, size_t) { throw std::runtime_error("Buffer updates unsupported"); }

	virtual rhi_texture_handle	create_texture(texture_type type, u32 width, u32 height, format fmt, bool mipmaps) = 0;
	virtual void			destroy_texture(rhi_texture_handle handle) = 0;
    virtual rhi_texture_handle create_texture(texture_desc const&, std::vector<texture_subresource_data> const&) { return 0; }
    virtual void update_texture(rhi_texture_handle, std::vector<texture_subresource_data> const&) { throw std::runtime_error("Texture updates unsupported"); }
    virtual rhi_sampler_handle create_sampler(sampler_desc const&) { return 0; }
    virtual void destroy_sampler(rhi_sampler_handle) {}
    virtual void bind_sampler(u32, rhi_sampler_handle) { throw std::runtime_error("Sampler bindings unsupported"); }

	virtual rhi_shader_handle	create_shader(shader_stage stage, const void* bytecode, size_t size, char const* entry = "main") = 0;
	virtual void			destroy_shader(rhi_shader_handle handle) = 0;

	// --- State Management & Command Execution ---
	virtual void			bind_vertex_buffer(rhi_buffer_handle handle, u32 offset) = 0;
	virtual void			bind_index_buffer(rhi_buffer_handle handle, u32 offset, index_format type = index_format::uint16) = 0;
	virtual void            set_vertex_layout(vertex_input_layout const& layout) = 0;
	virtual void			bind_constant_buffer(u32 slot, rhi_buffer_handle handle) = 0;
	virtual void			bind_texture(u32 slot, rhi_texture_handle handle) = 0;
	virtual void			bind_shader(shader_stage stage, rhi_shader_handle handle) = 0;
	
	virtual void			draw(u32 vertex_count, u32 start_vertex) = 0;
	virtual void			draw_indexed(u32 index_count, u32 start_index, u32 start_vertex) = 0;

	virtual void			set_viewport(float x, float y, float width, float height) = 0;
	virtual void			clear_render_target(format fmt, float r, float g, float b, float a) = 0;

	// --- Swapchain ---
	virtual bool            present() = 0;
	virtual void			resize_swapchain(u32 width, u32 height) = 0;

	// --- Pipeline State (Phase 2) ---
	virtual void			set_topology(topology topo) = 0;
	virtual void			set_rasterizer_state(fill_mode fill, cull_mode cull) = 0;
    virtual void set_depth_bias(float constant, float slope) {
        if (constant!=0 || slope!=0) throw std::runtime_error("Depth bias unsupported");
    }
	virtual void			set_depth_stencil_state(bool depth_enable, bool depth_write, comparison_func func) = 0;
    virtual void set_stencil_state(bool enable, u32 reference = 0, u32 read_mask = 255, u32 write_mask = 255,
        comparison_func compare = comparison_func::always, stencil_op pass = stencil_op::keep) {
        if (enable) throw std::runtime_error("Stencil state unsupported");
    }
	virtual void			set_blend_state(bool enable, blend src, blend dst, blend_op op) = 0;
	virtual void			set_scissor(u32 x, u32 y, u32 width, u32 height) = 0;
};

} // namespace rhi
} // namespace render
} // namespace xray

#endif // XRAY_RENDER_RHI_BACKEND_H_INCLUDED
