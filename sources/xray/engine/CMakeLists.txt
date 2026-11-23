cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/engine
file(GLOB_RECURSE ENGINE_SOURCES "*.cpp" "*.h")

# Create shared library for xray/engine
add_library(xray_engine SHARED ${ENGINE_SOURCES})
set_target_properties(xray_engine PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS TRUE)

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
)
target_link_libraries(xray_engine PUBLIC 
    Boost::system 
    Boost::thread 
    sol2::sol2
)

# Preprocessor definitions to export symbols
target_compile_definitions(xray_engine PRIVATE XRAY_ENGINE_BUILDING)
