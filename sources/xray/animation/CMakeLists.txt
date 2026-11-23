cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE ANIMATION_SOURCES "sources/*.cpp" "sources/*.h" "*.h")

# Exclude problematic files
list(FILTER ANIMATION_SOURCES EXCLUDE REGEX "fermi_dirac_interpolator\\.cpp")
list(FILTER ANIMATION_SOURCES EXCLUDE REGEX "mixing_n_ary_tree_transition_node\\.cpp")
list(FILTER ANIMATION_SOURCES EXCLUDE REGEX "mixing_n_ary_tree_transition_constructor\\.cpp")
list(FILTER ANIMATION_SOURCES EXCLUDE REGEX "test_animation_player\\.cpp")
list(FILTER ANIMATION_SOURCES EXCLUDE REGEX "test_mixing_animation_group\\.cpp")
list(FILTER ANIMATION_SOURCES EXCLUDE REGEX "mixer_test\\.cpp")
list(FILTER ANIMATION_SOURCES EXCLUDE REGEX "bone_mixer\\.cpp")
list(FILTER ANIMATION_SOURCES EXCLUDE REGEX "(^|/)mixer\\.cpp$")
list(FILTER ANIMATION_SOURCES EXCLUDE REGEX "mixing_animation_node\\.cpp")
list(FILTER ANIMATION_SOURCES EXCLUDE REGEX "mixing_n_ary_tree_builder\\.cpp")

add_library(xray_animation STATIC ${ANIMATION_SOURCES})

target_include_directories(xray_animation PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../ 
PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

target_link_libraries(xray_animation PUBLIC xray_core xray_animation_math)

target_compile_definitions(xray_animation PRIVATE XRAY_ANIMATION_BUILDING XRAY_ENGINE_BUILDING)
