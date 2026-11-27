cmake_minimum_required(VERSION 4.3)
project(XRayEngine2 VERSION 2.0 LANGUAGES C CXX)

if(NOT (CMAKE_SYSTEM_NAME STREQUAL "Windows" OR CMAKE_SYSTEM_NAME STREQUAL "Linux"))
    message(FATAL_ERROR "Unsupported operating system '${CMAKE_SYSTEM_NAME}'. Supported platforms: Windows x64 and Linux x64.")
endif()

option(USE_VULKAN_RENDERER "Use Vulkan renderer backend" ON)
if(NOT USE_VULKAN_RENDERER)
    message(FATAL_ERROR "USE_VULKAN_RENDERER=OFF is not supported by this CMake build; the legacy DX11 targets have not been ported.")
endif()

option(XRAY_GAME_PHYSICS_USE_BULLET "Use Bullet physics world in stalker2::game::initialize_physics (reversible rollback to legacy ODE)" ON)
option(XRAY_COLLISION_USE_BULLET_BVH "Use Bullet btOptimizedBvh instead of OPCODE for xray_collision triangle meshes" OFF)
option(XRAY_COOK_FITTER_USE_NATIVE "Use native PCA/Welzl collision primitive fitter instead of WildMagic4" OFF)
option(XRAY_BUILD_EDITOR_HOST "Build .NET/Avalonia EditorHost and xray_editor_world" ON)
option(XRAY_HEADLESS_BUILD "Build only OS-agnostic headless CPU targets (skips renderer, audio, input, engine, game)" OFF)

# Enforce C++26 standard
set(CMAKE_CXX_STANDARD 26)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Enforce 64-bit build
if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "X-Ray Engine 2.0 requires a 64-bit build environment!")
endif()

# Platform & compiler toolchain flags
if(WIN32)
    add_definitions(-DWIN64 -D_WINDOWS)
elseif(UNIX)
    set(CMAKE_POSITION_INDEPENDENT_CODE ON)
    set(CMAKE_BUILD_RPATH "$ORIGIN")
    set(CMAKE_INSTALL_RPATH "$ORIGIN")
endif()

if(MSVC)
    add_compile_options(/W3 /MP /permissive-)
elseif(CMAKE_COMPILER_IS_GNUCXX OR CMAKE_CXX_COMPILER_ID MATCHES "Clang")
    add_compile_options(
        "$<$<COMPILE_LANGUAGE:CXX>:-Wall>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wextra>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-unused-parameter>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-deprecated-declarations>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-empty-body>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-unused-local-typedefs>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-deprecated-variadic-comma-omission>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-misleading-indentation>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-unknown-pragmas>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-conversion-null>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-volatile>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-reorder>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-attributes>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-ignored-qualifiers>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-class-memaccess>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-dangling-else>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-format>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-deprecated-copy>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-sign-compare>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-parentheses>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-extra>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-cast-function-type>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-cast-user-defined>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-write-strings>"
        "$<$<COMPILE_LANGUAGE:CXX>:-Wno-int-in-bool-context>"
    )
    if(CMAKE_COMPILER_IS_GNUCXX)
        add_compile_options("$<$<COMPILE_LANGUAGE:CXX>:-Wno-changes-meaning>")
    endif()
endif()

# Preserve legacy API decorations; the renderer opts into XRAY_RENDER_SHARED.
add_definitions(-DXRAY_STATIC_LIBRARIES)

# Global include directories
include_directories(${CMAKE_CURRENT_SOURCE_DIR})

# Output directories
set(XRAY_BINARY_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/../binaries" CACHE PATH
    "Runtime/library output root; use a distinct directory for each build configuration")
get_filename_component(XRAY_BINARY_OUTPUT_DIRECTORY "${XRAY_BINARY_OUTPUT_DIRECTORY}" ABSOLUTE BASE_DIR "${CMAKE_BINARY_DIR}")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${XRAY_BINARY_OUTPUT_DIRECTORY}")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${XRAY_BINARY_OUTPUT_DIRECTORY}")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${XRAY_BINARY_OUTPUT_DIRECTORY}/lib")

# Find dependencies from the configured toolchain prefix (MSYS2 for GCC).
find_package(Boost CONFIG REQUIRED COMPONENTS filesystem system thread)
if(NOT XRAY_HEADLESS_BUILD)
    find_package(SDL3 CONFIG REQUIRED)
endif()
find_package(mimalloc CONFIG REQUIRED)
find_package(PkgConfig REQUIRED)
pkg_check_modules(BULLET REQUIRED IMPORTED_TARGET GLOBAL bullet)

# Add subdirectories for engine modules (to be populated)
add_subdirectory(lua)
add_subdirectory(third_party/sol2)
add_subdirectory(cs)
if(XRAY_BUILD_EDITOR_HOST AND NOT XRAY_HEADLESS_BUILD)
    add_subdirectory(xray/editor)
endif()
add_subdirectory(xray/logging)
add_subdirectory(xray/vfs)
add_subdirectory(xray/debug)
if(NOT XRAY_COLLISION_USE_BULLET_BVH)
    add_subdirectory(opcode)
endif()
add_subdirectory(ode)
add_subdirectory(xray/core)
add_subdirectory(xray/fs)
if(NOT XRAY_HEADLESS_BUILD)
    add_subdirectory(xray/render/vulkan)
    add_subdirectory(xray/sound)
    add_subdirectory(xray/network)
else()
    add_library(xray_render_headless_stub STATIC
        xray/render/facade/sources/headless_debug_renderer_stub.cpp
    )
    target_include_directories(xray_render_headless_stub PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
    target_link_libraries(xray_render_headless_stub PUBLIC xray_core)
    target_compile_definitions(xray_render_headless_stub PRIVATE XRAY_RENDER_BUILDING XRAY_ENGINE_BUILDING)
endif()
add_subdirectory(xray/collision)
if(NOT XRAY_HEADLESS_BUILD)
    add_subdirectory(xray/particle)
endif()
add_subdirectory(xray/animation_math)
add_subdirectory(xray/animation)
if(NOT XRAY_HEADLESS_BUILD)
    add_subdirectory(xray/input)
    add_subdirectory(xray/ui)
    add_subdirectory(xray/ai)
    add_subdirectory(xray/ai_navigation)
endif()
add_subdirectory(xray/physics)
if(NOT XRAY_HEADLESS_BUILD)
    add_subdirectory(xray/rtp)
    add_subdirectory(xray/engine)
    add_subdirectory(xray/game)
    add_subdirectory(xray/stalker2/pc)
endif()
add_subdirectory(xray/maya_cook)

# xray_threading and xray_math are currently part of xray_core
add_library(xray_threading INTERFACE)
target_link_libraries(xray_threading INTERFACE xray_core)
add_library(xray_math INTERFACE)
target_link_libraries(xray_math INTERFACE xray_core)
if(TARGET xray_render_vulkan)
    add_library(xray_render INTERFACE)
    target_link_libraries(xray_render INTERFACE xray_render_vulkan)
endif()

include(CTest)
option(XRAY_ENABLE_TESTS "Build and register CTest verification targets" ON)
if(BUILD_TESTING AND XRAY_ENABLE_TESTS)
    add_subdirectory(tests)
endif()
