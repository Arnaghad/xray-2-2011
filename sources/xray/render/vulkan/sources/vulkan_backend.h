////////////////////////////////////////////////////////////////////////////
//	Vulkan Backend
//	Implementation of the RHI backend for Vulkan API.
//	Phase 2: Pipeline State & Resources
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_VULKAN_BACKEND_H_INCLUDED
#define XRAY_RENDER_VULKAN_BACKEND_H_INCLUDED

#include <vector>
#include <unordered_map>
#include <list>
#include <map>
#include <set>
#include <deque>
#include <functional>
#include <array>
#include <stdexcept>

#include <xray/noncopyable.h>

#include <xray/render/core/sources/rhi/rhi_backend.h>
#include <xray/render/core/sources/rhi/rhi_enums.h>
#include <xray/render/vulkan/api.h>
#include "vulkan_memory.h"
#include "vulkan_resources.h"
#include "pipeline_key.h"
#include "vulkan_presentation_output.h"
#include <xray/render/vulkan/performance.h>
#include <memory>

namespace xray {
namespace render {
namespace vulkan {

// Maximum number of frames that can be processed concurrently.
// With 2 frames in flight, the CPU can prepare frame N+1 while the GPU
// is still rendering frame N, without stalling on vkQueueWaitIdle.
static constexpr uint32_t MAX_FRAMES_IN_FLIGHT = 2;

class vulkan_backend : public rhi::rhi_backend {
public:
	vulkan_backend();
	~vulkan_backend();
    std::shared_ptr<presentation_output> output() const { return m_output; }
    // All output operations run on the renderer owner, between recorded frames.
    std::shared_ptr<presentation_output> create_output(HWND);
    void adopt_output(std::shared_ptr<presentation_output> const&);
    std::vector<std::shared_ptr<presentation_output>> outputs() const;
    void select_output(std::shared_ptr<presentation_output> const&);
    void close_output(std::shared_ptr<presentation_output> const&);
    size_t output_count() const { return m_outputs.size(); }
    struct captured_frame { u32 width=0,height=0; std::vector<unsigned char> bgra; };
    captured_frame capture_frame();
    bool supports_samples(rhi::format,u32) const;
    u32 raster_subpixel_bits() const;
    void resolve_texture(rhi::rhi_texture_handle,rhi::rhi_texture_handle) override;
    void dispatch_compute_buffers(rhi::rhi_shader_handle, rhi::rhi_buffer_handle const*, u32, rhi::rhi_buffer_handle, u32, u32, u32) override;
    void compute_to_vertex_barrier(rhi::rhi_buffer_handle) override;
    u32 register_bindless_texture(rhi::rhi_texture_handle) override;
    bool supports_bindless() const override { return m_bindless_enabled; }
    bool synchronization2_enabled() const { return m_synchronization2_enabled; }
    bool dynamic_rendering_enabled() const { return m_dynamic_rendering_enabled; }
    std::array<size_t,2> capture_storage() const;
    // Owner-thread opt-in timing. Memory accounting is active for every session.
    void enable_performance_timestamps();
    performance_snapshot performance() const;
    heap_budget_snapshot memory_heaps() const;

	// --- rhi::rhi_backend interface ---
	virtual bool			initialize	(HWND hwnd = nullptr) override;
	virtual void            shutdown() noexcept override;

	virtual rhi::rhi_buffer_handle	create_buffer(rhi::buffer_type type, size_t size, const void* data) override;
	virtual void			destroy_buffer(rhi::rhi_buffer_handle handle) override;
    void update_buffer(rhi::rhi_buffer_handle, void const*, size_t) override;

	virtual rhi::rhi_texture_handle	create_texture(rhi::texture_type type, u32 width, u32 height, rhi::format fmt, bool mipmaps) override;
	virtual void			destroy_texture(rhi::rhi_texture_handle handle) override;
    rhi::rhi_texture_handle create_texture(rhi::texture_desc const&, std::vector<rhi::texture_subresource_data> const&) override;
    void update_texture(rhi::rhi_texture_handle, std::vector<rhi::texture_subresource_data> const&) override;
    rhi::rhi_sampler_handle create_sampler(rhi::sampler_desc const&) override;
    void destroy_sampler(rhi::rhi_sampler_handle) override;
    void bind_sampler(u32, rhi::rhi_sampler_handle) override;

	virtual rhi::rhi_shader_handle	create_shader(rhi::shader_stage stage, const void* bytecode, size_t size, char const* entry = "main") override;
	virtual void			destroy_shader(rhi::rhi_shader_handle handle) override;

