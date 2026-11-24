cmake_minimum_required(VERSION 3.25)

project(xray_render_vulkan)

find_package(Vulkan 1.4 REQUIRED)
find_package(ZLIB REQUIRED)
set(SPIRV_REFLECT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../../third_party/spirv_reflect")
add_library(xray_spirv_reflect STATIC "${SPIRV_REFLECT_ROOT}/spirv_reflect.c")
target_include_directories(xray_spirv_reflect PUBLIC "${SPIRV_REFLECT_ROOT}")

set(VULKAN_FILES
    sources/material_contract.cpp
    sources/pch.cpp
    sources/vulkan_backend.cpp
    sources/vulkan_bc_decode.cpp
    sources/vulkan_bloom.cpp
    sources/vulkan_capture.cpp
    sources/vulkan_debug_renderer.cpp
    sources/vulkan_decal_renderer.cpp
    sources/vulkan_deferred_lighting.cpp
    sources/vulkan_device_memory.cpp
    sources/vulkan_diagnostics.cpp
    sources/vulkan_frustum.cpp
    sources/vulkan_fxaa.cpp
    sources/vulkan_gbuffer_contract.cpp
    sources/vulkan_gbuffer_renderer.cpp
    sources/vulkan_lights.cpp
    sources/vulkan_material_assets.cpp
    sources/vulkan_material_cook.cpp
    sources/vulkan_material_resources.cpp
    sources/vulkan_memory.cpp
    sources/vulkan_model_cook.cpp
    sources/vulkan_model_properties.cpp
    sources/vulkan_model_resources.cpp
    sources/vulkan_original_material.cpp
    sources/vulkan_original_model_cook.cpp
    sources/vulkan_original_source_config.cpp
    sources/vulkan_original_static_assets.cpp
    sources/vulkan_particle_material_cook.cpp
    sources/vulkan_particle_renderer.cpp
    sources/vulkan_particle_snapshot.cpp
    sources/vulkan_performance.cpp
    sources/vulkan_platform_surface.cpp
    sources/vulkan_render_passes.cpp
    sources/vulkan_resource_operations.cpp
    sources/vulkan_scene_decals.cpp
    sources/vulkan_scene_particles.cpp
    sources/vulkan_scene_resources.cpp
    sources/vulkan_scene_runtime.cpp
    sources/vulkan_scene_shadows.cpp
    sources/vulkan_screen_effects.cpp
    sources/vulkan_selection_renderer.cpp
    sources/vulkan_skeletal_resources.cpp
    sources/vulkan_sky_renderer.cpp
    sources/vulkan_sky_resources.cpp
    sources/vulkan_smoke_geometry.cpp
    sources/vulkan_smoke_materials.cpp
    sources/vulkan_static_renderer.cpp
    sources/vulkan_taa.cpp
    sources/vulkan_terrain_bindings.cpp
    sources/vulkan_terrain_cook.cpp
    sources/vulkan_terrain_normalization.cpp
    sources/vulkan_terrain_renderer.cpp
    sources/vulkan_terrain_resources.cpp
    sources/vulkan_texture_cook.cpp
    sources/vulkan_texture_options.cpp
    sources/vulkan_texture_reload.cpp
    sources/vulkan_tga_assets.cpp
    sources/vulkan_ui_renderer.cpp
    sources/vulkan_ui_text.cpp
    sources/vulkan_vegetation.cpp
    sources/vulkan_vegetation_cook.cpp
    sources/vulkan_world.cpp
)

add_library(xray_terrain_collision STATIC sources/vulkan_terrain_collision.cpp)
target_include_directories(xray_terrain_collision PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/../../.." PRIVATE sources)
target_link_libraries(xray_terrain_collision PUBLIC xray_collision)

set(FACADE_FILES
    ../facade/sources/debug_draw_lines_command.cpp
    ../facade/sources/debug_draw_triangles_command.cpp
    ../facade/sources/debug_renderer.cpp
    ../facade/sources/editor_draw_3d_screen_lines_command.cpp
    ../facade/sources/editor_draw_screen_lines_command.cpp
    ../facade/sources/editor_renderer.cpp
    ../facade/sources/engine_renderer.cpp
    ../facade/sources/functor_command.cpp
    ../facade/sources/game_renderer.cpp
    ../facade/sources/one_way_render_channel.cpp
    ../facade/sources/pch.cpp
    ../facade/sources/render_facade_entry_point.cpp
    ../facade/sources/render_facade_library_linkage.cpp
    ../facade/sources/render_facade_world.cpp
    ../facade/sources/render_stage_types.cpp
    ../facade/sources/scene_renderer.cpp
    ../facade/sources/terrain_cell_update_buffer_command.cpp
    ../facade/sources/terrain_exchange_texture_command.cpp
    ../facade/sources/ui_draw_vertices_command.cpp
    ../facade/sources/ui_renderer.cpp
    ../facade/sources/update_model_vertex_buffer_command.cpp
    ../facade/sources/update_skeleton_command.cpp
    ../facade/sources/vertex_input_type.cpp
)

# Include RHI layer (which is technically in core, but part of the backend)
set(RHI_FILES
    ../core/sources/rhi/rhi_device.cpp
    ../core/sources/rhi/shader_compiler.cpp
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
target_link_libraries(xray_render_vulkan PRIVATE xray_particle xray_spirv_reflect lua ZLIB::ZLIB SDL3::SDL3)

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
    target_compile_options(xray_render_vulkan PRIVATE
        -Wall -Wextra -Wstrict-aliasing=2 -Werror=strict-aliasing -Werror=return-local-addr -Wformat
    )
endif()
