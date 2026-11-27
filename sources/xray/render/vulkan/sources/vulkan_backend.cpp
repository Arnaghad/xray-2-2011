////////////////////////////////////////////////////////////////////////////
//	Vulkan Backend Implementation
//	Checkpoint 3: Render Pass, Framebuffers, Synchronization, Depth
////////////////////////////////////////////////////////////////////////////

#include <vector>
#include <list>
#include <map>
#include <set>
#include <deque>
#include <functional>
#include <array>
#include <stdexcept>
#include <algorithm>
#include <limits>

#include "vulkan_backend.h"
#include "vulkan_diagnostics.h"
#include "vulkan_creation.h"
#include "vulkan_performance.h"
#include "vulkan_api_version.h"
#include "vertex_layout.h"
#include <xray/render/vulkan/platform_surface.h>
#include <xray/threading_functions.h>
#include <cstring>
#include <cstdlib>

namespace xray {
namespace render {
namespace vulkan {
u32 vulkan_backend::raster_subpixel_bits() const {
    if (!m_physical_device) throw std::runtime_error("Raster limits require an initialized physical device");
    VkPhysicalDeviceProperties properties={}; vkGetPhysicalDeviceProperties(m_physical_device,&properties);
    return properties.limits.subPixelPrecisionBits;
}

namespace {
VkFormat vertex_format(rhi::format type) {
    using rhi::format;
    switch (type) {
        case format::r32_float: return VK_FORMAT_R32_SFLOAT;
        case format::r32g32_float: return VK_FORMAT_R32G32_SFLOAT;
        case format::r32g32b32_float: return VK_FORMAT_R32G32B32_SFLOAT;
        case format::r32g32b32a32_float: return VK_FORMAT_R32G32B32A32_SFLOAT;
        case format::r8g8b8a8_unorm: return VK_FORMAT_R8G8B8A8_UNORM;
        default: throw ::std::runtime_error("Unsupported vertex format");
    }
}
void vk_check(VkResult result, char const* operation) {
	if (result != VK_SUCCESS) {
		diagnostic("error", "%s (VkResult=%d)", operation, static_cast<int>(result));
		throw ::std::runtime_error(::std::string(operation) + " (VkResult=" + ::std::to_string(result) + ")");
	}
}
VKAPI_ATTR VkBool32 VKAPI_CALL validation_callback(
	VkDebugUtilsMessageSeverityFlagBitsEXT severity, VkDebugUtilsMessageTypeFlagsEXT,
	VkDebugUtilsMessengerCallbackDataEXT const* data, void*) {
	record_validation((severity & VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT) != 0, data->pMessage);
	return VK_FALSE;
}
bool has_extension(::std::vector<VkExtensionProperties> const& extensions, char const* name) {
	return ::std::any_of(extensions.begin(), extensions.end(), [name](auto const& e) {
		return ::std::strcmp(e.extensionName, name) == 0;
	});
}
}

// -----------------------------------------------------------------------
// Construction / Destruction
// -----------------------------------------------------------------------

vulkan_backend::vulkan_backend() {}
vulkan_backend::~vulkan_backend() { shutdown(); }

void vulkan_backend::require_output_owner() const {
    if (xray::threading::current_thread_id()!=m_owner_thread)
        throw std::runtime_error("Vulkan output operation requires the renderer owner thread");
}

void vulkan_backend::select_output(std::shared_ptr<presentation_output> const& output) {
    require_output_owner();
    if (!m_initialized || m_recording || m_frame_submitted)
        throw std::runtime_error("Output selection requires an initialized backend between frames");
    auto entry=std::find_if(m_outputs.begin(),m_outputs.end(),[&](auto const& value) { return value.presentation==output; });
    if (entry==m_outputs.end()) throw std::runtime_error("Closed or foreign Vulkan output");
    m_output=entry->presentation; m_frames=entry->frames.get();
}

std::shared_ptr<presentation_output> vulkan_backend::create_output(platform::native_surface_handle window) {
    return create_output_for(window,std::make_shared<presentation_output>());
}
std::vector<std::shared_ptr<presentation_output>> vulkan_backend::outputs() const {
    require_output_owner();
    std::vector<std::shared_ptr<presentation_output>> result;
    for (auto const& entry:m_outputs) result.push_back(entry.presentation);
    return result;
}
void vulkan_backend::adopt_output(std::shared_ptr<presentation_output> const& output) {
    require_output_owner();
    if (!output || output->closed || output->close_requested) throw std::runtime_error("Cannot adopt a closed output lease");
    for (auto const& entry:m_outputs) if (entry.presentation==output) return;
    if (output->surface || output->swapchain) throw std::runtime_error("Output lease already has foreign GPU objects");
    create_output_for(output->surface_handle,output);
}
std::shared_ptr<presentation_output> vulkan_backend::create_output_for(platform::native_surface_handle window,std::shared_ptr<presentation_output> next) {
    require_output_owner();
    if (!m_initialized || m_recording || m_frame_submitted || !surface_adapter::is_surface_alive(window))
        throw std::runtime_error("Output creation requires a live surface between frames");
    for (auto const& entry:m_outputs) if (entry.presentation->surface_handle==window)
        throw std::runtime_error("Surface already has a Vulkan output");
    auto previous=m_output;
    next->surface_handle=window;
    m_outputs.push_back({next,std::make_unique<output_frames>()});
    m_output=next; m_frames=m_outputs.back().frames.get();
    try {
        m_frames->capture_enabled=surface_adapter::get_surface_property(window,"XRAY_VULKAN_TEST_CAPTURE")!=0;
        create_window_surface();
        if (!next->is_offscreen) {
            VkBool32 supported=VK_FALSE;
            vk_check(vkGetPhysicalDeviceSurfaceSupportKHR(m_physical_device,m_present_queue_family_index,next->surface,&supported),"Query additional output present support");
            if (!supported) throw std::runtime_error("Shared device cannot present to the additional surface");
        }
        create_command_objects(); create_sync_objects();
        next->swapchain_dirty=true; refresh_swapchain();
    } catch (...) {
        destroy_output_objects(); m_outputs.pop_back();
        m_output=previous; m_frames=&m_idle_frames;
        for (auto const& entry:m_outputs) if (entry.presentation==previous) m_frames=entry.frames.get();
        throw;
    }
    if (m_outputs.size()>1) select_output(previous);
    diagnostic("info","Vulkan output created (shared device, outputs=%zu)",m_outputs.size());
    return next;
}

void vulkan_backend::close_output(std::shared_ptr<presentation_output> const& output) {
    require_output_owner();
    if (!m_initialized || m_recording) throw std::runtime_error("Output close requires a finished command recording");
    auto entry=std::find_if(m_outputs.begin(),m_outputs.end(),[&](auto const& value) { return value.presentation==output; });
    if (entry==m_outputs.end()) throw std::runtime_error("Closed or foreign Vulkan output");
    // A submitted frame owns an acquired image and its semaphore until present.
    if (m_frame_submitted && !present()) throw std::runtime_error("Cannot retire pending output presentation");
    auto previous=m_output;
    m_output=entry->presentation; m_frames=entry->frames.get();
    for (u32 i=0;i<MAX_FRAMES_IN_FLIGHT;++i) {
        vk_check(vkWaitForFences(m_device,1,&m_frames->in_flight[i],VK_TRUE,UINT64_MAX),"Wait closing output frame");
        read_frame_timestamp(i);
        m_completed_serial=std::max(m_completed_serial,m_frames->serials[i]);
    }
    wait_for_presentations();
    destroy_output_objects(); m_outputs.erase(entry);
    m_frames=&m_idle_frames;
    if (!m_outputs.empty()) select_output(previous==output ? m_outputs.front().presentation : previous);
    collect_retired();
    diagnostic("info","Vulkan output closed (remaining=%zu)",m_outputs.size());
}

// Called after this output's submissions have retired (or device idle on shutdown).
void vulkan_backend::destroy_output_objects() noexcept {
    if (m_device) {
        for (size_t i=0;i<m_output->present_pending.size();++i)
            if (m_output->present_pending[i]) vkWaitForFences(m_device,1,&m_output->present_fences[i],VK_TRUE,UINT64_MAX);
        cleanup_capture(); cleanup_material_frames(); cleanup_swapchain();
        for (u32 i=0;i<MAX_FRAMES_IN_FLIGHT;++i) {
            if (m_frames->image_available[i]) vkDestroySemaphore(m_device,m_frames->image_available[i],allocation_callbacks());
            if (m_frames->in_flight[i]) vkDestroyFence(m_device,m_frames->in_flight[i],allocation_callbacks());
        }
        if (m_frames->command_pool) vkDestroyCommandPool(m_device,m_frames->command_pool,allocation_callbacks());
        if (m_frames->timestamps) vkDestroyQueryPool(m_device,m_frames->timestamps,allocation_callbacks());
    }
    *m_frames=output_frames{};
    if (m_output->surface) vkDestroySurfaceKHR(m_instance,m_output->surface,allocation_callbacks());
    m_output->surface=VK_NULL_HANDLE; m_output->surface_handle={};
    m_output->is_offscreen=false;
    m_output->closed=true;
    m_output->swapchain_dirty=false;
}

// -----------------------------------------------------------------------
// Initialization
// -----------------------------------------------------------------------

bool vulkan_backend::initialize(platform::native_surface_handle surface) {
	if (!g_allocator || m_instance != VK_NULL_HANDLE || !surface.valid() || !surface_adapter::is_surface_alive(surface)) return false;
	reset_diagnostics();
    m_memory_statistics={};
    m_performance_enabled=false; m_timestamp_valid_bits=0; m_timestamp_period=0;
    m_frame_started_ns=m_pipeline_creations=m_pipeline_creation_ns=m_device_idle_calls=m_pass_pair_creations=0;
    m_owner_thread=xray::threading::current_thread_id();
    try {
    // A previous session's retained lease must stay closed after reinitialization.
    m_output=std::make_shared<presentation_output>();
    m_outputs.push_back({m_output,std::make_unique<output_frames>()});
    m_frames=m_outputs.back().frames.get();
	m_output->surface_handle = surface;
    m_output->presented_frames=0;
    m_frames->capture_enabled=surface_adapter::get_surface_property(surface,"XRAY_VULKAN_TEST_CAPTURE")!=0;
		create_instance();
		create_window_surface();
		pick_physical_device();
		find_queue_families();
		create_logical_device();
		create_sync_objects();
		m_output->swapchain_dirty = true;
		refresh_swapchain();
		m_initialized = true;
        initialize_material_resources();
        if (auto mode=std::getenv("XRAY_VULKAN_PERFORMANCE"); mode && !std::strcmp(mode,"1")) enable_performance_timestamps();
		diagnostic("info", "Vulkan backend initialized (two frame slots)");
		return true;
	} catch (::std::exception const& e) {
		diagnostic("error", "Vulkan backend failed to initialize: %s", e.what());
		shutdown();
		return false;
	} catch (...) {
		diagnostic("error", "Vulkan backend failed to initialize (unknown error)");
		shutdown();
		return false;
	}
}

// -----------------------------------------------------------------------
// Shutdown
// -----------------------------------------------------------------------

void vulkan_backend::shutdown() noexcept {
    bool const was_profiled=m_performance_enabled;
	m_initialized = false;
	m_recording = false;
	m_frame_submitted = false;
    m_pass_active = m_presentation_pass = m_pass_has_stencil = false;
    m_presentation_contents = false;
    m_pass_description = {}; m_pass_color_formats.clear(); m_pass_depth_format = 0;
	if (m_device != VK_NULL_HANDLE) {
		VkResult const idle = vkDeviceWaitIdle(m_device);
        ++m_device_idle_calls;
        if (idle != VK_SUCCESS) diagnostic("error", "Shutdown device wait (VkResult=%d)", int(idle));
        collect_retired(true);
        for (auto const& entry:m_outputs) {
            m_output=entry.presentation; m_frames=entry.frames.get();
            destroy_output_objects();
        }

		destroy_all_resources();
		for (auto const& pipeline : m_pipeline_map)
			vkDestroyPipeline(m_device, pipeline.second, allocation_callbacks());
		m_pipeline_map.clear();
		for (auto const& pipeline : m_compute_pipelines)
			vkDestroyPipeline(m_device, pipeline.second, allocation_callbacks());
		m_compute_pipelines.clear();
		if (m_compute_pipeline_layout != VK_NULL_HANDLE)
			vkDestroyPipelineLayout(m_device, m_compute_pipeline_layout, allocation_callbacks());
		m_compute_pipeline_layout = VK_NULL_HANDLE;
		if (m_compute_descriptor_layout != VK_NULL_HANDLE)
			vkDestroyDescriptorSetLayout(m_device, m_compute_descriptor_layout, allocation_callbacks());
		m_compute_descriptor_layout = VK_NULL_HANDLE;
		if (m_bindless_pool != VK_NULL_HANDLE)
			vkDestroyDescriptorPool(m_device, m_bindless_pool, allocation_callbacks());
		m_bindless_pool = VK_NULL_HANDLE;
		m_bindless_set = VK_NULL_HANDLE;
		m_bindless_enabled = false;
		if (m_pipeline_layout != VK_NULL_HANDLE)
			vkDestroyPipelineLayout(m_device, m_pipeline_layout, allocation_callbacks());
		m_pipeline_layout = VK_NULL_HANDLE;
        for (auto& layout : m_descriptor_layouts) {
            if (layout) vkDestroyDescriptorSetLayout(m_device, layout, allocation_callbacks());
            layout = VK_NULL_HANDLE;
        }
		if (m_pipeline_cache != VK_NULL_HANDLE)
			vkDestroyPipelineCache(m_device, m_pipeline_cache, allocation_callbacks());
		m_pipeline_cache = VK_NULL_HANDLE;

		vkDestroyDevice(m_device, allocation_callbacks());
		m_device = VK_NULL_HANDLE;
	}
	m_physical_device = VK_NULL_HANDLE;
    // Also handles failure before device creation.
    for (auto const& entry:m_outputs) {
        auto& output=*entry.presentation;
        if (output.surface) vkDestroySurfaceKHR(m_instance,output.surface,allocation_callbacks());
        output.surface=VK_NULL_HANDLE; output.surface_handle={};
        output.closed=true; output.swapchain_dirty=false;
    }
    m_outputs.clear(); m_frames=&m_idle_frames;
    m_performance_enabled=false;

	if (m_instance != VK_NULL_HANDLE) {
		if (m_debug_messenger != VK_NULL_HANDLE) {
			auto destroy = reinterpret_cast<PFN_vkDestroyDebugUtilsMessengerEXT>(vkGetInstanceProcAddr(m_instance, "vkDestroyDebugUtilsMessengerEXT"));
			if (destroy) destroy(m_instance, m_debug_messenger, allocation_callbacks());
			m_debug_messenger = VK_NULL_HANDLE;
		}
		vkDestroyInstance(m_instance, allocation_callbacks());
		m_instance = VK_NULL_HANDLE;
	}
	m_output->surface_handle = {};
	m_graphics_queue = VK_NULL_HANDLE;
	m_present_queue = VK_NULL_HANDLE;
	m_graphics_queue_family_index = UINT32_MAX;
	m_present_queue_family_index = UINT32_MAX;
	m_frames->current_frame = 0;
	m_output->current_image_index = 0;
	m_current_state = {};
	m_bound_vb = RHI_INVALID_HANDLE;
	m_bound_ib = RHI_INVALID_HANDLE;
    m_bound_constants[0] = m_bound_constants[1] = 0;
    m_bound_textures = {}; m_bound_sampler = m_fallback_texture = m_fallback_sampler = 0;
    m_bound_comparison_sampler=0; m_depth_bias_constant=m_depth_bias_slope=0;
	m_output->swapchain_dirty = false;
	m_surface_maintenance_enabled = false;
	m_present_fences_enabled = false;
	m_submitted_serial = m_completed_serial = 0;
	for (auto& serial : m_frames->serials) serial = 0;
    m_owner_thread=0;
    if (was_profiled) {
        auto host=host_memory_usage();
        diagnostic("info","M9 performance teardown: device_bytes=%llu allocations=%llu frees=%llu ledger=%zu host_bytes=%llu",
            static_cast<unsigned long long>(m_memory_statistics.live_bytes),
            static_cast<unsigned long long>(m_memory_statistics.allocations),
            static_cast<unsigned long long>(m_memory_statistics.frees),m_memory_allocations.size(),
            static_cast<unsigned long long>(host.live_bytes));
    }
}

void vulkan_backend::destroy_all_resources() {
	m_buffers.for_each_active([this](vulkan_buffer_entry& entry) {
		if (entry.mapped) vkUnmapMemory(m_device, entry.memory);
		if (entry.buffer) vkDestroyBuffer(m_device, entry.buffer, allocation_callbacks());
		free_memory(entry.memory);
	});
	m_textures.for_each_active([this](vulkan_texture_entry& entry) {
		if (entry.sample_view) vkDestroyImageView(m_device, entry.sample_view, allocation_callbacks());
		if (entry.view) vkDestroyImageView(m_device, entry.view, allocation_callbacks());
		if (entry.image) vkDestroyImage(m_device, entry.image, allocation_callbacks());
		free_memory(entry.memory);
	});
	m_shaders.for_each_active([this](vulkan_shader_entry& entry) {
		if (entry.module) vkDestroyShaderModule(m_device, entry.module, allocation_callbacks());
	});
    m_samplers.for_each_active([this](vulkan_sampler_entry& entry) {
        if (entry.sampler) vkDestroySampler(m_device, entry.sampler, allocation_callbacks());
    });
    m_samplers.clear();
	m_buffers.clear();
	m_textures.clear();
	m_shaders.clear();
	m_bindless_2d_slots.clear();
	m_bindless_cube_slots.clear();
	m_free_bindless_2d.clear();
	m_free_bindless_cube.clear();
	m_next_bindless_2d = 1;
	m_next_bindless_cube = 1;
	m_fallback_texture = 0;
	m_fallback_cubemap = 0;
}

// -----------------------------------------------------------------------
// Instance
// -----------------------------------------------------------------------

void vulkan_backend::create_instance() {
	uint32_t loader_version = VK_API_VERSION_1_0;
	auto enumerate_version = reinterpret_cast<PFN_vkEnumerateInstanceVersion>(vkGetInstanceProcAddr(nullptr, "vkEnumerateInstanceVersion"));
	if (enumerate_version) vk_check(enumerate_version(&loader_version), "Enumerate instance version");
	if (!supports_required_api(loader_version)) throw ::std::runtime_error("Vulkan 1.4 loader required");
	uint32_t count = 0;
	vk_check(vkEnumerateInstanceExtensionProperties(nullptr, &count, nullptr), "Enumerate instance extensions");
	::std::vector<VkExtensionProperties> extensions(count);
	vk_check(vkEnumerateInstanceExtensionProperties(nullptr, &count, extensions.data()), "Read instance extensions");
	::std::vector<char const*> enabled_extensions = surface_adapter::required_instance_extensions();
	if (enabled_extensions.empty()) enabled_extensions.push_back(VK_KHR_SURFACE_EXTENSION_NAME);
	for (auto name : enabled_extensions)
		if (!has_extension(extensions, name)) throw ::std::runtime_error(::std::string("Missing instance extension: ") + name);
	m_surface_maintenance_enabled = has_extension(extensions, VK_EXT_SURFACE_MAINTENANCE_1_EXTENSION_NAME)
		&& has_extension(extensions, VK_KHR_GET_SURFACE_CAPABILITIES_2_EXTENSION_NAME);
	if (m_surface_maintenance_enabled) {
		enabled_extensions.push_back(VK_KHR_GET_SURFACE_CAPABILITIES_2_EXTENSION_NAME);
		enabled_extensions.push_back(VK_EXT_SURFACE_MAINTENANCE_1_EXTENSION_NAME);
	}
	bool validation_requested = false;
#ifndef NDEBUG
	validation_requested = true;
#endif
	bool const require_validation = ::std::getenv("XRAY_VULKAN_REQUIRE_VALIDATION") != nullptr;
	validation_requested = validation_requested || require_validation;
	vk_check(vkEnumerateInstanceLayerProperties(&count, nullptr), "Enumerate instance layers");
	::std::vector<VkLayerProperties> layers(count);
	vk_check(vkEnumerateInstanceLayerProperties(&count, layers.data()), "Read instance layers");
	bool const layer_available = ::std::any_of(layers.begin(), layers.end(), [](auto const& layer) {
		return ::std::strcmp(layer.layerName, "VK_LAYER_KHRONOS_validation") == 0;
	});
	bool const validation = validation_requested && layer_available && has_extension(extensions, VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
	if (validation_requested && !validation) {
		if (require_validation) throw ::std::runtime_error("Requested validation layer/debug utils are unavailable");
		diagnostic("warning", "VK_LAYER_KHRONOS_validation or VK_EXT_debug_utils unavailable; validation is NOT active");
	}
	if (validation) enabled_extensions.push_back(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
	char const* validation_layer = "VK_LAYER_KHRONOS_validation";
	VkDebugUtilsMessengerCreateInfoEXT debug_info = {};
	debug_info.sType = VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT;
	debug_info.messageSeverity = VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
	debug_info.messageType = VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;
	debug_info.pfnUserCallback = &validation_callback;
	VkValidationFeatureEnableEXT validation_feature = VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT;
	VkValidationFeaturesEXT validation_features = {};
	validation_features.sType = VK_STRUCTURE_TYPE_VALIDATION_FEATURES_EXT;
	validation_features.enabledValidationFeatureCount = 1;
	validation_features.pEnabledValidationFeatures = &validation_feature;
	// This layer feature is enabled only when its extension is advertised by the layer.
	if (validation) {
		uint32_t layer_count = 0;
		vk_check(vkEnumerateInstanceExtensionProperties(validation_layer, &layer_count, nullptr), "Enumerate validation extensions");
		::std::vector<VkExtensionProperties> layer_extensions(layer_count);
		vk_check(vkEnumerateInstanceExtensionProperties(validation_layer, &layer_count, layer_extensions.data()), "Read validation extensions");
		if (has_extension(layer_extensions, VK_EXT_VALIDATION_FEATURES_EXTENSION_NAME)) {
			enabled_extensions.push_back(VK_EXT_VALIDATION_FEATURES_EXTENSION_NAME);
			debug_info.pNext = &validation_features;
		}
	}
	VkApplicationInfo app_info = {};
	app_info.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
	app_info.pApplicationName = "X-Ray Engine 2.0";
	app_info.applicationVersion = VK_MAKE_VERSION(2, 0, 0);
	app_info.pEngineName = "X-Ray Engine";
	app_info.engineVersion = VK_MAKE_VERSION(2, 0, 0);
	app_info.apiVersion = required_api_version;

	VkInstanceCreateInfo create_info = {};
	create_info.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
	create_info.pApplicationInfo = &app_info;
	create_info.enabledExtensionCount = static_cast<uint32_t>(enabled_extensions.size());
	create_info.ppEnabledExtensionNames = enabled_extensions.data();
	create_info.enabledLayerCount = validation ? 1 : 0;
	create_info.ppEnabledLayerNames = validation ? &validation_layer : nullptr;
	create_info.pNext = validation ? &debug_info : nullptr;

    // Runtime-error output values are undefined; publish only a successful handle.
    VkInstance candidate = VK_NULL_HANDLE;
    VkResult result = vkCreateInstance(&create_info, allocation_callbacks(), &candidate);
    vk_check(result, "Create Vulkan instance");
    m_instance = candidate;
    record_validation_enabled(validation);
    record_instance(loader_version,app_info.apiVersion);
	if (validation) {
		debug_info.pNext = nullptr;
		auto create = reinterpret_cast<PFN_vkCreateDebugUtilsMessengerEXT>(vkGetInstanceProcAddr(m_instance, "vkCreateDebugUtilsMessengerEXT"));
		if (!create) throw ::std::runtime_error("Debug messenger entry point unavailable");
        vk_check(create_handle(m_debug_messenger, create, m_instance, &debug_info, allocation_callbacks()), "Create debug messenger");
	}
	diagnostic("info", "Validation: %s", validation ? "enabled (synchronization checks requested)" : "disabled");
	diagnostic("info", "Vulkan instance created (API %u.%u; loader %u.%u.%u; headers %u.%u.%u)",
		VK_API_VERSION_MAJOR(app_info.apiVersion),VK_API_VERSION_MINOR(app_info.apiVersion),
		VK_API_VERSION_MAJOR(loader_version),VK_API_VERSION_MINOR(loader_version),VK_API_VERSION_PATCH(loader_version),
		VK_API_VERSION_MAJOR(VK_HEADER_VERSION_COMPLETE),VK_API_VERSION_MINOR(VK_HEADER_VERSION_COMPLETE),VK_API_VERSION_PATCH(VK_HEADER_VERSION_COMPLETE));
}

// -----------------------------------------------------------------------
// Window Surface (SDL3 + Native WSI via surface_adapter)
// -----------------------------------------------------------------------

void vulkan_backend::create_window_surface() {
	if (!m_output->surface_handle.valid()) {
		diagnostic("warning", "No surface handle provided — headless mode, skipping surface creation");
		return;
	}
	if (m_output->surface_handle.kind == platform::native_surface_kind::offscreen) {
		m_output->is_offscreen = true;
		m_output->surface = VK_NULL_HANDLE;
		diagnostic("info", "Vulkan surface created (offscreen mailbox)");
		return;
	}

	bool used_sdl = false;
	if (!surface_adapter::create_surface(m_instance, m_output->surface_handle, allocation_callbacks(), &m_output->surface, &used_sdl))
		throw ::std::runtime_error("Failed to create Vulkan surface");
	diagnostic("info", "Vulkan surface created (%s)", used_sdl ? "SDL3" : "native WSI");
}

// -----------------------------------------------------------------------
// Physical Device Selection
// -----------------------------------------------------------------------

void vulkan_backend::pick_physical_device() {
 uint32_t device_count = 0;
 vk_check(vkEnumeratePhysicalDevices(m_instance, &device_count, nullptr), "Enumerate physical devices");
 ::std::vector<VkPhysicalDevice> devices(device_count);
 vk_check(vkEnumeratePhysicalDevices(m_instance, &device_count, devices.data()), "Read physical devices");
 int best_score = -1;
 for (auto device : devices) {
  VkPhysicalDeviceProperties props;
  vkGetPhysicalDeviceProperties(device, &props);
  if (!supports_required_api(props.apiVersion)) continue;
  uint32_t count = 0;
  vk_check(vkEnumerateDeviceExtensionProperties(device, nullptr, &count, nullptr), "Enumerate device extensions");
  ::std::vector<VkExtensionProperties> extensions(count);
  vk_check(vkEnumerateDeviceExtensionProperties(device, nullptr, &count, extensions.data()), "Read device extensions");
  if (!m_output->is_offscreen) {
   if (!has_extension(extensions, VK_KHR_SWAPCHAIN_EXTENSION_NAME)) continue;
   if (!m_surface_maintenance_enabled || !has_extension(extensions, VK_EXT_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME)) continue;
   VkPhysicalDeviceSwapchainMaintenance1FeaturesEXT maintenance = {};
   maintenance.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SWAPCHAIN_MAINTENANCE_1_FEATURES_EXT;
   VkPhysicalDeviceFeatures2 features = {};
   features.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;
   features.pNext = &maintenance;
   vkGetPhysicalDeviceFeatures2(device, &features);
   if (!maintenance.swapchainMaintenance1) continue;
  }
  vkGetPhysicalDeviceQueueFamilyProperties(device, &count, nullptr);
  ::std::vector<VkQueueFamilyProperties> families(count);
  vkGetPhysicalDeviceQueueFamilyProperties(device, &count, families.data());
  uint32_t graphics = UINT32_MAX, present = UINT32_MAX;
  for (uint32_t i = 0; i < count; ++i) {
   if (!families[i].queueCount) continue;
   bool const renders = (families[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) != 0;
   if (m_output->is_offscreen) {
    if (renders) { graphics = present = i; break; }
    continue;
   }
   VkBool32 supported = VK_FALSE;
   vk_check(vkGetPhysicalDeviceSurfaceSupportKHR(device, i, m_output->surface, &supported), "Query present support");
   if (renders && graphics == UINT32_MAX) graphics = i;
   if (supported && present == UINT32_MAX) present = i;
   if (renders && supported) { graphics = present = i; break; }
  }
  if (graphics == UINT32_MAX || present == UINT32_MAX) continue;
  if (!m_output->is_offscreen) {
   uint32_t formats = 0, modes = 0;
   vk_check(vkGetPhysicalDeviceSurfaceFormatsKHR(device, m_output->surface, &formats, nullptr), "Query surface formats");
   vk_check(vkGetPhysicalDeviceSurfacePresentModesKHR(device, m_output->surface, &modes, nullptr), "Query present modes");
   if (!formats || !modes) continue;
   VkSurfaceCapabilitiesKHR caps;
   vk_check(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(device, m_output->surface, &caps), "Query surface capabilities");
   if (!(caps.supportedUsageFlags & VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT)) continue;
  }
  int score = props.deviceType == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU ? 1000 :
   props.deviceType == VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU ? 100 : 1;
  if (score <= best_score) continue;
  best_score = score;
  m_physical_device = device;
  m_graphics_queue_family_index = graphics;
  m_present_queue_family_index = present;
 }
 if (m_physical_device == VK_NULL_HANDLE)
  throw ::std::runtime_error("No suitable Vulkan 1.4 GPU with graphics/present, swapchain and EXT swapchain maintenance present fences");
 VkPhysicalDeviceProperties props;
 vkGetPhysicalDeviceProperties(m_physical_device, &props);
 record_device(props.deviceName, props.apiVersion, false);
 diagnostic("info", "GPU: %s; Vulkan %u.%u.%u", props.deviceName,
  VK_API_VERSION_MAJOR(props.apiVersion), VK_API_VERSION_MINOR(props.apiVersion), VK_API_VERSION_PATCH(props.apiVersion));
}

// -----------------------------------------------------------------------
// Queue Family Discovery
// -----------------------------------------------------------------------

void vulkan_backend::find_queue_families() {
 if (m_graphics_queue_family_index == UINT32_MAX || m_present_queue_family_index == UINT32_MAX)
  throw ::std::runtime_error("Suitable graphics/present queues were not selected");
}

// -----------------------------------------------------------------------
// Logical Device
// -----------------------------------------------------------------------

void vulkan_backend::create_logical_device() {
	float queue_priority = 1.0f;

	::std::vector<VkDeviceQueueCreateInfo> queue_create_infos;

	VkDeviceQueueCreateInfo gfx_queue_info = {};
	gfx_queue_info.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
	gfx_queue_info.queueFamilyIndex = m_graphics_queue_family_index;
	gfx_queue_info.queueCount = 1;
	gfx_queue_info.pQueuePriorities = &queue_priority;
	queue_create_infos.push_back(gfx_queue_info);

	if (m_present_queue_family_index != UINT32_MAX &&
		m_present_queue_family_index != m_graphics_queue_family_index) {
		VkDeviceQueueCreateInfo present_queue_info = {};
		present_queue_info.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
		present_queue_info.queueFamilyIndex = m_present_queue_family_index;
		present_queue_info.queueCount = 1;
		present_queue_info.pQueuePriorities = &queue_priority;
		queue_create_infos.push_back(present_queue_info);
	}

	::std::vector<char const*> device_extensions;
	uint32_t extension_count = 0;
	vk_check(vkEnumerateDeviceExtensionProperties(m_physical_device, nullptr, &extension_count, nullptr), "Enumerate selected device extensions");
	::std::vector<VkExtensionProperties> extensions(extension_count);
	vk_check(vkEnumerateDeviceExtensionProperties(m_physical_device, nullptr, &extension_count, extensions.data()), "Read selected device extensions");
	if (has_extension(extensions, VK_KHR_SWAPCHAIN_EXTENSION_NAME))
		device_extensions.push_back(VK_KHR_SWAPCHAIN_EXTENSION_NAME);
	VkPhysicalDeviceSwapchainMaintenance1FeaturesEXT maintenance = {};
	maintenance.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SWAPCHAIN_MAINTENANCE_1_FEATURES_EXT;
	if (m_surface_maintenance_enabled && has_extension(extensions, VK_EXT_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME)) {
		VkPhysicalDeviceFeatures2 features = {};
		features.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;
		features.pNext = &maintenance;
		vkGetPhysicalDeviceFeatures2(m_physical_device, &features);
		m_present_fences_enabled = maintenance.swapchainMaintenance1 == VK_TRUE;
		if (m_present_fences_enabled) device_extensions.push_back(VK_EXT_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME);
	}
	if (!m_output->is_offscreen && !m_present_fences_enabled)
		throw ::std::runtime_error("Present fences required for safe swapchain teardown in the Stage 1 bootstrap");

	VkPhysicalDeviceProperties props;
	vkGetPhysicalDeviceProperties(m_physical_device, &props);
	VkDeviceCreateInfo device_create_info = {};
	VkPhysicalDeviceVulkan12Features vk12_supported = {};
	vk12_supported.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES;
	VkPhysicalDeviceVulkan13Features vk13_supported = {};
	vk13_supported.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES;
	vk13_supported.pNext = &vk12_supported;
	VkPhysicalDeviceFeatures2 supported_features = {};
	supported_features.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;
	supported_features.pNext = &vk13_supported;
	vkGetPhysicalDeviceFeatures2(m_physical_device, &supported_features);
	VkPhysicalDeviceFeatures enabled_features = {};
	enabled_features.textureCompressionBC = supported_features.features.textureCompressionBC;
    enabled_features.fillModeNonSolid=supported_features.features.fillModeNonSolid;m_non_solid_enabled=enabled_features.fillModeNonSolid==VK_TRUE;
    enabled_features.sampleRateShading=supported_features.features.sampleRateShading;
    m_sample_shading_enabled=enabled_features.sampleRateShading==VK_TRUE;
	m_bc_textures_enabled = enabled_features.textureCompressionBC == VK_TRUE;
	m_shader_demote_enabled = vk13_supported.shaderDemoteToHelperInvocation == VK_TRUE;
	m_bindless_enabled = props.apiVersion >= VK_API_VERSION_1_2
		&& vk12_supported.descriptorIndexing == VK_TRUE
		&& vk12_supported.shaderSampledImageArrayNonUniformIndexing == VK_TRUE
		&& vk12_supported.descriptorBindingPartiallyBound == VK_TRUE
		&& vk12_supported.descriptorBindingSampledImageUpdateAfterBind == VK_TRUE;
	m_synchronization2_enabled = props.apiVersion >= VK_API_VERSION_1_3 && vk13_supported.synchronization2 == VK_TRUE;
	m_dynamic_rendering_enabled = props.apiVersion >= VK_API_VERSION_1_3 && vk13_supported.dynamicRendering == VK_TRUE;
	if (!m_synchronization2_enabled || !m_dynamic_rendering_enabled)
		throw ::std::runtime_error("Vulkan 1.3 synchronization2 and dynamicRendering required");
	VkPhysicalDeviceVulkan13Features vk13_enabled = {};
	vk13_enabled.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES;
	vk13_enabled.synchronization2 = VK_TRUE;
	vk13_enabled.dynamicRendering = VK_TRUE;
	vk13_enabled.shaderDemoteToHelperInvocation = m_shader_demote_enabled ? VK_TRUE : VK_FALSE;
	vk13_enabled.pNext = m_present_fences_enabled ? &maintenance : nullptr;
	VkPhysicalDeviceVulkan12Features vk12_enabled = {};
	vk12_enabled.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES;
	if (m_bindless_enabled) {
		vk12_enabled.descriptorIndexing = VK_TRUE;
		vk12_enabled.shaderSampledImageArrayNonUniformIndexing = VK_TRUE;
		vk12_enabled.descriptorBindingPartiallyBound = VK_TRUE;
		vk12_enabled.descriptorBindingSampledImageUpdateAfterBind = VK_TRUE;
		vk12_enabled.pNext = &vk13_enabled;
	}
	device_create_info.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
	device_create_info.pEnabledFeatures = &enabled_features;
	device_create_info.pQueueCreateInfos = queue_create_infos.data();
	device_create_info.queueCreateInfoCount = (uint32_t)queue_create_infos.size();
	device_create_info.enabledExtensionCount = static_cast<uint32_t>(device_extensions.size());
	device_create_info.ppEnabledExtensionNames = device_extensions.data();
	device_create_info.pNext = m_bindless_enabled ? static_cast<void*>(&vk12_enabled) : static_cast<void*>(&vk13_enabled);

    VkDevice candidate = VK_NULL_HANDLE;
    VkResult result = vkCreateDevice(m_physical_device, &device_create_info, allocation_callbacks(), &candidate);
    vk_check(result, "Create logical device");
    m_device = candidate;

	vkGetDeviceQueue(m_device, m_graphics_queue_family_index, 0, &m_graphics_queue);
	if (m_present_queue_family_index != UINT32_MAX) {
		vkGetDeviceQueue(m_device, m_present_queue_family_index, 0, &m_present_queue);
	}

    create_command_objects();

	diagnostic("info", "Vulkan logical device created (queue families: gfx=%u, present=%u)",
		m_graphics_queue_family_index, m_present_queue_family_index);

	create_pipeline_layout();
	record_device(props.deviceName, props.apiVersion, m_present_fences_enabled);
	diagnostic("info", "Present fences: %s, Vulkan 1.2 bindless: %s, Vulkan 1.3 sync2/dynamicRendering: enabled",
		m_present_fences_enabled ? "enabled" : "disabled",
		m_bindless_enabled ? "enabled" : "disabled");
}

void vulkan_backend::create_command_objects() {
	// --- Command Pool ---
	VkCommandPoolCreateInfo pool_info = {};
	pool_info.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
	pool_info.queueFamilyIndex = m_graphics_queue_family_index;
	pool_info.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;

    vk_check(create_handle(m_frames->command_pool, vkCreateCommandPool,
        m_device, &pool_info, allocation_callbacks()), "Create command pool");

	// --- Command Buffers (one per frame in flight) ---
	VkCommandBufferAllocateInfo alloc_info = {};
	alloc_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
	alloc_info.commandPool = m_frames->command_pool;
	alloc_info.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
	alloc_info.commandBufferCount = MAX_FRAMES_IN_FLIGHT;

    VkCommandBuffer commands[MAX_FRAMES_IN_FLIGHT]={};
    vk_check(vkAllocateCommandBuffers(m_device, &alloc_info, commands), "Allocate frame command buffers");
    std::copy(std::begin(commands),std::end(commands),m_frames->command_buffers);

}

// -----------------------------------------------------------------------
// Swapchain Helpers
// -----------------------------------------------------------------------

VkSurfaceFormatKHR vulkan_backend::choose_surface_format(const ::std::vector<VkSurfaceFormatKHR>& formats) {
	if (formats.size() == 1 && formats[0].format == VK_FORMAT_UNDEFINED)
		return {VK_FORMAT_B8G8R8A8_SRGB, formats[0].colorSpace};
	for (const auto& fmt : formats) {
		if (fmt.format == VK_FORMAT_B8G8R8A8_SRGB &&
			fmt.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR) {
			return fmt;
		}
	}
	for (const auto& fmt : formats) {
		if (fmt.format == VK_FORMAT_B8G8R8A8_UNORM) {
			return fmt;
		}
	}
	return formats[0];
}

VkPresentModeKHR vulkan_backend::choose_present_mode(const ::std::vector<VkPresentModeKHR>& modes) {
	if (char const* const disable_vsync = ::std::getenv("XRAY_VULKAN_DISABLE_VSYNC");
		disable_vsync && ::std::strcmp(disable_vsync, "1") == 0) {
		for (const auto& mode : modes) {
			if (mode == VK_PRESENT_MODE_IMMEDIATE_KHR) {
				return mode;
			}
		}
	}
	for (const auto& mode : modes) {
		if (mode == VK_PRESENT_MODE_MAILBOX_KHR) {
			return mode;
		}
	}
	return VK_PRESENT_MODE_FIFO_KHR;
}

VkExtent2D vulkan_backend::choose_swap_extent(const VkSurfaceCapabilitiesKHR& caps) {
	if (caps.currentExtent.width != ::std::numeric_limits<uint32_t>::max()) {
		return caps.currentExtent;
	}
	u32 width = 0, height = 0;
	if (!surface_adapter::query_surface_extent(m_output->surface_handle, width, height) || !width || !height) {
		return caps.minImageExtent;
	}
	VkExtent2D extent = { width, height };
	extent.width  = ::std::clamp(extent.width,  caps.minImageExtent.width,  caps.maxImageExtent.width);
	extent.height = ::std::clamp(extent.height, caps.minImageExtent.height, caps.maxImageExtent.height);
	return extent;
}

// -----------------------------------------------------------------------
// Swapchain Creation
// -----------------------------------------------------------------------

void vulkan_backend::create_swapchain() {
	if (m_output->is_offscreen) {
		u32 width = 960, height = 640;
		(void)surface_adapter::query_surface_extent(m_output->surface_handle, width, height);
		width = std::max(1u, width);
		height = std::max(1u, height);

		m_output->swapchain = VK_NULL_HANDLE;
		m_output->swapchain_format = VK_FORMAT_B8G8R8A8_SRGB;
		m_output->swapchain_extent = { width, height };

		uint32_t const image_count = MAX_FRAMES_IN_FLIGHT;
		m_output->swapchain_images.resize(image_count, VK_NULL_HANDLE);
		m_output->swapchain_image_views.resize(image_count, VK_NULL_HANDLE);
		m_output->offscreen_color_memory.resize(image_count, VK_NULL_HANDLE);

		for (uint32_t i = 0; i < image_count; ++i) {
			VkImageCreateInfo image_info = {};
			image_info.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
			image_info.imageType = VK_IMAGE_TYPE_2D;
			image_info.extent = { width, height, 1 };
			image_info.mipLevels = 1;
			image_info.arrayLayers = 1;
			image_info.format = m_output->swapchain_format;
			image_info.tiling = VK_IMAGE_TILING_OPTIMAL;
			image_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
			image_info.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
			image_info.samples = VK_SAMPLE_COUNT_1_BIT;
			image_info.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
			vk_check(create_handle(m_output->swapchain_images[i], vkCreateImage,
				m_device, &image_info, allocation_callbacks()), "Create offscreen color image");

			VkMemoryRequirements mem_req;
			vkGetImageMemoryRequirements(m_device, m_output->swapchain_images[i], &mem_req);
			VkMemoryAllocateInfo alloc_info = {};
			alloc_info.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
			alloc_info.allocationSize = mem_req.size;
			alloc_info.memoryTypeIndex = find_memory_type(mem_req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
			vk_check(allocate_memory(alloc_info, m_output->offscreen_color_memory[i]), "Allocate offscreen color memory");
			vk_check(vkBindImageMemory(m_device, m_output->swapchain_images[i], m_output->offscreen_color_memory[i], 0), "Bind offscreen color memory");

			VkImageViewCreateInfo view_info = {};
			view_info.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
			view_info.image = m_output->swapchain_images[i];
			view_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
			view_info.format = m_output->swapchain_format;
			view_info.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
			vk_check(create_handle(m_output->swapchain_image_views[i], vkCreateImageView,
				m_device, &view_info, allocation_callbacks()), "Create offscreen image view");
		}
		m_output->images_in_flight.assign(image_count, VK_NULL_HANDLE);
		m_output->render_finished_semaphores.clear();
		m_output->present_fences.clear();
		m_output->present_pending.clear();
		diagnostic("info", "Vulkan offscreen target created: %ux%u, %u images, format=%d",
			width, height, image_count, (int)m_output->swapchain_format);
		return;
	}

	if (m_output->surface == VK_NULL_HANDLE) {
		diagnostic("info", "No surface — skipping swapchain creation (headless mode)");
		return;
	}

	VkSurfaceCapabilitiesKHR surface_caps;
	vk_check(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(m_physical_device, m_output->surface, &surface_caps), "Query swapchain capabilities");

	uint32_t format_count = 0;
	vk_check(vkGetPhysicalDeviceSurfaceFormatsKHR(m_physical_device, m_output->surface, &format_count, nullptr), "Count surface formats");
	::std::vector<VkSurfaceFormatKHR> surface_formats(format_count);
	if (format_count > 0)
		vk_check(vkGetPhysicalDeviceSurfaceFormatsKHR(m_physical_device, m_output->surface, &format_count, surface_formats.data()), "Read surface formats");

	uint32_t present_mode_count = 0;
	vk_check(vkGetPhysicalDeviceSurfacePresentModesKHR(m_physical_device, m_output->surface, &present_mode_count, nullptr), "Count present modes");
	::std::vector<VkPresentModeKHR> present_modes(present_mode_count);
	if (present_mode_count > 0)
		vk_check(vkGetPhysicalDeviceSurfacePresentModesKHR(m_physical_device, m_output->surface, &present_mode_count, present_modes.data()), "Read present modes");

	if (surface_formats.empty() || present_modes.empty()) {
		throw ::std::runtime_error("Swapchain not supported on this surface");
	}

	VkSurfaceFormatKHR chosen_format = choose_surface_format(surface_formats);
	VkPresentModeKHR   chosen_mode   = choose_present_mode(present_modes);
	VkExtent2D         chosen_extent = choose_swap_extent(surface_caps);
	if (!chosen_extent.width || !chosen_extent.height) throw ::std::runtime_error("Surface has zero extent");

	m_output->swapchain_format = chosen_format.format;
	m_output->swapchain_extent = chosen_extent;

	uint32_t image_count = surface_caps.minImageCount + 1;
	if (surface_caps.maxImageCount > 0 && image_count > surface_caps.maxImageCount)
		image_count = surface_caps.maxImageCount;

	VkSwapchainCreateInfoKHR swapchain_info = {};
	swapchain_info.sType            = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
	swapchain_info.surface          = m_output->surface;
	swapchain_info.minImageCount    = image_count;
	swapchain_info.imageFormat      = chosen_format.format;
	swapchain_info.imageColorSpace  = chosen_format.colorSpace;
	swapchain_info.imageExtent      = chosen_extent;
	swapchain_info.imageArrayLayers = 1;
	swapchain_info.imageUsage       = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    if (m_frames->capture_enabled) {
        if (!(surface_caps.supportedUsageFlags & VK_IMAGE_USAGE_TRANSFER_SRC_BIT))
            throw std::runtime_error("Test capture requires swapchain transfer-source support");
        swapchain_info.imageUsage |= VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    }
	swapchain_info.preTransform     = surface_caps.currentTransform;
	swapchain_info.compositeAlpha   = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
	for (auto alpha : {VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR, VK_COMPOSITE_ALPHA_PRE_MULTIPLIED_BIT_KHR,
		VK_COMPOSITE_ALPHA_POST_MULTIPLIED_BIT_KHR, VK_COMPOSITE_ALPHA_INHERIT_BIT_KHR}) {
		if (surface_caps.supportedCompositeAlpha & alpha) { swapchain_info.compositeAlpha = alpha; break; }
	}
	swapchain_info.presentMode      = chosen_mode;
	swapchain_info.clipped          = VK_TRUE;
    if (m_frames->capture_enabled) swapchain_info.clipped=VK_FALSE;
	swapchain_info.oldSwapchain     = VK_NULL_HANDLE;

	uint32_t queue_family_indices[] = { m_graphics_queue_family_index, m_present_queue_family_index };
	if (m_graphics_queue_family_index != m_present_queue_family_index) {
		swapchain_info.imageSharingMode      = VK_SHARING_MODE_CONCURRENT;
		swapchain_info.queueFamilyIndexCount = 2;
		swapchain_info.pQueueFamilyIndices   = queue_family_indices;
	} else {
		swapchain_info.imageSharingMode      = VK_SHARING_MODE_EXCLUSIVE;
	}

    vk_check(create_handle(m_output->swapchain, vkCreateSwapchainKHR,
        m_device, &swapchain_info, allocation_callbacks()), "Create Vulkan swapchain");

	vk_check(vkGetSwapchainImagesKHR(m_device, m_output->swapchain, &image_count, nullptr), "Count swapchain images");
	m_output->swapchain_images.resize(image_count);
	vk_check(vkGetSwapchainImagesKHR(m_device, m_output->swapchain, &image_count, m_output->swapchain_images.data()), "Read swapchain images");

	m_output->swapchain_image_views.resize(image_count);
	for (uint32_t i = 0; i < image_count; ++i) {
		VkImageViewCreateInfo view_info = {};
		view_info.sType    = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
		view_info.image    = m_output->swapchain_images[i];
		view_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
		view_info.format   = m_output->swapchain_format;
		view_info.components.r = VK_COMPONENT_SWIZZLE_IDENTITY;
		view_info.components.g = VK_COMPONENT_SWIZZLE_IDENTITY;
		view_info.components.b = VK_COMPONENT_SWIZZLE_IDENTITY;
		view_info.components.a = VK_COMPONENT_SWIZZLE_IDENTITY;
		view_info.subresourceRange.aspectMask     = VK_IMAGE_ASPECT_COLOR_BIT;
		view_info.subresourceRange.baseMipLevel   = 0;
		view_info.subresourceRange.levelCount     = 1;
		view_info.subresourceRange.baseArrayLayer = 0;
		view_info.subresourceRange.layerCount     = 1;

        vk_check(create_handle(m_output->swapchain_image_views[i], vkCreateImageView,
            m_device, &view_info, allocation_callbacks()), "Create swapchain image view");
	}

	diagnostic("info", "Vulkan swapchain created: %ux%u, %u images, format=%d, mode=%d",
		chosen_extent.width, chosen_extent.height, image_count,
		(int)chosen_format.format, (int)chosen_mode);
	m_output->images_in_flight.assign(image_count, VK_NULL_HANDLE);
	m_output->render_finished_semaphores.resize(image_count, VK_NULL_HANDLE);
	m_output->present_fences.resize(m_present_fences_enabled ? image_count : 0, VK_NULL_HANDLE);
	m_output->present_pending.assign(m_output->present_fences.size(), false);
	VkSemaphoreCreateInfo semaphore_info = {};
	semaphore_info.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
	VkFenceCreateInfo fence_info = {};
	fence_info.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
	for (uint32_t i = 0; i < image_count; ++i) {
        vk_check(create_handle(m_output->render_finished_semaphores[i], vkCreateSemaphore,
            m_device, &semaphore_info, allocation_callbacks()), "Create per-image present semaphore");
		if (m_present_fences_enabled)
            vk_check(create_handle(m_output->present_fences[i], vkCreateFence,
                m_device, &fence_info, allocation_callbacks()), "Create presentation fence");
	}
}

// -----------------------------------------------------------------------
// Depth Format Selection
// -----------------------------------------------------------------------

VkFormat vulkan_backend::find_depth_format() {
	const VkFormat candidates[] = {
		VK_FORMAT_D32_SFLOAT,
		VK_FORMAT_D32_SFLOAT_S8_UINT,
		VK_FORMAT_D24_UNORM_S8_UINT
	};

	for (auto format : candidates) {
		VkFormatProperties props;
		vkGetPhysicalDeviceFormatProperties(m_physical_device, format, &props);
		if (props.optimalTilingFeatures & VK_FORMAT_FEATURE_DEPTH_STENCIL_ATTACHMENT_BIT) {
			return format;
		}
	}
	throw ::std::runtime_error("Failed to find supported depth format");
}

// -----------------------------------------------------------------------
// Memory Type Finder
// -----------------------------------------------------------------------

uint32_t vulkan_backend::find_memory_type(uint32_t type_filter, VkMemoryPropertyFlags properties) {
	VkPhysicalDeviceMemoryProperties mem_properties;
	vkGetPhysicalDeviceMemoryProperties(m_physical_device, &mem_properties);

	for (uint32_t i = 0; i < mem_properties.memoryTypeCount; ++i) {
		if ((type_filter & (1 << i)) &&
			(mem_properties.memoryTypes[i].propertyFlags & properties) == properties) {
			return i;
		}
	}
	throw ::std::runtime_error("Failed to find suitable memory type");
}

// -----------------------------------------------------------------------
// Depth Resource
// -----------------------------------------------------------------------

void vulkan_backend::create_depth_resource() {
	m_output->depth_format = find_depth_format();
	m_output->depth_attachments.resize(m_output->swapchain_images.size());
	for (auto& depth : m_output->depth_attachments) {

	VkImageCreateInfo image_info = {};
	image_info.sType         = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
	image_info.imageType     = VK_IMAGE_TYPE_2D;
	image_info.extent.width  = m_output->swapchain_extent.width;
	image_info.extent.height = m_output->swapchain_extent.height;
	image_info.extent.depth  = 1;
	image_info.mipLevels     = 1;
	image_info.arrayLayers   = 1;
	image_info.format        = m_output->depth_format;
	image_info.tiling        = VK_IMAGE_TILING_OPTIMAL;
	image_info.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
	image_info.usage         = VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT;
	image_info.samples       = VK_SAMPLE_COUNT_1_BIT;
	image_info.sharingMode   = VK_SHARING_MODE_EXCLUSIVE;

    vk_check(create_handle(depth.image, vkCreateImage, m_device, &image_info, allocation_callbacks()), "Create depth image");

	VkMemoryRequirements mem_req;
	vkGetImageMemoryRequirements(m_device, depth.image, &mem_req);

	VkMemoryAllocateInfo alloc_info = {};
	alloc_info.sType           = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
	alloc_info.allocationSize  = mem_req.size;
	alloc_info.memoryTypeIndex = find_memory_type(mem_req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

    vk_check(allocate_memory(alloc_info, depth.memory), "Allocate depth image memory");
	vk_check(vkBindImageMemory(m_device, depth.image, depth.memory, 0), "Bind depth memory");

	VkImageViewCreateInfo view_info = {};
	view_info.sType    = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
	view_info.image    = depth.image;
	view_info.viewType = VK_IMAGE_VIEW_TYPE_2D;
	view_info.format   = m_output->depth_format;
	view_info.subresourceRange.aspectMask     = VK_IMAGE_ASPECT_DEPTH_BIT;
	view_info.subresourceRange.baseMipLevel   = 0;
	view_info.subresourceRange.levelCount     = 1;
	view_info.subresourceRange.baseArrayLayer = 0;
	view_info.subresourceRange.layerCount     = 1;

    vk_check(create_handle(depth.view, vkCreateImageView, m_device, &view_info, allocation_callbacks()), "Create depth image view");
	}
	diagnostic("info", "Vulkan per-image depth resources created (%ux%u, format=%d)",
		m_output->swapchain_extent.width, m_output->swapchain_extent.height, (int)m_output->depth_format);
}

// -----------------------------------------------------------------------
// Sync Objects
// -----------------------------------------------------------------------

void vulkan_backend::create_sync_objects() {
	VkSemaphoreCreateInfo semaphore_info = {};
	semaphore_info.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

	VkFenceCreateInfo fence_info = {};
	fence_info.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
	fence_info.flags = VK_FENCE_CREATE_SIGNALED_BIT;  // start signaled so first frame doesn't wait

	for (uint32_t i = 0; i < MAX_FRAMES_IN_FLIGHT; ++i) {
        vk_check(create_handle(m_frames->image_available[i], vkCreateSemaphore,
            m_device, &semaphore_info, allocation_callbacks()), "Create image acquire semaphore");
        vk_check(create_handle(m_frames->in_flight[i], vkCreateFence,
            m_device, &fence_info, allocation_callbacks()), "Create in-flight frame fence");
	}
	diagnostic("info", "Vulkan sync objects created (%u frames in flight)", MAX_FRAMES_IN_FLIGHT);
}

// -----------------------------------------------------------------------
// Swapchain Cleanup
// -----------------------------------------------------------------------

void vulkan_backend::cleanup_swapchain() {
	for (auto const& depth : m_output->depth_attachments) {
		if (depth.view) vkDestroyImageView(m_device, depth.view, allocation_callbacks());
		if (depth.image) vkDestroyImage(m_device, depth.image, allocation_callbacks());
		free_memory(depth.memory);
	}
	m_output->depth_attachments.clear();
	for (auto semaphore : m_output->render_finished_semaphores)
		if (semaphore) vkDestroySemaphore(m_device, semaphore, allocation_callbacks());
	for (auto fence : m_output->present_fences)
		if (fence) vkDestroyFence(m_device, fence, allocation_callbacks());
	m_output->render_finished_semaphores.clear();
	m_output->present_fences.clear();
	m_output->present_pending.clear();
	m_output->images_in_flight.clear();

	for (auto& view : m_output->swapchain_image_views) {
		if (view != VK_NULL_HANDLE)
			vkDestroyImageView(m_device, view, allocation_callbacks());
	}
	m_output->swapchain_image_views.clear();
	if (!m_output->offscreen_color_memory.empty()) {
		for (auto image : m_output->swapchain_images) {
			if (image != VK_NULL_HANDLE)
				vkDestroyImage(m_device, image, allocation_callbacks());
		}
		for (auto memory : m_output->offscreen_color_memory)
			free_memory(memory);
		m_output->offscreen_color_memory.clear();
	}
	m_output->swapchain_images.clear();

	if (m_output->swapchain != VK_NULL_HANDLE) {
		vkDestroySwapchainKHR(m_device, m_output->swapchain, allocation_callbacks());
		m_output->swapchain = VK_NULL_HANDLE;
	}
	m_output->swapchain_extent = {0, 0};
}

// -----------------------------------------------------------------------
// Resize Swapchain
// -----------------------------------------------------------------------

void vulkan_backend::resize_swapchain(u32 width, u32 height) {
	// Defer recreation until the next begin_frame, never destroy acquired images.
	(void)width;
	(void)height;
	m_output->swapchain_dirty = true;
}

void vulkan_backend::wait_for_presentations() {
	for (size_t i = 0; i < m_output->present_pending.size(); ++i) {
		if (!m_output->present_pending[i]) continue;
		vk_check(vkWaitForFences(m_device, 1, &m_output->present_fences[i], VK_TRUE, UINT64_MAX), "Wait for presentation fence");
		m_output->present_pending[i] = false;
	}
}

bool vulkan_backend::refresh_swapchain() {
	u32 surface_w = 0, surface_h = 0;
	if (!surface_adapter::is_surface_alive(m_output->surface_handle)) throw ::std::runtime_error("Cannot query render window extent");
	if (surface_adapter::is_surface_minimized(m_output->surface_handle)) return false;
	if (surface_adapter::query_surface_extent(m_output->surface_handle, surface_w, surface_h) && (!surface_w || !surface_h)) return false;
	if (m_output->is_offscreen) {
		if (!surface_w || !surface_h) return false;
		if (!m_output->swapchain_dirty && !m_output->swapchain_images.empty() && surface_w == m_output->swapchain_extent.width && surface_h == m_output->swapchain_extent.height) return true;
		if (m_recording || m_frame_submitted) throw ::std::runtime_error("Swapchain recreation requested during an active frame");
		++m_device_idle_calls;
		vk_check(vkDeviceWaitIdle(m_device), "Wait for offscreen target rendering");
		m_completed_serial = m_submitted_serial;
		collect_retired();
		cleanup_swapchain();
		create_swapchain();
		create_depth_resource();
		m_output->swapchain_dirty = false;
		record_swapchain_recreation();
		return true;
	}
	VkSurfaceCapabilitiesKHR caps = {};
	vk_check(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(m_physical_device, m_output->surface, &caps), "Query current surface extent");
	VkExtent2D const extent = choose_swap_extent(caps);
	uint32_t width = extent.width;
	uint32_t height = extent.height;
	if (!width || !height) return false;
	if (!m_output->swapchain_dirty && m_output->swapchain && width == m_output->swapchain_extent.width && height == m_output->swapchain_extent.height) return true;
	if (m_recording || m_frame_submitted) throw ::std::runtime_error("Swapchain recreation requested during an active frame");
    ++m_device_idle_calls;
	vk_check(vkDeviceWaitIdle(m_device), "Wait for swapchain rendering");
	m_completed_serial = m_submitted_serial;
	collect_retired();
	wait_for_presentations();
	cleanup_swapchain();
	// Pipelines depend on dynamic rendering attachment formats, not swapchain dimensions.
	// The full compatibility signature selects a new pipeline if formats change.
	create_swapchain();
	create_depth_resource();
	m_output->swapchain_dirty = false;
	record_swapchain_recreation();
	return true;
}

// =======================================================================
// Frame Lifecycle
// =======================================================================

// -----------------------------------------------------------------------
// begin_frame: wait for fence, acquire image, begin cmd buffer + render pass
// -----------------------------------------------------------------------

bool vulkan_backend::begin_frame() {
	if (!m_initialized || !m_output->surface_handle.valid() || m_recording || m_frame_submitted) return false;
    require_output_owner();
    if (m_performance_enabled) m_frame_started_ns=performance_clock_ns();
	if (!refresh_swapchain()) return false;

	// Wait for the previous frame using this slot to finish
    VkResult waited = vkWaitForFences(m_device, 1, &m_frames->in_flight[m_frames->current_frame], VK_TRUE, UINT64_MAX);
    if (waited != VK_SUCCESS) {
        m_initialized = false;
        vk_check(waited, "Wait for frame fence");
    }
	m_completed_serial = ::std::max(m_completed_serial, m_frames->serials[m_frames->current_frame]);
    read_frame_timestamp(m_frames->current_frame);
    prepare_frame_timestamps();
	// Release completed command references before recycling their descriptors
	// and retired resources. The slot fence already guarantees GPU completion.
	VkCommandBuffer cmd = m_frames->command_buffers[m_frames->current_frame];
	vk_check(vkResetCommandBuffer(cmd, 0), "Reset frame command buffer");
	collect_retired();

    reset_material_frame();

	if (m_output->is_offscreen) {
		m_output->current_image_index = m_frames->current_frame % static_cast<uint32_t>(m_output->swapchain_images.size());
	} else {
		// Acquire the next swapchain image
		VkResult result = vkAcquireNextImageKHR(
			m_device, m_output->swapchain, 1000000000ull,
			m_frames->image_available[m_frames->current_frame],
			VK_NULL_HANDLE,
			&m_output->current_image_index
		);

		if (result == VK_ERROR_OUT_OF_DATE_KHR) {
			m_output->swapchain_dirty = true;
			return false;
		}
		if (result == VK_TIMEOUT || result == VK_NOT_READY) return false;
		if (result != VK_SUCCESS && result != VK_SUBOPTIMAL_KHR) {
			m_initialized = false;
			vk_check(result, "Acquire swapchain image");
		}
		if (result == VK_SUBOPTIMAL_KHR) m_output->swapchain_dirty = true;
	}
	if (m_output->images_in_flight[m_output->current_image_index])
		vk_check(vkWaitForFences(m_device, 1, &m_output->images_in_flight[m_output->current_image_index], VK_TRUE, UINT64_MAX), "Wait for acquired image rendering");

	// Begin command buffer
	VkCommandBufferBeginInfo begin_info = {};
	begin_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
	vk_check(vkBeginCommandBuffer(cmd, &begin_info), "Begin frame command buffer");
    begin_frame_timestamp(cmd);

	m_recording = true;
    m_presentation_contents = false;
    begin_presentation_pass();
	m_bound_vb = m_bound_ib = RHI_INVALID_HANDLE;
    m_bound_constants[0] = m_bound_constants[1] = 0;
    m_bound_textures = {}; m_bound_sampler = 0;
    m_bound_comparison_sampler=0; set_depth_bias(0,0);
	return true;
}

// -----------------------------------------------------------------------
// end_frame: end render pass + cmd buffer and submit; present is explicit
// -----------------------------------------------------------------------

bool vulkan_backend::end_frame() {
	if (!m_initialized || !m_recording) return false;
    if (!m_pass_active || !m_presentation_pass) throw std::runtime_error("Frame must finish in the presentation pass");

	VkCommandBuffer cmd = m_frames->command_buffers[m_frames->current_frame];

    end_render_pass();
	if (m_frames->capture_enabled || m_output->is_offscreen) {
        record_capture(cmd);
    } else {
        VkImageMemoryBarrier2 to_present = {};
        to_present.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        to_present.srcStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
        to_present.srcAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT;
        to_present.dstStageMask = VK_PIPELINE_STAGE_2_NONE;
        to_present.dstAccessMask = VK_ACCESS_2_NONE;
        to_present.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        to_present.newLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
        to_present.srcQueueFamilyIndex = to_present.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        to_present.image = m_output->swapchain_images[m_output->current_image_index];
        to_present.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        VkDependencyInfo dep = {};
        dep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        dep.imageMemoryBarrierCount = 1;
        dep.pImageMemoryBarriers = &to_present;
        vkCmdPipelineBarrier2(cmd, &dep);
    }
    if (m_performance_enabled)
        vkCmdWriteTimestamp2(cmd,VK_PIPELINE_STAGE_2_BOTTOM_OF_PIPE_BIT,m_frames->timestamps,m_frames->current_frame*2+1);
	m_recording = false;
	VkResult const recorded = vkEndCommandBuffer(cmd);
	if (recorded != VK_SUCCESS) {
		diagnostic("error", "End command buffer failed (VkResult=%d)", static_cast<int>(recorded));
		m_initialized = false;
		return false;
	}

	// Submit with synchronization2
	VkSemaphoreSubmitInfo wait_info = {};
	wait_info.sType = VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
	wait_info.semaphore = m_frames->image_available[m_frames->current_frame];
	wait_info.stageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;

	VkCommandBufferSubmitInfo cmd_info = {};
	cmd_info.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_SUBMIT_INFO;
	cmd_info.commandBuffer = cmd;

	VkSemaphoreSubmitInfo signal_info = {};
	VkSubmitInfo2 submit_info = {};
	submit_info.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO_2;
	submit_info.commandBufferInfoCount = 1;
	submit_info.pCommandBufferInfos = &cmd_info;
	if (!m_output->is_offscreen) {
		signal_info.sType = VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
		signal_info.semaphore = m_output->render_finished_semaphores[m_output->current_image_index];
		signal_info.stageMask = VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
		submit_info.waitSemaphoreInfoCount = 1;
		submit_info.pWaitSemaphoreInfos = &wait_info;
		submit_info.signalSemaphoreInfoCount = 1;
		submit_info.pSignalSemaphoreInfos = &signal_info;
	}

	// Reset only after successful command recording, immediately before submit.
	vk_check(vkResetFences(m_device, 1, &m_frames->in_flight[m_frames->current_frame]), "Reset submit fence");
    VkResult submitted = vkQueueSubmit2(m_graphics_queue, 1, &submit_info, m_frames->in_flight[m_frames->current_frame]);
	if (submitted != VK_SUCCESS) {
		diagnostic("error", "Submit draw command buffer failed (VkResult=%d)", static_cast<int>(submitted));
		m_initialized = false;
		return false;
	}
	m_output->images_in_flight[m_output->current_image_index] = m_frames->in_flight[m_frames->current_frame];
	m_frames->serials[m_frames->current_frame] = ++m_submitted_serial;
    if (m_performance_enabled) m_frames->timestamp_pending[m_frames->current_frame]=true;
	m_frame_submitted = true;
    if (m_frames->capture_enabled || m_output->is_offscreen) { m_frames->capture_frame=m_frames->current_frame; m_frames->capture_valid=true; }
	return true;
}

bool vulkan_backend::present() {
	if (!m_initialized || !m_frame_submitted) return false;
	if (m_output->is_offscreen) {
		VkFence frame_fence = m_frames->in_flight[m_frames->current_frame];
		vk_check(vkWaitForFences(m_device, 1, &frame_fence, VK_TRUE, UINT64_MAX), "Wait for offscreen frame fence");
		auto const& slot = m_frames->captures[m_frames->current_frame];
		if (slot.buffer.mapped && slot.width > 0 && slot.height > 0) {
			if (!slot.coherent) {
				VkMappedMemoryRange range = {};
				range.sType = VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
				range.memory = slot.buffer.memory;
				range.size = VK_WHOLE_SIZE;
				vk_check(vkInvalidateMappedMemoryRanges(m_device, 1, &range), "Invalidate offscreen readback memory");
			}
			surface_adapter::publish_offscreen_frame(m_output->surface_handle, slot.buffer.mapped, slot.width, slot.height);
		}
		m_frame_submitted = false;
		++m_output->presented_frames;
		if (m_performance_enabled) {
			++m_frames->performance.cpu_samples;
			m_frames->performance.cpu_serial = m_submitted_serial;
			m_frames->performance.cpu_frame_ns = performance_clock_ns() - m_frame_started_ns;
		}
		m_frames->current_frame = (m_frames->current_frame + 1) % MAX_FRAMES_IN_FLIGHT;
		return true;
	}
	VkSemaphore signal_semaphores[] = { m_output->render_finished_semaphores[m_output->current_image_index] };
	VkPresentInfoKHR present_info = {};
	present_info.sType              = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
	present_info.waitSemaphoreCount = 1;
	present_info.pWaitSemaphores    = signal_semaphores;
	present_info.swapchainCount     = 1;
	present_info.pSwapchains        = &m_output->swapchain;
	present_info.pImageIndices      = &m_output->current_image_index;
	VkSwapchainPresentFenceInfoEXT present_fence_info = {};
	if (m_present_fences_enabled) {
		VkFence fence = m_output->present_fences[m_output->current_image_index];
		if (m_output->present_pending[m_output->current_image_index])
			vk_check(vkWaitForFences(m_device, 1, &fence, VK_TRUE, UINT64_MAX), "Wait for image presentation");
		vk_check(vkResetFences(m_device, 1, &fence), "Reset presentation fence");
		m_output->present_pending[m_output->current_image_index] = false;
		present_fence_info.sType = VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_FENCE_INFO_EXT;
		present_fence_info.swapchainCount = 1;
		present_fence_info.pFences = &m_output->present_fences[m_output->current_image_index];
		present_info.pNext = &present_fence_info;
	}

	VkQueue present_queue = (m_present_queue != VK_NULL_HANDLE) ? m_present_queue : m_graphics_queue;
    VkResult result = vkQueuePresentKHR(present_queue, &present_info);
	m_frame_submitted = false;
	if (result != VK_SUCCESS && result != VK_ERROR_OUT_OF_DATE_KHR && result != VK_SUBOPTIMAL_KHR) {
		diagnostic("error", "Present swapchain image failed (VkResult=%d)", static_cast<int>(result));
		m_initialized = false;
		return false;
	}
	if (m_present_fences_enabled) m_output->present_pending[m_output->current_image_index] = true;

	if (result == VK_ERROR_OUT_OF_DATE_KHR || result == VK_SUBOPTIMAL_KHR) {
		m_output->swapchain_dirty = true;
	}

	// Advance only this output; other views retain their own frame slots/history.
    ++m_output->presented_frames;
    if (m_performance_enabled) {
        ++m_frames->performance.cpu_samples;
        m_frames->performance.cpu_serial=m_submitted_serial;
        m_frames->performance.cpu_frame_ns=performance_clock_ns()-m_frame_started_ns;
    }
	m_frames->current_frame = (m_frames->current_frame + 1) % MAX_FRAMES_IN_FLIGHT;
	return true;
}

// =======================================================================
// Pipeline State (Phase 2)
// =======================================================================

namespace {

VkPrimitiveTopology get_vk_topology(xray::render::rhi::topology topo) {
	using namespace xray::render::rhi;
	switch (topo) {
		case topology::point_list: return VK_PRIMITIVE_TOPOLOGY_POINT_LIST;
		case topology::line_list: return VK_PRIMITIVE_TOPOLOGY_LINE_LIST;
		case topology::line_strip: return VK_PRIMITIVE_TOPOLOGY_LINE_STRIP;
		case topology::triangle_list: return VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
		case topology::triangle_strip: return VK_PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP;
		default: return VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
	}
}

VkPolygonMode get_vk_polygon_mode(xray::render::rhi::fill_mode fill) {
	return fill == xray::render::rhi::fill_mode::solid ? VK_POLYGON_MODE_FILL : VK_POLYGON_MODE_LINE;
}

VkCullModeFlags get_vk_cull_mode(xray::render::rhi::cull_mode cull) {
	using namespace xray::render::rhi;
	switch (cull) {
		case cull_mode::none: return VK_CULL_MODE_NONE;
		case cull_mode::front: return VK_CULL_MODE_FRONT_BIT;
		case cull_mode::back: return VK_CULL_MODE_BACK_BIT;
		default: return VK_CULL_MODE_BACK_BIT;
	}
}

VkCompareOp get_vk_compare_op(xray::render::rhi::comparison_func func) {
	using namespace xray::render::rhi;
	switch (func) {
		case comparison_func::never: return VK_COMPARE_OP_NEVER;
		case comparison_func::less: return VK_COMPARE_OP_LESS;
		case comparison_func::equal: return VK_COMPARE_OP_EQUAL;
		case comparison_func::less_equal: return VK_COMPARE_OP_LESS_OR_EQUAL;
		case comparison_func::greater: return VK_COMPARE_OP_GREATER;
		case comparison_func::not_equal: return VK_COMPARE_OP_NOT_EQUAL;
		case comparison_func::greater_equal: return VK_COMPARE_OP_GREATER_OR_EQUAL;
		case comparison_func::always: return VK_COMPARE_OP_ALWAYS;
		default: return VK_COMPARE_OP_LESS_OR_EQUAL;
	}
}

VkBlendFactor get_vk_blend_factor(xray::render::rhi::blend b) {
	using namespace xray::render::rhi;
	switch (b) {
		case blend::zero: return VK_BLEND_FACTOR_ZERO;
		case blend::one: return VK_BLEND_FACTOR_ONE;
		case blend::src_color: return VK_BLEND_FACTOR_SRC_COLOR;
		case blend::inv_src_color: return VK_BLEND_FACTOR_ONE_MINUS_SRC_COLOR;
		case blend::src_alpha: return VK_BLEND_FACTOR_SRC_ALPHA;
		case blend::inv_src_alpha: return VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
		case blend::dest_alpha: return VK_BLEND_FACTOR_DST_ALPHA;
		case blend::inv_dest_alpha: return VK_BLEND_FACTOR_ONE_MINUS_DST_ALPHA;
		case blend::dest_color: return VK_BLEND_FACTOR_DST_COLOR;
		case blend::inv_dest_color: return VK_BLEND_FACTOR_ONE_MINUS_DST_COLOR;
		default: return VK_BLEND_FACTOR_ZERO;
	}
}

VkBlendOp get_vk_blend_op(xray::render::rhi::blend_op op) {
	using namespace xray::render::rhi;
	switch (op) {
		case blend_op::add: return VK_BLEND_OP_ADD;
		case blend_op::subtract: return VK_BLEND_OP_SUBTRACT;
		case blend_op::rev_subtract: return VK_BLEND_OP_REVERSE_SUBTRACT;
		case blend_op::min: return VK_BLEND_OP_MIN;
		case blend_op::max: return VK_BLEND_OP_MAX;
		default: return VK_BLEND_OP_ADD;
	}
}

} // anonymous namespace


void vulkan_backend::set_topology(rhi::topology topo) {
	m_current_state.prim_topology = topo;
}

void vulkan_backend::set_vertex_layout(rhi::vertex_input_layout const& layout) {
	if (!m_initialized) throw ::std::runtime_error("Vertex layout requires an initialized device");
	VkPhysicalDeviceProperties properties;
	vkGetPhysicalDeviceProperties(m_physical_device, &properties);
	auto const& limits = properties.limits;
	if (!valid_vertex_layout(layout, limits.maxVertexInputBindingStride, limits.maxVertexInputAttributes, limits.maxVertexInputAttributeOffset))
		throw ::std::runtime_error("Invalid vertex input layout");
	for (auto const& attribute : layout.attributes) {
		VkFormatProperties format_properties;
		vkGetPhysicalDeviceFormatProperties(m_physical_device, vertex_format(attribute.type), &format_properties);
		if (!(format_properties.bufferFeatures & VK_FORMAT_FEATURE_VERTEX_BUFFER_BIT))
			throw ::std::runtime_error("Vertex format unsupported by this GPU");
	}
	m_current_state.vertex_layout = layout;
}

void vulkan_backend::set_rasterizer_state(rhi::fill_mode fill, rhi::cull_mode cull) {
    if(fill!=rhi::fill_mode::solid && (fill!=rhi::fill_mode::wireframe || !m_non_solid_enabled))throw ::std::runtime_error("Device does not support non-solid rasterization");
	m_current_state.fill = fill;
	m_current_state.cull = cull;
}

void vulkan_backend::set_depth_stencil_state(bool depth_enable, bool depth_write, rhi::comparison_func func) {
	m_current_state.depth_enable = depth_enable;
	m_current_state.depth_write = depth_write;
	m_current_state.depth_func = func;
}
void vulkan_backend::set_stencil_state(bool enable, u32 reference, u32 read_mask, u32 write_mask, rhi::comparison_func compare, rhi::stencil_op pass) {
    if (reference > 255 || read_mask > 255 || write_mask > 255 || compare < rhi::comparison_func::never || compare > rhi::comparison_func::always
        || pass < rhi::stencil_op::keep || pass > rhi::stencil_op::decr) throw std::runtime_error("Invalid stencil state");
    m_current_state.stencil_enable = enable; m_current_state.stencil_reference = reference;
    m_current_state.stencil_read_mask = read_mask; m_current_state.stencil_write_mask = write_mask;
    m_current_state.stencil_compare = compare; m_current_state.stencil_pass = pass;
}

void vulkan_backend::set_blend_state(bool enable, rhi::blend src, rhi::blend dst, rhi::blend_op op) {
	m_current_state.blend_enable = enable;
	m_current_state.src_blend = src;
	m_current_state.dst_blend = dst;
	m_current_state.blend_operation = op;
}

void vulkan_backend::set_scissor(u32 x, u32 y, u32 width, u32 height) {
	if (!m_recording) return;
	VkCommandBuffer cmd = m_frames->command_buffers[m_frames->current_frame];
	VkRect2D scissor = { { (int32_t)x, (int32_t)y }, { width, height } };
	vkCmdSetScissor(cmd, 0, 1, &scissor);
}

void vulkan_backend::create_pipeline_layout() {
    VkPhysicalDeviceProperties properties; vkGetPhysicalDeviceProperties(m_physical_device,&properties);
    if (properties.limits.maxPerStageDescriptorSamplers<2 || properties.limits.maxDescriptorSetSamplers<2
        || properties.limits.maxPerStageDescriptorSampledImages<8 || properties.limits.maxDescriptorSetSampledImages<8)
        throw std::runtime_error("Material/shadow descriptor layout exceeds device limits");
    VkDescriptorSetLayoutBinding frame = {0, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 1, VK_SHADER_STAGE_VERTEX_BIT, nullptr};
    VkDescriptorSetLayoutBinding material[10] = {
        {0, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr},
        {1, VK_DESCRIPTOR_TYPE_SAMPLER, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr},
        {2, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr},
        {3, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr},
        {4, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr},
        {5, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr},
        {6, VK_DESCRIPTOR_TYPE_SAMPLER, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr},
        {7, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr},
        {8, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr},
        {9, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr}};
    VkDescriptorSetLayoutBinding object = {0, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 1, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, nullptr};
    for (int set = 0; set < 3; ++set) {
        VkDescriptorSetLayoutCreateInfo layout = {};
        layout.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
        layout.bindingCount = set == 1 ? 10 : 1;
        layout.pBindings = set == 0 ? &frame : set == 1 ? material : &object;
        vk_check(create_handle(m_descriptor_layouts[set], vkCreateDescriptorSetLayout,
            m_device, &layout, allocation_callbacks()), "Create material set layout");
    }
    if (m_bindless_enabled) {
        VkDescriptorSetLayoutBinding bindless_bindings[2] = {
            {0, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, MAX_BINDLESS_2D, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr},
            {1, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, MAX_BINDLESS_CUBE, VK_SHADER_STAGE_FRAGMENT_BIT, nullptr}
        };
        VkDescriptorBindingFlags flags[2] = {
            VK_DESCRIPTOR_BINDING_PARTIALLY_BOUND_BIT | VK_DESCRIPTOR_BINDING_UPDATE_AFTER_BIND_BIT,
            VK_DESCRIPTOR_BINDING_PARTIALLY_BOUND_BIT | VK_DESCRIPTOR_BINDING_UPDATE_AFTER_BIND_BIT
        };
        VkDescriptorSetLayoutBindingFlagsCreateInfo binding_flags = {};
        binding_flags.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_BINDING_FLAGS_CREATE_INFO;
        binding_flags.bindingCount = 2;
        binding_flags.pBindingFlags = flags;
        VkDescriptorSetLayoutCreateInfo layout = {};
        layout.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
        layout.flags = VK_DESCRIPTOR_SET_LAYOUT_CREATE_UPDATE_AFTER_BIND_POOL_BIT;
        layout.pNext = &binding_flags;
        layout.bindingCount = 2;
        layout.pBindings = bindless_bindings;
        vk_check(create_handle(m_descriptor_layouts[3], vkCreateDescriptorSetLayout,
            m_device, &layout, allocation_callbacks()), "Create bindless set layout");
    }
    VkPipelineLayoutCreateInfo info = {};
    info.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    info.setLayoutCount = m_bindless_enabled ? 4u : 3u;
    info.pSetLayouts = m_descriptor_layouts;
    vk_check(create_handle(m_pipeline_layout, vkCreatePipelineLayout,
        m_device, &info, allocation_callbacks()), "Create material pipeline layout");

    VkDescriptorSetLayoutBinding compute_bindings[3] = {
        {0, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {1, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
        {2, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr}
    };
    VkDescriptorSetLayoutCreateInfo compute_layout = {};
    compute_layout.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    compute_layout.bindingCount = 3;
    compute_layout.pBindings = compute_bindings;
    vk_check(create_handle(m_compute_descriptor_layout, vkCreateDescriptorSetLayout,
        m_device, &compute_layout, allocation_callbacks()), "Create compute set layout");
    VkPipelineLayoutCreateInfo compute_pipeline_info = {};
    compute_pipeline_info.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    compute_pipeline_info.setLayoutCount = 1;
    compute_pipeline_info.pSetLayouts = &m_compute_descriptor_layout;
    vk_check(create_handle(m_compute_pipeline_layout, vkCreatePipelineLayout,
        m_device, &compute_pipeline_info, allocation_callbacks()), "Create compute pipeline layout");
}

VkPipeline vulkan_backend::get_or_create_pipeline(pipeline_state_desc const& desc) {
	pipeline_key key = desc;
    key.color_formats = m_pass_color_formats;
    key.color_format = key.color_formats.empty() ? 0 : key.color_formats[0];
    key.depth_format = m_pass_depth_format;
    key.samples=m_pass_samples;
    key.has_stencil = m_pass_has_stencil;
    if (desc.stencil_enable && key.depth_format != VK_FORMAT_D24_UNORM_S8_UINT && key.depth_format != VK_FORMAT_D32_SFLOAT_S8_UINT)
        throw std::runtime_error("Stencil draw requires a stencil attachment");
	auto it = m_pipeline_map.find(key);
	if (it != m_pipeline_map.end()) {
		return it->second;
	}

	// Missing shaders? Cannot create.
	if (desc.vs_shader == RHI_INVALID_HANDLE || desc.ps_shader == RHI_INVALID_HANDLE) {
		throw ::std::runtime_error("Draw requires vertex and pixel shaders");
	}

	vulkan_shader_entry* vs = m_shaders.get(desc.vs_shader);
	vulkan_shader_entry* ps = m_shaders.get(desc.ps_shader);
	if (!vs || !ps) throw ::std::runtime_error("Pipeline references a stale shader");

	std::vector<VkPipelineShaderStageCreateInfo> shader_stages;

	VkPipelineShaderStageCreateInfo vs_info = {};
	vs_info.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
	vs_info.stage = VK_SHADER_STAGE_VERTEX_BIT;
	vs_info.module = vs->module;
	vs_info.pName = vs->entry_point.c_str();
	shader_stages.push_back(vs_info);

	VkPipelineShaderStageCreateInfo ps_info = {};
	ps_info.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
	ps_info.stage = VK_SHADER_STAGE_FRAGMENT_BIT;
	ps_info.module = ps->module;
	ps_info.pName = ps->entry_point.c_str();
	shader_stages.push_back(ps_info);

	VkVertexInputBindingDescription binding = {0, desc.vertex_layout.stride, VK_VERTEX_INPUT_RATE_VERTEX};
	::std::vector<VkVertexInputAttributeDescription> attributes;
	for (auto const& attribute : desc.vertex_layout.attributes)
		attributes.push_back({attribute.location, 0, vertex_format(attribute.type), attribute.offset});
	VkPipelineVertexInputStateCreateInfo vertex_input_info = {};
	vertex_input_info.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
	vertex_input_info.vertexBindingDescriptionCount = attributes.empty() ? 0 : 1;
	vertex_input_info.pVertexBindingDescriptions = &binding;
	vertex_input_info.vertexAttributeDescriptionCount = static_cast<uint32_t>(attributes.size());
	vertex_input_info.pVertexAttributeDescriptions = attributes.data();

	VkPipelineInputAssemblyStateCreateInfo input_assembly = {};
	input_assembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
	input_assembly.topology = get_vk_topology(desc.prim_topology);
	input_assembly.primitiveRestartEnable = VK_FALSE;

	VkPipelineViewportStateCreateInfo viewport_state = {};
	viewport_state.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
	viewport_state.viewportCount = 1;
	viewport_state.scissorCount = 1;

	VkPipelineRasterizationStateCreateInfo rasterizer = {};
	rasterizer.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
	rasterizer.depthClampEnable = VK_FALSE;
	rasterizer.rasterizerDiscardEnable = VK_FALSE;
	rasterizer.polygonMode = get_vk_polygon_mode(desc.fill);
	rasterizer.lineWidth = 1.0f;
	rasterizer.cullMode = get_vk_cull_mode(desc.cull);
	rasterizer.frontFace = VK_FRONT_FACE_CLOCKWISE;
	rasterizer.depthBiasEnable = desc.depth_bias_enable;

	VkPipelineMultisampleStateCreateInfo multisampling = {};
	multisampling.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    multisampling.sampleShadingEnable=key.samples>1 ? VK_TRUE : VK_FALSE;
    multisampling.minSampleShading=1;
    multisampling.rasterizationSamples=static_cast<VkSampleCountFlagBits>(key.samples);

	VkPipelineDepthStencilStateCreateInfo depth_stencil = {};
	depth_stencil.sType = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
	depth_stencil.depthTestEnable = desc.depth_enable ? VK_TRUE : VK_FALSE;
	depth_stencil.depthWriteEnable = desc.depth_write ? VK_TRUE : VK_FALSE;
	depth_stencil.depthCompareOp = get_vk_compare_op(desc.depth_func);
	depth_stencil.depthBoundsTestEnable = VK_FALSE;
	depth_stencil.stencilTestEnable = desc.stencil_enable;
    depth_stencil.front = {VK_STENCIL_OP_KEEP, rhi::to_vk_stencil_op(desc.stencil_pass), VK_STENCIL_OP_KEEP,
        rhi::to_vk_compare_op(desc.stencil_compare), desc.stencil_read_mask, desc.stencil_write_mask, desc.stencil_reference};
    depth_stencil.back = depth_stencil.front;

	VkPipelineColorBlendAttachmentState color_blend_attachment = {};
	color_blend_attachment.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
	color_blend_attachment.blendEnable = desc.blend_enable ? VK_TRUE : VK_FALSE;
	color_blend_attachment.srcColorBlendFactor = get_vk_blend_factor(desc.src_blend);
	color_blend_attachment.dstColorBlendFactor = get_vk_blend_factor(desc.dst_blend);
	color_blend_attachment.colorBlendOp = get_vk_blend_op(desc.blend_operation);
	color_blend_attachment.srcAlphaBlendFactor = color_blend_attachment.srcColorBlendFactor;
	color_blend_attachment.dstAlphaBlendFactor = color_blend_attachment.dstColorBlendFactor;
	color_blend_attachment.alphaBlendOp = color_blend_attachment.colorBlendOp;

	VkPipelineColorBlendStateCreateInfo color_blending = {};
	color_blending.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
	color_blending.logicOpEnable = VK_FALSE;
    std::vector<VkPipelineColorBlendAttachmentState> color_attachments(key.color_formats.size(), color_blend_attachment);
	color_blending.attachmentCount = static_cast<u32>(color_attachments.size());
	color_blending.pAttachments = color_attachments.data();

	std::vector<VkDynamicState> dynamic_states = { VK_DYNAMIC_STATE_VIEWPORT, VK_DYNAMIC_STATE_SCISSOR, VK_DYNAMIC_STATE_DEPTH_BIAS };
	VkPipelineDynamicStateCreateInfo dynamic_state_info = {};
	dynamic_state_info.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
	dynamic_state_info.dynamicStateCount = (uint32_t)dynamic_states.size();
	dynamic_state_info.pDynamicStates = dynamic_states.data();

	std::vector<VkFormat> color_vk_formats;
	color_vk_formats.reserve(key.color_formats.size());
	for (u32 fmt : key.color_formats)
		color_vk_formats.push_back(static_cast<VkFormat>(fmt));

	VkPipelineRenderingCreateInfo rendering_info = {};
	rendering_info.sType = VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO;
	rendering_info.colorAttachmentCount = static_cast<uint32_t>(color_vk_formats.size());
	rendering_info.pColorAttachmentFormats = color_vk_formats.empty() ? nullptr : color_vk_formats.data();
	rendering_info.depthAttachmentFormat = static_cast<VkFormat>(key.depth_format);
	rendering_info.stencilAttachmentFormat = key.has_stencil ? static_cast<VkFormat>(key.depth_format) : VK_FORMAT_UNDEFINED;

	VkGraphicsPipelineCreateInfo pipeline_info = {};
	pipeline_info.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
	pipeline_info.pNext = &rendering_info;
	pipeline_info.stageCount = (uint32_t)shader_stages.size();
	pipeline_info.pStages = shader_stages.data();
	pipeline_info.pVertexInputState = &vertex_input_info;
	pipeline_info.pInputAssemblyState = &input_assembly;
	pipeline_info.pViewportState = &viewport_state;
	pipeline_info.pRasterizationState = &rasterizer;
	pipeline_info.pMultisampleState = &multisampling;
	pipeline_info.pDepthStencilState = &depth_stencil;
	pipeline_info.pColorBlendState = &color_blending;
	pipeline_info.pDynamicState = &dynamic_state_info;
	pipeline_info.layout = m_pipeline_layout;
	pipeline_info.renderPass = VK_NULL_HANDLE;
	pipeline_info.subpass = 0;

	VkPipeline pipeline = VK_NULL_HANDLE;
	auto const pipeline_started=m_performance_enabled ? performance_clock_ns() : 0;
	VkResult const created = vkCreateGraphicsPipelines(m_device, m_pipeline_cache, 1, &pipeline_info, allocation_callbacks(), &pipeline);
    ++m_pipeline_creations;
    if (m_performance_enabled) m_pipeline_creation_ns+=performance_clock_ns()-pipeline_started;
	if (created != VK_SUCCESS && pipeline) vkDestroyPipeline(m_device, pipeline, allocation_callbacks());
	vk_check(created, "Create graphics pipeline");
	try { m_pipeline_map.emplace(::std::move(key), pipeline); }
	catch (...) { vkDestroyPipeline(m_device, pipeline, allocation_callbacks()); throw; }
	return pipeline;
}

// =======================================================================
// RHI Interface
// =======================================================================

// -----------------------------------------------------------------------
// clear_render_target — stores the clear color for the next render pass
// -----------------------------------------------------------------------

void vulkan_backend::clear_render_target(rhi::format fmt, float r, float g, float b, float a) {
	m_output->clear_color[0] = r;
	m_output->clear_color[1] = g;
	m_output->clear_color[2] = b;
	m_output->clear_color[3] = a;
	// The actual clear happens at vkCmdBeginRenderPass in begin_frame()
}

// -----------------------------------------------------------------------
// Viewport
// -----------------------------------------------------------------------

void vulkan_backend::set_viewport(float x, float y, float width, float height) {
	if (!m_recording) return;
	VkCommandBuffer cmd = m_frames->command_buffers[m_frames->current_frame];
	VkViewport viewport = { x, y, width, height, 0.0f, 1.0f };
	vkCmdSetViewport(cmd, 0, 1, &viewport);
}

} // namespace vulkan
} // namespace render
} // namespace xray
