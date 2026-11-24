#define XRAY_LOG_MODULE_INITIATOR "test_vulkan_sync2_dynamic_rendering"
#include "test_check.hpp"

#include <xray/extensions.h>
#include <xray/os_include.h>
#include <xray/render/vulkan/api.h>
#include <xray/render/vulkan/diagnostics.h>
#include <xray/render/vulkan/sources/vulkan_backend.h>

#include <algorithm>
#include <cstdlib>
#include <vector>

namespace {

LRESULT CALLBACK dummy_wndproc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    return DefWindowProcA(hwnd, msg, wp, lp);
}

} // namespace

int main() {
    using namespace xray::render::vulkan;
    using namespace xray::render::rhi;

    _putenv("XRAY_VULKAN_REQUIRE_VALIDATION=1");
    _putenv("DISABLE_RTSS_LAYER=1");
    _putenv("DISABLE_VULKAN_OBS_CAPTURE=1");

    vulkan_allocator_type alloc;
    set_memory_allocator(alloc);

    WNDCLASSA wc = {};
    wc.lpfnWndProc = &dummy_wndproc;
    wc.hInstance = GetModuleHandleA(nullptr);
    wc.lpszClassName = "xray_stage07_sync2_dyn_win";
    XRAY_TEST_CHECK(RegisterClassA(&wc) != 0);

    HWND hwnd = CreateWindowExA(
        WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW,
        wc.lpszClassName,
        "xray_stage07_sync2_dyn_win",
        WS_POPUP,
        0, 0, 320, 240,
        nullptr, nullptr, wc.hInstance, nullptr
    );
    XRAY_TEST_CHECK(hwnd != nullptr);
    SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);
    XRAY_TEST_CHECK(SetPropA(hwnd, "XRAY_VULKAN_TEST_CAPTURE", HANDLE(1)) != 0);

    {
        vulkan_backend backend;
        XRAY_TEST_CHECK(backend.initialize(hwnd));
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
                SetWindowPos(hwnd, nullptr, 0, 0, 400, 300, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
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
    XRAY_TEST_CHECK(snap.validation_enabled);
    XRAY_TEST_CHECK(snap.validation_errors == 0);
    XRAY_TEST_CHECK(snap.validation_warnings == 0);

    RemovePropA(hwnd, "XRAY_VULKAN_TEST_CAPTURE");
    DestroyWindow(hwnd);
    UnregisterClassA(wc.lpszClassName, wc.hInstance);

    std::cout << "PASS: xray_vulkan_sync2_dynamic_rendering_contract\n";
    return 0;
}
