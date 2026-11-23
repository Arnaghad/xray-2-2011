cmake_minimum_required(VERSION 3.25)

project(xray_render_vulkan)

find_package(Vulkan 1.4 REQUIRED)
find_package(ZLIB REQUIRED)
set(SPIRV_REFLECT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../../third_party/spirv_reflect")
add_library(xray_spirv_reflect STATIC "${SPIRV_REFLECT_ROOT}/spirv_reflect.c")
target_include_directories(xray_spirv_reflect PUBLIC "${SPIRV_REFLECT_ROOT}")

file(GLOB VULKAN_FILES CONFIGURE_DEPENDS
    "sources/*.cpp"
    "*.h"
)
list(REMOVE_ITEM VULKAN_FILES "${CMAKE_CURRENT_SOURCE_DIR}/sources/vulkan_terrain_collision.cpp")

add_library(xray_terrain_collision STATIC sources/vulkan_terrain_collision.cpp)
target_include_directories(xray_terrain_collision PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/../../.." PRIVATE sources)
target_link_libraries(xray_terrain_collision PUBLIC xray_collision)

file(GLOB FACADE_FILES CONFIGURE_DEPENDS
    "../facade/sources/*.cpp"
    "../facade/sources/*.h"
)

# Include RHI layer (which is technically in core, but part of the backend)
file(GLOB RHI_FILES CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/../core/sources/rhi/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/../core/sources/rhi/*.h"
)

# The active facade and world have one owner. Legacy base platform/renderers
# implement a different facade and must not compete for the same symbols.
add_library(xray_render_vulkan SHARED ${VULKAN_FILES} ${FACADE_FILES} ${RHI_FILES}
    ../base/sources/vulkan_stubs.cpp
)

target_include_directories(xray_render_vulkan PRIVATE 
    "sources"
)

target_include_directories(xray_render_vulkan PUBLIC 
    "${CMAKE_CURRENT_SOURCE_DIR}/../../.."
)

target_link_libraries(xray_render_vulkan PUBLIC Vulkan::Vulkan xray_core)
# One DLL owns scene and external particle cook registries in production/tests.
target_link_libraries(xray_render_vulkan PRIVATE xray_particle xray_spirv_reflect lua ZLIB::ZLIB)

# Vulkan::Vulkan supplies headers/import symbols; Windows loads the system runtime.

target_compile_definitions(xray_render_vulkan
    PUBLIC USE_VULKAN_RENDERER=1 XRAY_RENDER_SHARED=1
    PRIVATE XRAY_RENDER_VULKAN_BUILDING XRAY_RENDER_BASE_BUILDING XRAY_RENDER_BUILDING
        XRAY_VULKAN_SMOKE_SHADER_DIR="${CMAKE_CURRENT_SOURCE_DIR}/assets/basic"
        XRAY_VULKAN_ASSET_SHADER_DIR="${CMAKE_CURRENT_SOURCE_DIR}/assets"
)
target_compile_features(xray_render_vulkan PUBLIC cxx_std_26)
add_executable(xray_shader_compiler ../../tools/shader_compiler/main.cpp)
target_link_libraries(xray_shader_compiler PRIVATE xray_render_vulkan)
if(MSVC)
    set_target_properties(xray_render_vulkan PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS TRUE)
elseif(MINGW)
    target_link_options(xray_render_vulkan PRIVATE -Wl,--export-all-symbols)
endif()

if (MSVC)
    target_compile_options(xray_render_vulkan PRIVATE /W4)
else()
    target_compile_options(xray_render_vulkan PRIVATE -Wall -Wextra)
endif()
