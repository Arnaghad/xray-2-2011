#define XRAY_LOG_MODULE_INITIATOR "test_vulkan_sync2_dynamic_rendering"
#include "test_check.hpp"

#include <xray/extensions.h>
#include <xray/platform_surface.h>
#include <xray/engine/capture_control_bus.h>
#include <xray/render/vulkan/api.h>
#include <xray/render/vulkan/diagnostics.h>
#include <xray/render/vulkan/platform_surface.h>
#include <xray/render/vulkan/sources/vulkan_backend.h>
#include <SDL3/SDL.h>

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <vector>

namespace {

bool validation_layer_available() {
    uint32_t layer_count = 0;
    if (vkEnumerateInstanceLayerProperties(&layer_count, nullptr) != VK_SUCCESS || !layer_count) {
        return false;
    }
    std::vector<VkLayerProperties> layers(layer_count);
    if (vkEnumerateInstanceLayerProperties(&layer_count, layers.data()) != VK_SUCCESS) {
        return false;
    }
    uint32_t ext_count = 0;
    if (vkEnumerateInstanceExtensionProperties(nullptr, &ext_count, nullptr) != VK_SUCCESS || !ext_count) {
        return false;
    }
    std::vector<VkExtensionProperties> exts(ext_count);
    if (vkEnumerateInstanceExtensionProperties(nullptr, &ext_count, exts.data()) != VK_SUCCESS) {
        return false;
    }
    bool const has_layer = std::any_of(layers.begin(), layers.end(), [](VkLayerProperties const& l) {
        return std::strcmp(l.layerName, "VK_LAYER_KHRONOS_validation") == 0;
    });
    bool const has_dbg = std::any_of(exts.begin(), exts.end(), [](VkExtensionProperties const& e) {
        return std::strcmp(e.extensionName, VK_EXT_DEBUG_UTILS_EXTENSION_NAME) == 0;
    });
    return has_layer && has_dbg;
}

void set_env_var(char const* key, char const* value) {
#if defined(_WIN32)
    _putenv_s(key, value);
#else
    setenv(key, value, 1);
#endif
}

} // namespace

