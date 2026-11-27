////////////////////////////////////////////////////////////////////////////
//	RHI Device
//	High-level interface for managing the RHI backend and resource lifecycle.
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_RHI_DEVICE_H_INCLUDED
#define XRAY_RENDER_RHI_DEVICE_H_INCLUDED

#include <memory>
#include <xray/type_extensions.h>
#include <xray/platform_surface.h>
#include "rhi_backend.h"
#include <xray/render/vulkan/library_linkage.h>

namespace xray {
namespace render {
namespace rhi {

class XRAY_RENDER_VULKAN_API rhi_device {
public:
	static rhi_device&	ref	();

	// Ownership is transferred even on failure. Backends use standard new/delete.
	bool            initialize(std::unique_ptr<rhi_backend> backend, platform::native_surface_handle surface = {});
	void			shutdown	();
	bool            begin_frame();
	bool            end_frame();
	bool            present();

	// --- Resource Factories ---
	rhi_buffer_handle	create_buffer(buffer_type type, size_t size, const void* data = nullptr);
	void			destroy_buffer(rhi_buffer_handle handle);
    void update_buffer(rhi_buffer_handle handle, void const* data, size_t size);

	rhi_texture_handle	create_texture(texture_type type, u32 width, u32 height, format fmt, bool mipmaps = true);
	void			destroy_texture(rhi_texture_handle handle);
    rhi_texture_handle create_texture(texture_desc const& desc, std::vector<texture_subresource_data> const& data);
    void update_texture(rhi_texture_handle handle, std::vector<texture_subresource_data> const& data);
    rhi_sampler_handle create_sampler(sampler_desc const& desc);
    void destroy_sampler(rhi_sampler_handle handle);

	rhi_shader_handle	create_shader(shader_stage stage, const void* bytecode, size_t size, char const* entry = "main");
	void			destroy_shader(rhi_shader_handle handle);

	// --- Backend Access ---
	rhi_backend*		get_backend	() { return m_backend.get(); }

private:
	rhi_device	() = default;
	~rhi_device	();
	rhi_device(rhi_device const&) = delete;
	rhi_device& operator=(rhi_device const&) = delete;

	enum class frame_state { idle, recording, submitted };
	frame_state m_frame_state = frame_state::idle;
	std::unique_ptr<rhi_backend> m_backend;
};

} // namespace rhi
} // namespace render
} // namespace xray

#endif // XRAY_RENDER_RHI_DEVICE_H_INCLUDED
