cmake_minimum_required(VERSION 3.25)

file(GLOB PARTICLE_SOURCES
    "sources/*.cpp"
    "sources/*.h"
    "*.h"
)

# Exclude legacy cooker files that reference APIs removed from the current engine
# (fs::path_info, fs::open_file, resources::query_fs_iterator, etc.)
list(FILTER PARTICLE_SOURCES EXCLUDE REGEX ".*particle_template_cooker\\.cpp$")
list(FILTER PARTICLE_SOURCES EXCLUDE REGEX ".*particle_system_wrapper_cook\\.cpp$")

add_library(xray_particle STATIC ${PARTICLE_SOURCES})

target_include_directories(xray_particle PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../..)
target_include_directories(xray_particle PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/sources)

target_link_libraries(xray_particle PUBLIC xray_core)

target_compile_definitions(xray_particle PRIVATE XRAY_PARTICLE_BUILDING XRAY_ENGINE_BUILDING)
