#define XRAY_LOG_MODULE_INITIATOR "test_vulkan_sdl3_surface_contract"
#include "test_check.hpp"

#include <xray/os_platform.h>
#include <xray/platform_surface.h>
#include <xray/render/vulkan/platform_surface.h>
#include <SDL3/SDL.h>
#include <SDL3/SDL_vulkan.h>

#include <algorithm>
#include <cstring>
#include <iostream>
#include <string_view>
#include <vector>

namespace {

VKAPI_ATTR VkBool32 VKAPI_CALL test_validation_cb(
    VkDebugUtilsMessageSeverityFlagBitsEXT severity,
    VkDebugUtilsMessageTypeFlagsEXT,
    VkDebugUtilsMessengerCallbackDataEXT const* data,
    void* user_data
) {
    if (severity & VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT) {
        auto* err_count = static_cast<int*>(user_data);
        ++(*err_count);
        std::cerr << "Vulkan validation error: " << (data ? data->pMessage : "") << '\n';
    }
    return VK_FALSE;
}

} // namespace

int main() {
    using namespace xray::render::vulkan;
    using xray::platform::native_surface_handle;

    XRAY_TEST_CHECK(SDL_InitSubSystem(SDL_INIT_VIDEO));
    char const* const driver = SDL_GetCurrentVideoDriver();
    std::string_view const driver_sv = driver ? std::string_view(driver) : std::string_view("unknown");

    SDL_Window* win = SDL_CreateWindow(
        "xray_sdl3_vk_smoke",
        320,
        240,
        SDL_WINDOW_VULKAN | SDL_WINDOW_HIDDEN | SDL_WINDOW_RESIZABLE | SDL_WINDOW_HIGH_PIXEL_DENSITY
    );
    XRAY_TEST_CHECK(win != nullptr);

    native_surface_handle const sdl_handle = native_surface_handle::from_sdl_window(win);
    XRAY_TEST_CHECK(sdl_handle.valid());
    XRAY_TEST_CHECK(surface_adapter::is_surface_alive(sdl_handle));
    XRAY_TEST_CHECK(!surface_adapter::is_surface_minimized(sdl_handle));

    u32 initial_w = 0, initial_h = 0;
    XRAY_TEST_CHECK(surface_adapter::query_surface_extent(sdl_handle, initial_w, initial_h));
    XRAY_TEST_CHECK(initial_w > 0 && initial_h > 0);

    surface_adapter::register_sdl_window(sdl_handle, win);
    XRAY_TEST_CHECK(surface_adapter::lookup_sdl_window(sdl_handle) == win);

    std::vector<char const*> exts = surface_adapter::required_instance_extensions();
    bool const has_surface_ext = std::any_of(exts.begin(), exts.end(), [](char const* s) {
        return std::strcmp(s, VK_KHR_SURFACE_EXTENSION_NAME) == 0;
    });
    XRAY_TEST_CHECK(has_surface_ext);

    uint32_t layer_count = 0;
    vkEnumerateInstanceLayerProperties(&layer_count, nullptr);
    std::vector<VkLayerProperties> layers(layer_count);
    vkEnumerateInstanceLayerProperties(&layer_count, layers.data());
    bool const has_val = std::any_of(layers.begin(), layers.end(), [](VkLayerProperties const& l) {
        return std::strcmp(l.layerName, "VK_LAYER_KHRONOS_validation") == 0;
    });

    uint32_t avail_ext_count = 0;
    vkEnumerateInstanceExtensionProperties(nullptr, &avail_ext_count, nullptr);
    std::vector<VkExtensionProperties> avail_exts(avail_ext_count);
    vkEnumerateInstanceExtensionProperties(nullptr, &avail_ext_count, avail_exts.data());
    bool const has_debug_utils = std::any_of(avail_exts.begin(), avail_exts.end(), [](VkExtensionProperties const& e) {
        return std::strcmp(e.extensionName, VK_EXT_DEBUG_UTILS_EXTENSION_NAME) == 0;
    });

    int val_errors = 0;
    VkDebugUtilsMessengerCreateInfoEXT dbg{};
    dbg.sType = VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT;
    dbg.messageSeverity = VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
    dbg.messageType = VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT;
    dbg.pfnUserCallback = &test_validation_cb;
    dbg.pUserData = &val_errors;

    if (has_val && has_debug_utils) {
        exts.push_back(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
    }

    VkApplicationInfo app{};
    app.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    app.pApplicationName = "xray_vulkan_sdl3_surface_contract";
    app.apiVersion = VK_API_VERSION_1_4;

    char const* val_layer = "VK_LAYER_KHRONOS_validation";
    VkInstanceCreateInfo ici{};
    ici.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    ici.pApplicationInfo = &app;
    ici.enabledExtensionCount = static_cast<uint32_t>(exts.size());
    ici.ppEnabledExtensionNames = exts.data();
    if (has_val && has_debug_utils) {
        ici.enabledLayerCount = 1;
        ici.ppEnabledLayerNames = &val_layer;
        ici.pNext = &dbg;
    }

    VkInstance instance = VK_NULL_HANDLE;
    XRAY_TEST_CHECK(vkCreateInstance(&ici, nullptr, &instance) == VK_SUCCESS);
    XRAY_TEST_CHECK(instance != VK_NULL_HANDLE);

    VkSurfaceKHR surface = VK_NULL_HANDLE;
    bool used_sdl = false;
    XRAY_TEST_CHECK(surface_adapter::create_surface(instance, sdl_handle, nullptr, &surface, &used_sdl));
    XRAY_TEST_CHECK(used_sdl);
    XRAY_TEST_CHECK(surface != VK_NULL_HANDLE);

    // Exercise resize and minimize/restore transitions on the live SDL3 Vulkan window
    XRAY_TEST_CHECK(SDL_SetWindowSize(win, 640, 480));
    SDL_SyncWindow(win);
    int w = 0, h = 0;
    XRAY_TEST_CHECK(SDL_GetWindowSizeInPixels(win, &w, &h));
    XRAY_TEST_CHECK(w > 0 && h > 0);
    XRAY_TEST_CHECK(SDL_MinimizeWindow(win));
    XRAY_TEST_CHECK(SDL_RestoreWindow(win));

    vkDestroySurfaceKHR(instance, surface, nullptr);
    surface = VK_NULL_HANDLE;

    // Verify native WSI handle surface creation path (used_sdl == false).
    bool tested_native_wsi = false;
    SDL_PropertiesID const props = SDL_GetWindowProperties(win);
#if XRAY_PLATFORM_WINDOWS
    void* const native_hwnd = SDL_GetPointerProperty(props, SDL_PROP_WINDOW_WIN32_HWND_POINTER, nullptr);
    XRAY_TEST_CHECK(native_hwnd != nullptr);
    native_surface_handle const native_wsi = native_surface_handle::from_win32_hwnd(native_hwnd);
    bool used_sdl_native = true;
    VkSurfaceKHR native_surface = VK_NULL_HANDLE;
    XRAY_TEST_CHECK(surface_adapter::create_surface(instance, native_wsi, nullptr, &native_surface, &used_sdl_native));
    XRAY_TEST_CHECK(!used_sdl_native);
    XRAY_TEST_CHECK(native_surface != VK_NULL_HANDLE);
    vkDestroySurfaceKHR(instance, native_surface, nullptr);
    tested_native_wsi = true;
#elif XRAY_PLATFORM_LINUX
    if (driver_sv == "wayland") {
        void* const wl_display = SDL_GetPointerProperty(props, SDL_PROP_WINDOW_WAYLAND_DISPLAY_POINTER, nullptr);
        void* const wl_surface = SDL_GetPointerProperty(props, SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, nullptr);
        if (wl_display && wl_surface) {
            native_surface_handle const native_wsi = native_surface_handle::from_wayland(
                static_cast<u64>(reinterpret_cast<std::uintptr_t>(wl_display)),
                static_cast<u64>(reinterpret_cast<std::uintptr_t>(wl_surface))
            );
            bool used_sdl_native = true;
            VkSurfaceKHR native_surface = VK_NULL_HANDLE;
            XRAY_TEST_CHECK(surface_adapter::create_surface(instance, native_wsi, nullptr, &native_surface, &used_sdl_native));
            XRAY_TEST_CHECK(!used_sdl_native);
            XRAY_TEST_CHECK(native_surface != VK_NULL_HANDLE);
            vkDestroySurfaceKHR(instance, native_surface, nullptr);
            tested_native_wsi = true;
        }
    } else if (driver_sv == "x11") {
        void* const x11_dpy = SDL_GetPointerProperty(props, SDL_PROP_WINDOW_X11_DISPLAY_POINTER, nullptr);
        Sint64 const x11_win = SDL_GetNumberProperty(props, SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0);
        if (x11_dpy && x11_win) {
            native_surface_handle const native_wsi = native_surface_handle::from_x11_xlib(
                static_cast<u64>(reinterpret_cast<std::uintptr_t>(x11_dpy)),
                static_cast<u64>(x11_win)
            );
            bool used_sdl_native = true;
            VkSurfaceKHR native_surface = VK_NULL_HANDLE;
            XRAY_TEST_CHECK(surface_adapter::create_surface(instance, native_wsi, nullptr, &native_surface, &used_sdl_native));
            XRAY_TEST_CHECK(!used_sdl_native);
            XRAY_TEST_CHECK(native_surface != VK_NULL_HANDLE);
            vkDestroySurfaceKHR(instance, native_surface, nullptr);
            tested_native_wsi = true;
        }
    }
#endif

    vkDestroyInstance(instance, nullptr);

    surface_adapter::unregister_sdl_window(sdl_handle);
    SDL_DestroyWindow(win);
    SDL_QuitSubSystem(SDL_INIT_VIDEO);

    XRAY_TEST_CHECK(val_errors == 0);
    std::cout << "PASS: xray_vulkan_sdl3_surface_contract (driver=" << driver_sv
              << ", pixel_size=" << w << "x" << h
              << ", native_wsi=" << (tested_native_wsi ? "yes" : "no")
              << ", validation=" << (has_val ? "on" : "off") << ")\n";
    return 0;
}
