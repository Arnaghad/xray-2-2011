cmake_minimum_required(VERSION 3.25)

set(PARTICLE_SOURCES
    sources/bounded_particle_template_cook.cpp
    sources/color_matrix.cpp
    sources/curve_line.cpp
    sources/evaluate_type.cpp
    sources/help_functions.cpp
    sources/particle_actions.cpp
    sources/particle_beam_emitter_instance.cpp
    sources/particle_domains.cpp
    sources/particle_emitter.cpp
    sources/particle_emitter_instance.cpp
    sources/particle_entry_point.cpp
    sources/particle_library_linkage.cpp
    sources/particle_system.cpp
    sources/particle_system_instance.cpp
    sources/particle_system_instance_cook.cpp
    sources/particle_system_instance_impl.cpp
    sources/particle_world.cpp
    sources/particle_world_cooker.cpp
    sources/pch.cpp
    sources/preview_particle_system.cpp
    sources/preview_particle_system_cooker.cpp
    sources/preview_particle_system_instance.cpp
)

add_library(xray_particle STATIC ${PARTICLE_SOURCES})

target_include_directories(xray_particle PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../..)
target_include_directories(xray_particle PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/sources)

target_link_libraries(xray_particle PUBLIC xray_core)

target_compile_definitions(xray_particle PRIVATE XRAY_PARTICLE_BUILDING XRAY_ENGINE_BUILDING)