	virtual void			bind_vertex_buffer(rhi::rhi_buffer_handle handle, u32 offset) override;
	virtual void			bind_index_buffer(rhi::rhi_buffer_handle handle, u32 offset, rhi::index_format type = rhi::index_format::uint16) override;
	virtual void            set_vertex_layout(rhi::vertex_input_layout const& layout) override;
	virtual void			bind_constant_buffer(u32 slot, rhi::rhi_buffer_handle handle) override;
	virtual void			bind_texture(u32 slot, rhi::rhi_texture_handle handle) override;
	virtual void			bind_shader(rhi::shader_stage stage, rhi::rhi_shader_handle handle) override;
	
	virtual void			draw(u32 vertex_count, u32 start_vertex) override;
	virtual void			draw_indexed(u32 index_count, u32 start_index, u32 start_vertex) override;

	virtual void			set_viewport(float x, float y, float width, float height) override;
	virtual void			clear_render_target(rhi::format fmt, float r, float g, float b, float a) override;

	virtual bool            begin_frame() override;
	virtual bool            end_frame() override;
    void begin_render_pass(rhi::render_pass_desc const&) override;
    void end_render_pass() override;
    void begin_presentation_pass(bool preserve_contents = false) override;
    void transition_texture(rhi::rhi_texture_handle, rhi::resource_state) override;
    size_t pending_pass_objects() const { return 0; }
    size_t pipeline_count() const { return m_pipeline_map.size(); }
	virtual bool            present() override;
	virtual void			resize_swapchain(u32 width, u32 height) override;

	// --- Pipeline State (Phase 2) ---
	virtual void			set_topology(rhi::topology topo) override;
	virtual void			set_rasterizer_state(rhi::fill_mode fill, rhi::cull_mode cull) override;
    void set_depth_bias(float constant,float slope) override;
	virtual void			set_depth_stencil_state(bool depth_enable, bool depth_write, rhi::comparison_func func) override;
    void set_stencil_state(bool enable, u32 reference = 0, u32 read_mask = 255, u32 write_mask = 255,
        rhi::comparison_func compare = rhi::comparison_func::always, rhi::stencil_op pass = rhi::stencil_op::keep) override;
	virtual void			set_blend_state(bool enable, rhi::blend src, rhi::blend dst, rhi::blend_op op) override;
	virtual void			set_scissor(u32 x, u32 y, u32 width, u32 height) override;

    // Smoke acceptance checks use actual retained pool/arena capacity.
    std::array<size_t,4> material_capacity() const {
        return {m_frames->materials[0].pools.size(), m_frames->materials[0].uniforms.size(),
            m_frames->materials[1].pools.size(), m_frames->materials[1].uniforms.size()};
    }
    std::array<size_t,5> resource_counts() const {
        return {m_buffers.active_count(),m_shaders.active_count(),m_textures.active_count(),m_samplers.active_count(),m_retired_buffers.size()};
    }
    std::array<size_t,5> retired_resource_counts() const {
        return {m_retired_buffers.size(),m_retired_shaders.size(),m_retired_pipelines.size(),m_retired_textures.size(),m_retired_samplers.size()};
    }

private:
    VkResult allocate_memory(VkMemoryAllocateInfo const&, VkDeviceMemory&);
    void free_memory(VkDeviceMemory) noexcept;
    void free_buffer(vulkan_buffer_entry const&) noexcept;
    void free_texture(vulkan_texture_entry const&) noexcept;
    std::map<VkDeviceMemory,VkDeviceSize> m_memory_allocations;
    device_memory_statistics m_memory_statistics;
    bool m_performance_enabled=false;
    uint32_t m_timestamp_valid_bits=0;
    float m_timestamp_period=0;
    uint64_t m_frame_started_ns=0, m_pipeline_creations=0, m_pipeline_creation_ns=0, m_device_idle_calls=0;
    void read_frame_timestamp(u32 slot);
    void prepare_frame_timestamps();
    void begin_frame_timestamp(VkCommandBuffer);
    struct capture_slot {
        vulkan_buffer_entry buffer;
        bool coherent=false;
        u32 width=0,height=0;
        VkFormat format=VK_FORMAT_UNDEFINED;
    };
    void record_capture(VkCommandBuffer);
    void cleanup_capture() noexcept;
	// --- Vulkan Core Objects ---
	VkInstance		m_instance		= VK_NULL_HANDLE;
	VkDebugUtilsMessengerEXT m_debug_messenger = VK_NULL_HANDLE;
	bool m_surface_maintenance_enabled = false;
	bool m_present_fences_enabled = false;
	bool m_bc_textures_enabled = false;
    bool m_non_solid_enabled=false;
	bool m_shader_demote_enabled = false;
	bool m_bindless_enabled = false;
	bool m_synchronization2_enabled = false;
	bool m_dynamic_rendering_enabled = false;
	static constexpr u32 MAX_BINDLESS_2D = 1024;
	static constexpr u32 MAX_BINDLESS_CUBE = 128;
	VkPhysicalDevice	m_physical_device	= VK_NULL_HANDLE;
	VkDevice		m_device		= VK_NULL_HANDLE;
	VkQueue			m_graphics_queue	= VK_NULL_HANDLE;
	VkQueue			m_present_queue		= VK_NULL_HANDLE;
	uint32_t		m_graphics_queue_family_index = UINT32_MAX;
	uint32_t		m_present_queue_family_index  = UINT32_MAX;

