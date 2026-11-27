cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/engine
set(ENGINE_COMMON_SOURCES
    sources/apc.cpp
    sources/console_impl.cpp
    sources/console_impl_input.cpp
    sources/editor_console.cpp
    sources/engine_entry_point.cpp
    sources/engine_library_linkage.cpp
    sources/engine_world.cpp
    sources/engine_world_build.cpp
    sources/engine_world_editor.cpp
    sources/engine_world_file_system_device.cpp
    sources/engine_world_initialize.cpp
    sources/engine_world_interfaces.cpp
    sources/engine_world_logic.cpp
    sources/engine_world_network.cpp
    sources/engine_world_platform.cpp
    sources/engine_world_resources.cpp
    sources/engine_world_sound.cpp
    sources/engine_world_terminate_on_timeout.cpp
    sources/game_console.cpp
    sources/pch.cpp
    sources/rpc.cpp
    sources/terrain_collision_cook.cpp
)

set(ENGINE_WIN_SOURCES
    pc/sources/engine_pc_entry_point.cpp
)

set(ENGINE_SOURCES ${ENGINE_COMMON_SOURCES})
if(WIN32)
    list(APPEND ENGINE_SOURCES ${ENGINE_WIN_SOURCES})
endif()

# Create shared library for xray/engine
add_library(xray_engine SHARED ${ENGINE_SOURCES})
if(WIN32)
    set_target_properties(xray_engine PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS TRUE)
endif()

# Include directories (root of sources)
target_include_directories(xray_engine PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Link dependencies (xray_core, xray_fs)
# Internal static libs are PRIVATE to avoid duplicate symbols with the DLL's import library.
# External deps (Boost, sol2) remain PUBLIC for consumers that need them.
target_link_libraries(xray_engine PRIVATE 
    xray_core 
    xray_sound
    xray_network
    xray_collision
    xray_terrain_collision
    xray_threading
    xray_math
    xray_animation
    xray_physics
    xray_render_vulkan
    SDL3::SDL3
)
target_link_libraries(xray_engine PUBLIC 
    Boost::system 
    Boost::thread 
    sol2::sol2
)

# Preprocessor definitions to export symbols
target_compile_definitions(xray_engine PRIVATE XRAY_ENGINE_BUILDING)