int main() {
    using namespace xray::render::vulkan;
    using namespace xray::render::rhi;
    using xray::platform::native_surface_handle;

    bool const val_available = validation_layer_available();
    if (val_available) {
        set_env_var("XRAY_VULKAN_REQUIRE_VALIDATION", "1");
    }
    set_env_var("DISABLE_RTSS_LAYER", "1");
    set_env_var("DISABLE_VULKAN_OBS_CAPTURE", "1");

    vulkan_allocator_type alloc;
    set_memory_allocator(alloc);

    XRAY_TEST_CHECK(SDL_InitSubSystem(SDL_INIT_VIDEO));
    SDL_Window* win = SDL_CreateWindow(
        "xray_stage07_sync2_dyn_win",
        320,
        240,
        SDL_WINDOW_VULKAN | SDL_WINDOW_HIDDEN | SDL_WINDOW_RESIZABLE | SDL_WINDOW_HIGH_PIXEL_DENSITY
    );
    XRAY_TEST_CHECK(win != nullptr);
    SDL_SetWindowFocusable(win, false);

    native_surface_handle const surface = native_surface_handle::from_sdl_window(win);
    XRAY_TEST_CHECK(surface.valid());
    surface_adapter::register_sdl_window(surface, win);
    XRAY_TEST_CHECK(xray::engine::capture_control_bus::set_flag(surface, "XRAY_VULKAN_TEST_CAPTURE"));

    {
        vulkan_backend backend;
        XRAY_TEST_CHECK(backend.initialize(surface));
        XRAY_TEST_CHECK(backend.synchronization2_enabled());
        XRAY_TEST_CHECK(backend.dynamic_rendering_enabled());

        // Sampled texture upload + update via vkCmdPipelineBarrier2 / vkQueueSubmit2
        texture_desc sampled_desc;
        sampled_desc.type = texture_type::type_2d;
        sampled_desc.width = 16;
        sampled_desc.height = 16;
        sampled_desc.mip_levels = 1;
        sampled_desc.pixel_format = format::r8g8b8a8_unorm;
        sampled_desc.usage = texture_usage::sampled;
        std::vector<u8> texels(16u * 16u * 4u, 0x80u);
        texture_subresource_data sub{0, 0, texels.data(), texels.size(), 16u * 4u};
        rhi_texture_handle tex = backend.create_texture(sampled_desc, {sub});
        XRAY_TEST_CHECK(tex != RHI_INVALID_HANDLE);
        std::fill(texels.begin(), texels.end(), 0xC0u);
        backend.update_texture(tex, {sub});

        // Offscreen MSAA color + D24_S8 depth/stencil dynamic rendering + resolve + transition
        texture_desc msaa_desc;
        msaa_desc.type = texture_type::type_2d;
        msaa_desc.width = 64;
        msaa_desc.height = 64;
        msaa_desc.mip_levels = 1;
        msaa_desc.pixel_format = format::r8g8b8a8_unorm;
        msaa_desc.usage = texture_usage::render_target;
        msaa_desc.samples = 4;
        if (!backend.supports_samples(format::r8g8b8a8_unorm, 4)) {
            msaa_desc.samples = 1;
        }
        rhi_texture_handle offscreen_color = backend.create_texture(msaa_desc, {});
        XRAY_TEST_CHECK(offscreen_color != RHI_INVALID_HANDLE);

        texture_desc resolved_desc = msaa_desc;
        resolved_desc.samples = 1;
        rhi_texture_handle resolved_color = backend.create_texture(resolved_desc, {});
        XRAY_TEST_CHECK(resolved_color != RHI_INVALID_HANDLE);

        texture_desc depth_desc;
        depth_desc.type = texture_type::type_2d;
        depth_desc.width = 64;
        depth_desc.height = 64;
        depth_desc.mip_levels = 1;
        depth_desc.pixel_format = format::d24_s8_unorm;
        depth_desc.usage = texture_usage::depth_stencil;
        depth_desc.samples = msaa_desc.samples;
        rhi_texture_handle offscreen_depth = backend.create_texture(depth_desc, {});
        XRAY_TEST_CHECK(offscreen_depth != RHI_INVALID_HANDLE);

        for (int frame_idx = 0; frame_idx < 4; ++frame_idx) {
            if (frame_idx == 2) {
                SDL_SetWindowSize(win, 400, 300);
                SDL_SyncWindow(win);
                backend.resize_swapchain(400, 300);
            }
            backend.clear_render_target(format::r8g8b8a8_unorm, 0.1f, 0.2f, 0.3f, 1.0f);
            XRAY_TEST_CHECK(backend.begin_frame());
            backend.end_render_pass();

            backend.transition_texture(offscreen_color, resource_state::render_target);
            backend.transition_texture(offscreen_depth, resource_state::depth_write);
            render_pass_desc offscreen_pass;
            offscreen_pass.colors.push_back({offscreen_color, attachment_load::clear, attachment_store::store, {0.2f, 0.4f, 0.6f, 1.0f}});
            offscreen_pass.depth = {offscreen_depth, attachment_load::clear, attachment_store::store, 1.0f, 0};
            backend.begin_render_pass(offscreen_pass);
            backend.end_render_pass();

            if (msaa_desc.samples > 1) {
                backend.resolve_texture(offscreen_color, resolved_color);
            } else {
                backend.transition_texture(offscreen_color, resource_state::shader_resource);
            }
            backend.transition_texture(offscreen_depth, resource_state::depth_read);

            // Preserve swapchain presentation contents inside the same frame via dynamicRendering loadOp=LOAD
            backend.begin_presentation_pass(true);
            XRAY_TEST_CHECK(backend.end_frame());
            auto cap = backend.capture_frame();
            XRAY_TEST_CHECK(cap.width > 0 && cap.height > 0 && cap.bgra.size() == size_t(cap.width) * cap.height * 4u);
            XRAY_TEST_CHECK(backend.present());
        }

        auto perf = backend.performance();
        XRAY_TEST_CHECK(perf.pass_pair_creations == 0);
        XRAY_TEST_CHECK(perf.cached_pass_objects == 0);
        XRAY_TEST_CHECK(perf.pending_pass_objects == 0);

        backend.destroy_texture(tex);
        backend.destroy_texture(offscreen_depth);
        backend.destroy_texture(resolved_color);
        backend.destroy_texture(offscreen_color);
        backend.shutdown();
    }

    reset_memory_allocator();

    auto snap = diagnostics();
    if (val_available || std::getenv("XRAY_VULKAN_REQUIRE_VALIDATION")) {
        XRAY_TEST_CHECK(snap.validation_enabled);
    }
    XRAY_TEST_CHECK(snap.validation_errors == 0);
    XRAY_TEST_CHECK(snap.validation_warnings == 0);

    char const* const current_driver = SDL_GetCurrentVideoDriver();
    std::string const driver_name = current_driver ? current_driver : "unknown";

    xray::engine::capture_control_bus::clear_surface(surface);
    surface_adapter::unregister_sdl_window(surface);
    SDL_DestroyWindow(win);
    SDL_QuitSubSystem(SDL_INIT_VIDEO);

    std::cout << "PASS: xray_vulkan_sync2_dynamic_rendering_contract (driver="
              << driver_name
              << ", validation=" << (snap.validation_enabled ? "on" : "off") << ")\n";
    return 0;
}