	// --- Command Management ---
	bool			m_recording = false;
	bool            m_initialized = false;
	bool            m_frame_submitted = false;

    std::shared_ptr<presentation_output> m_output = std::make_shared<presentation_output>();

	// --- Per-Frame Synchronization ---

	// --- Resource Pools (Phase 2) ---
	handle_pool<vulkan_buffer_entry>		m_buffers;
	handle_pool<vulkan_texture_entry>	m_textures;
    handle_pool<vulkan_sampler_entry> m_samplers;
	handle_pool<vulkan_shader_entry>		m_shaders;

	// --- Pipeline State Descriptor (Phase 2) ---
	// Captures all state needed to create/lookup a VkPipeline.
	using pipeline_state_desc = pipeline_key;

	pipeline_state_desc						m_current_state;
	VkPipelineLayout						m_pipeline_layout	= VK_NULL_HANDLE;
	VkPipelineCache							m_pipeline_cache	= VK_NULL_HANDLE;
	::std::unordered_map<pipeline_key, VkPipeline, pipeline_key_hash> m_pipeline_map;
	struct retired_buffer { u64 serial; vulkan_buffer_entry resource; };
	struct retired_shader { u64 serial; VkShaderModule module; };
	struct retired_pipeline { u64 serial; VkPipeline pipeline; };
	::std::list<retired_buffer> m_retired_buffers;
	::std::list<retired_shader> m_retired_shaders;
	::std::list<retired_pipeline> m_retired_pipelines;
    struct retired_texture { u64 serial; vulkan_texture_entry resource; u32 bindless_slot = 0; bool bindless_cube = false; };
    struct retired_sampler { u64 serial; VkSampler sampler; };
    std::list<retired_texture> m_retired_textures;
    std::list<retired_sampler> m_retired_samplers;
    u64 m_pass_pair_creations=0;
    bool m_pass_active = false, m_presentation_pass = false, m_pass_has_stencil = false;
    VkExtent2D m_pass_extent = {};
    rhi::render_pass_desc m_pass_description;
    std::vector<u32> m_pass_color_formats;
    u32 m_pass_depth_format = 0;
    u32 m_pass_samples = 1;
    bool m_sample_shading_enabled = false;
    bool m_presentation_contents = false;
    void set_pass_viewport();
    vulkan_texture_entry allocate_attachment(rhi::texture_desc const&);
	u64 m_submitted_serial = 0, m_completed_serial = 0;
	u64 retirement_serial() const;
	void collect_retired(bool force = false) noexcept;

	// --- Binding State (Phase 2) ---
	rhi::rhi_buffer_handle	m_bound_vb		= RHI_INVALID_HANDLE;
	u32				m_bound_vb_offset	= 0;
	rhi::rhi_buffer_handle	m_bound_ib		= RHI_INVALID_HANDLE;
	u32				m_bound_ib_offset	= 0;
	rhi::index_format m_index_format = rhi::index_format::uint16;

