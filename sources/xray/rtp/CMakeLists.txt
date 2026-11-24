cmake_minimum_required(VERSION 3.25)

set(RTP_SOURCES
    sources/action.cpp
    sources/action_base.cpp
    sources/animation_action.cpp
    sources/animation_grasping_action.cpp
    sources/animation_grasping_controller.cpp
    sources/animation_grasping_params.cpp
    sources/controller.cpp
    sources/grasping_space_params.cpp
    sources/navigation_action.cpp
    sources/navigation_controller.cpp
    sources/navigation_params.cpp
    sources/pch.cpp
    sources/regression_tree.cpp
    sources/resource_cooks.cpp
    sources/rtp_entry_point.cpp
    sources/rtp_library_linkage.cpp
    sources/rtp_world.cpp
    sources/space_params.cpp
    sources/state.cpp
    sources/testing.cpp
    sources/training_regression_tree.cpp
    sources/training_set.cpp
    sources/training_value_function.cpp
    sources/trajectory.cpp
    sources/value_function.cpp
)

add_library(xray_rtp STATIC ${RTP_SOURCES})

target_include_directories(xray_rtp PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)
target_include_directories(xray_rtp PRIVATE sources)

target_link_libraries(xray_rtp PUBLIC xray_core)

target_compile_definitions(xray_rtp PRIVATE XRAY_RTP_BUILDING XRAY_ENGINE_BUILDING)
