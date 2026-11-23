////////////////////////////////////////////////////////////////////////////
//	RHI Device Implementation
////////////////////////////////////////////////////////////////////////////

#include "rhi_device.h"

namespace xray {
namespace render {
namespace rhi {

rhi_device& rhi_device::ref() {
	static rhi_device instance;
	return instance;
}

bool rhi_device::initialize(std::unique_ptr<rhi_backend> backend, HWND hwnd) {
	if (m_backend || !backend) return false;
	try {
		if (!backend->initialize(hwnd)) {
			backend->shutdown();
			return false;
		}
	} catch (...) {
		backend->shutdown();
		return false;
	}
	m_backend = std::move(backend);
	return true;
}

void rhi_device::shutdown() {
	if (m_backend) {
		m_backend->shutdown();
		m_backend.reset();
	}
	m_frame_state = frame_state::idle;
}

rhi_device::~rhi_device() {
	shutdown();
}

bool rhi_device::begin_frame() {
	if (!m_backend || m_frame_state != frame_state::idle) return false;
	try {
		if (!m_backend->begin_frame()) return false;
	} catch (...) {
		shutdown();
		return false;
	}
	m_frame_state = frame_state::recording;
	return true;
}

bool rhi_device::end_frame() {
	if (!m_backend || m_frame_state != frame_state::recording) return false;
	try {
		if (m_backend->end_frame()) {
			m_frame_state = frame_state::submitted;
			return true;
		}
	} catch (...) {
		// A recording/submit failure invalidates the frame and backend.
	}
	shutdown();
	return false;
}

bool rhi_device::present() {
	if (!m_backend || m_frame_state != frame_state::submitted) return false;
	try {
		if (m_backend->present()) {
			m_frame_state = frame_state::idle;
			return true;
		}
	} catch (...) {
		// Swapchain recreation may fail after presentation.
	}
	shutdown();
	return false;
}

rhi_buffer_handle rhi_device::create_buffer(buffer_type type, size_t size, const void* data) {
	return m_backend ? m_backend->create_buffer(type, size, data) : RHI_INVALID_HANDLE;
}

void rhi_device::destroy_buffer(rhi_buffer_handle handle) {
	if (m_backend && handle != RHI_INVALID_HANDLE) {
		m_backend->destroy_buffer(handle);
	}
}

rhi_texture_handle rhi_device::create_texture(texture_type type, u32 width, u32 height, format fmt, bool mipmaps) {
	return m_backend ? m_backend->create_texture(type, width, height, fmt, mipmaps) : RHI_INVALID_HANDLE;
}

void rhi_device::destroy_texture(rhi_texture_handle handle) {
	if (m_backend && handle != RHI_INVALID_HANDLE) {
		m_backend->destroy_texture(handle);
	}
}

rhi_shader_handle rhi_device::create_shader(shader_stage stage, const void* bytecode, size_t size, char const* entry) {
	return m_backend ? m_backend->create_shader(stage, bytecode, size, entry) : RHI_INVALID_HANDLE;
}

void rhi_device::update_buffer(rhi_buffer_handle handle, void const* data, size_t size) {
    if (!m_backend) throw std::runtime_error("Device unavailable");
    m_backend->update_buffer(handle, data, size);
}
rhi_texture_handle rhi_device::create_texture(texture_desc const& desc, std::vector<texture_subresource_data> const& data) {
    return m_backend ? m_backend->create_texture(desc, data) : 0;
}
void rhi_device::update_texture(rhi_texture_handle handle, std::vector<texture_subresource_data> const& data) {
    if (!m_backend) throw std::runtime_error("Device unavailable");
    m_backend->update_texture(handle, data);
}
rhi_sampler_handle rhi_device::create_sampler(sampler_desc const& desc) { return m_backend ? m_backend->create_sampler(desc) : 0; }
void rhi_device::destroy_sampler(rhi_sampler_handle handle) { if (m_backend && handle) m_backend->destroy_sampler(handle); }

void rhi_device::destroy_shader(rhi_shader_handle handle) {
	if (m_backend && handle != RHI_INVALID_HANDLE) {
		m_backend->destroy_shader(handle);
	}
}

} // namespace rhi
} // namespace render
} // namespace xray