    struct uniform_chunk { vulkan_buffer_entry resource; VkDeviceSize cursor = 0; bool coherent = false; };
    struct material_frame {
        std::vector<VkDescriptorPool> pools;
        std::vector<std::array<VkDescriptorSet,3>> descriptors;
        std::vector<VkDescriptorSet> compute_descriptors;
        std::vector<uniform_chunk> uniforms;
        size_t pool_cursor = 0, descriptor_cursor = 0, compute_descriptor_cursor = 0, uniform_cursor = 0;
    };
    struct output_frames {
        VkCommandPool command_pool=VK_NULL_HANDLE;
        VkCommandBuffer command_buffers[MAX_FRAMES_IN_FLIGHT]={};
        VkSemaphore image_available[MAX_FRAMES_IN_FLIGHT]={};
        VkFence in_flight[MAX_FRAMES_IN_FLIGHT]={};
        u32 current_frame=0;
        u64 serials[MAX_FRAMES_IN_FLIGHT]={};
        material_frame materials[MAX_FRAMES_IN_FLIGHT];
        std::array<capture_slot,MAX_FRAMES_IN_FLIGHT> captures;
        bool capture_enabled=false,capture_valid=false;
        u32 capture_frame=0;
        VkQueryPool timestamps=VK_NULL_HANDLE;
        bool timestamp_pending[MAX_FRAMES_IN_FLIGHT]={};
        output_performance_statistics performance;
    };
    struct owned_output {
        std::shared_ptr<presentation_output> presentation;
        std::unique_ptr<output_frames> frames;
    };
    std::vector<owned_output> m_outputs;
    output_frames m_idle_frames;
    output_frames* m_frames=&m_idle_frames;
    DWORD m_owner_thread=0;
    void require_output_owner() const;
    void destroy_output_objects() noexcept;
    void create_command_objects();
    std::shared_ptr<presentation_output> create_output_for(HWND,std::shared_ptr<presentation_output>);
    VkDescriptorSetLayout m_descriptor_layouts[4] = {};
    VkDescriptorPool m_bindless_pool = VK_NULL_HANDLE;
    VkDescriptorSet m_bindless_set = VK_NULL_HANDLE;
    rhi::rhi_texture_handle m_fallback_cubemap = 0;
    std::unordered_map<rhi::rhi_texture_handle, u32> m_bindless_2d_slots;
    std::unordered_map<rhi::rhi_texture_handle, u32> m_bindless_cube_slots;
    std::vector<u32> m_free_bindless_2d;
    std::vector<u32> m_free_bindless_cube;
    u32 m_next_bindless_2d = 1, m_next_bindless_cube = 1;
    void write_bindless_descriptor(u32 binding, u32 slot, vulkan_texture_entry const& texture);
    VkDescriptorSetLayout m_compute_descriptor_layout = VK_NULL_HANDLE;
    VkPipelineLayout m_compute_pipeline_layout = VK_NULL_HANDLE;
    std::unordered_map<rhi::rhi_shader_handle, VkPipeline> m_compute_pipelines;
    VkPipeline get_or_create_compute_pipeline(rhi::rhi_shader_handle);
    rhi::rhi_buffer_handle m_bound_constants[2] = {};
    std::array<rhi::rhi_texture_handle,8> m_bound_textures = {};
    rhi::rhi_texture_handle m_fallback_texture = 0;
    rhi::rhi_sampler_handle m_bound_sampler = 0, m_fallback_sampler = 0;
    rhi::rhi_sampler_handle m_bound_comparison_sampler = 0;
    float m_depth_bias_constant=0,m_depth_bias_slope=0;
    void bind_depth_bias();
    VkDeviceSize m_uniform_alignment = 1;
    void initialize_material_resources();
    void reset_material_frame();
    void cleanup_material_frames() noexcept;
    void bind_material_descriptors();
    vulkan_buffer_entry allocate_host_buffer(VkDeviceSize, VkBufferUsageFlags, bool& coherent);
    vulkan_texture_entry upload_texture(rhi::texture_desc const&, std::vector<rhi::texture_subresource_data> const&);
    VkDescriptorBufferInfo uniform_slice(void const*, size_t);

	// --- Internal Helpers ---
	void	create_instance		();
	void	pick_physical_device	();
	void	find_queue_families	();
	void	create_logical_device	();
	void	create_window_surface	();
	void	create_swapchain	();
	void	create_depth_resource	();
	void	create_sync_objects	();
	void	cleanup_swapchain	();
	bool refresh_swapchain();
	void wait_for_presentations();

	// --- Pipeline Helpers (Phase 2) ---
	void		create_pipeline_layout	();
	VkPipeline	get_or_create_pipeline	(pipeline_state_desc const& desc);
	void		destroy_all_resources	();

	// --- Swapchain Query Helpers ---
	VkSurfaceFormatKHR	choose_surface_format	(const ::std::vector<VkSurfaceFormatKHR>& formats);
	VkPresentModeKHR	choose_present_mode	(const ::std::vector<VkPresentModeKHR>& modes);
	VkExtent2D		choose_swap_extent	(const VkSurfaceCapabilitiesKHR& caps);

	// --- Memory Helpers ---
	VkFormat	find_depth_format	();
	uint32_t	find_memory_type	(uint32_t type_filter, VkMemoryPropertyFlags properties);
};

} // namespace vulkan
} // namespace render
} // namespace xray

#endif // #ifndef XRAY_RENDER_VULKAN_BACKEND_H_INCLUDED
