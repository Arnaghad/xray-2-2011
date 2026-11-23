cmake_minimum_required(VERSION 4.3)
project(XRayEngine2 VERSION 2.0 LANGUAGES C CXX)

if(NOT CMAKE_SYSTEM_NAME STREQUAL "Windows")
    message(FATAL_ERROR "The current engine build supports Windows x64 only. Other operating systems are not implemented.")
endif()

option(USE_VULKAN_RENDERER "Use Vulkan renderer backend" ON)
if(NOT USE_VULKAN_RENDERER)
    message(FATAL_ERROR "USE_VULKAN_RENDERER=OFF is not supported by this CMake build; the legacy DX11 targets have not been ported.")
endif()

# Enforce C++26 standard
set(CMAKE_CXX_STANDARD 26)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Enforce 64-bit build
if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "X-Ray Engine 2.0 requires a 64-bit build environment!")
endif()

# Windows desktop toolchain
if(WIN32)
    add_definitions(-DWIN64 -D_WINDOWS)
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
find_package(SDL2 CONFIG REQUIRED)
find_package(mimalloc CONFIG REQUIRED)

# Add subdirectories for engine modules (to be populated)
add_subdirectory(lua)
add_subdirectory(third_party/sol2)
add_subdirectory(cs)
add_subdirectory(xray/editor)
add_subdirectory(xray/logging)
add_subdirectory(xray/vfs)
add_subdirectory(xray/debug)
add_subdirectory(opcode)
add_subdirectory(ode)
add_subdirectory(xray/core)
add_subdirectory(xray/fs)
add_subdirectory(xray/render/vulkan)
add_subdirectory(xray/sound)
add_subdirectory(xray/network)
add_subdirectory(xray/collision)
add_subdirectory(xray/particle)
add_subdirectory(xray/animation_math)
add_subdirectory(xray/animation)
add_subdirectory(xray/input)
add_subdirectory(xray/ui)
add_subdirectory(xray/ai)
add_subdirectory(xray/ai_navigation)
add_subdirectory(xray/physics)
add_subdirectory(xray/rtp)
add_subdirectory(xray/engine)
add_subdirectory(xray/game)
add_subdirectory(xray/stalker2/pc)
add_subdirectory(xray/maya_cook)

# xray_threading and xray_math are currently part of xray_core
add_library(xray_threading INTERFACE)
target_link_libraries(xray_threading INTERFACE xray_core)
add_library(xray_math INTERFACE)
target_link_libraries(xray_math INTERFACE xray_core)
add_library(xray_render INTERFACE)
target_link_libraries(xray_render INTERFACE xray_render_vulkan)
