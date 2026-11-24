#define XRAY_LOG_MODULE_INITIATOR "test_vulkan_sdl3_surface_contract"
#include "test_check.hpp"

#include <xray/os_include.h>
#include <xray/render/vulkan/platform_surface.h>
#include <SDL3/SDL.h>
#include <SDL3/SDL_vulkan.h>

#include <algorithm>
#include <cstring>
#include <iostream>
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

    XRAY_TEST_CHECK(SDL_InitSubSystem(SDL_INIT_VIDEO));
    SDL_Window* win = SDL_CreateWindow(
        "xray_sdl3_vk_smoke",
        320,
        240,
        SDL_WINDOW_VULKAN | SDL_WINDOW_HIDDEN | SDL_WINDOW_RESIZABLE
    );
    XRAY_TEST_CHECK(win != nullptr);

    HWND native_hwnd = static_cast<HWND>(
        SDL_GetPointerProperty(SDL_GetWindowProperties(win), SDL_PROP_WINDOW_WIN32_HWND_POINTER, nullptr)
    );
    XRAY_TEST_CHECK(native_hwnd != nullptr);

    surface_adapter::register_sdl_window(native_hwnd, win);
    XRAY_TEST_CHECK(surface_adapter::lookup_sdl_window(native_hwnd) == win);

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
    XRAY_TEST_CHECK(surface_adapter::create_surface(instance, native_hwnd, nullptr, &surface, &used_sdl));
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
    vkDestroyInstance(instance, nullptr);

    surface_adapter::unregister_sdl_window(native_hwnd);
    XRAY_TEST_CHECK(surface_adapter::lookup_sdl_window(native_hwnd) == nullptr);
    SDL_DestroyWindow(win);
    SDL_QuitSubSystem(SDL_INIT_VIDEO);

    XRAY_TEST_CHECK(val_errors == 0);
    std::cout << "PASS: xray_vulkan_sdl3_surface_contract (pixel_size=" << w << "x" << h
              << ", validation=" << (has_val ? "on" : "off") << ")\n";
    return 0;
}
