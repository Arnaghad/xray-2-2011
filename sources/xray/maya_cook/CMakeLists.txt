cmake_minimum_required(VERSION 3.24)

set(_maya_cook_standalone OFF)
if(CMAKE_SOURCE_DIR STREQUAL CMAKE_CURRENT_SOURCE_DIR)
    set(_maya_cook_standalone ON)
    project(xray_maya_cook LANGUAGES CXX)
    set(CMAKE_CXX_STANDARD 26)
    set(CMAKE_CXX_STANDARD_REQUIRED ON)
    set(CMAKE_CXX_EXTENSIONS OFF)
endif()

if(NOT TARGET xray_animation_math)
    add_subdirectory("${CMAKE_CURRENT_SOURCE_DIR}/../animation_math" "${CMAKE_CURRENT_BINARY_DIR}/animation_math")
endif()

set(MESHOPT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../third_party/meshoptimizer")
if(NOT EXISTS "${MESHOPT_DIR}/meshoptimizer.h")
    message(FATAL_ERROR "Missing vendored meshoptimizer headers at ${MESHOPT_DIR}")
endif()

set(WM4_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../third_party/wildmagic4/LibFoundation")
if(NOT EXISTS "${WM4_DIR}/Containment/Wm4ContBox3.h")
    message(FATAL_ERROR "Missing vendored WildMagic4 containment sources at ${WM4_DIR}")
endif()

add_library(xray_wildmagic4 STATIC
    "${WM4_DIR}/Approximation/Wm4ApprGaussPointsFit2.cpp"
    "${WM4_DIR}/Approximation/Wm4ApprGaussPointsFit3.cpp"
    "${WM4_DIR}/ComputationalGeometry/Wm4ConvexHull.cpp"
    "${WM4_DIR}/ComputationalGeometry/Wm4ConvexHull1.cpp"
    "${WM4_DIR}/ComputationalGeometry/Wm4ConvexHull2.cpp"
    "${WM4_DIR}/ComputationalGeometry/Wm4ConvexHull3.cpp"
    "${WM4_DIR}/ComputationalGeometry/Wm4HullEdge2.cpp"
    "${WM4_DIR}/ComputationalGeometry/Wm4HullTriangle3.cpp"
    "${WM4_DIR}/ComputationalGeometry/Wm4Query.cpp"
    "${WM4_DIR}/Containment/Wm4ContBox2.cpp"
    "${WM4_DIR}/Containment/Wm4ContBox3.cpp"
    "${WM4_DIR}/Containment/Wm4ContSphere3.cpp"
    "${WM4_DIR}/Mathematics/Wm4Math.cpp"
    "${WM4_DIR}/Mathematics/Wm4Matrix2.cpp"
    "${WM4_DIR}/Mathematics/Wm4Matrix3.cpp"
    "${WM4_DIR}/Mathematics/Wm4Matrix4.cpp"
    "${WM4_DIR}/Mathematics/Wm4Quaternion.cpp"
    "${WM4_DIR}/Mathematics/Wm4Vector2.cpp"
    "${WM4_DIR}/Mathematics/Wm4Vector3.cpp"
    "${WM4_DIR}/Mathematics/Wm4Vector4.cpp"
    "${WM4_DIR}/NumericalAnalysis/Wm4Eigen.cpp"
    "${WM4_DIR}/System/Wm4Memory.cpp"
    "${WM4_DIR}/System/Wm4System.cpp"
)

target_include_directories(xray_wildmagic4 PUBLIC
    "${WM4_DIR}"
    "${WM4_DIR}/Approximation"
    "${WM4_DIR}/ComputationalGeometry"
    "${WM4_DIR}/Containment"
    "${WM4_DIR}/Mathematics"
    "${WM4_DIR}/Meshes"
    "${WM4_DIR}/NumericalAnalysis"
    "${WM4_DIR}/System"
)
target_compile_features(xray_wildmagic4 PUBLIC cxx_std_23)
if(MSVC)
    target_compile_options(xray_wildmagic4 PRIVATE /W4)
else()
    target_compile_options(xray_wildmagic4 PRIVATE -Wall -Wextra)
endif()

add_library(xray_maya_cook STATIC
    sources/animation_cooker.cpp
    sources/cook_api.cpp
    sources/coordinate_transform.cpp
    sources/exchange_schema.cpp
    sources/json_value.cpp
    sources/lua_and_binary_config.cpp
    sources/mesh_mender.cpp
    sources/model_cook_common.cpp
    sources/physics_cooker.cpp
    sources/skeleton_cooker.cpp
    sources/skinned_model_cooker.cpp
    sources/static_model_cooker.cpp
    sources/vfs_archive.cpp
    "${MESHOPT_DIR}/allocator.cpp"
    "${MESHOPT_DIR}/indexanalyzer.cpp"
    "${MESHOPT_DIR}/overdrawoptimizer.cpp"
    "${MESHOPT_DIR}/vcacheoptimizer.cpp"
    "${MESHOPT_DIR}/vfetchoptimizer.cpp"
)

target_link_libraries(xray_maya_cook PRIVATE xray_wildmagic4 PUBLIC xray_animation_math)

target_include_directories(xray_maya_cook
    PUBLIC
        "${CMAKE_CURRENT_SOURCE_DIR}/include"
    PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/sources"
        "${MESHOPT_DIR}"
)

target_compile_features(xray_maya_cook PUBLIC cxx_std_23)
if(MSVC)
    target_compile_options(xray_maya_cook PRIVATE /W4)
else()
    target_compile_options(xray_maya_cook PRIVATE -Wall -Wextra)
endif()

add_executable(xray_maya_cook_cli sources/cli_main.cpp)
target_link_libraries(xray_maya_cook_cli PRIVATE xray_maya_cook)

# Both entry points use the same implementation and wire format.
add_executable(xray_asset_cook_cli sources/cli_main.cpp)
target_link_libraries(xray_asset_cook_cli PRIVATE xray_maya_cook)
