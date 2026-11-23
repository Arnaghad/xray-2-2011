cmake_minimum_required(VERSION 3.24)

add_library(xray_animation_math INTERFACE)
target_include_directories(xray_animation_math INTERFACE "${CMAKE_CURRENT_SOURCE_DIR}/../..")
target_compile_features(xray_animation_math INTERFACE cxx_std_23)
