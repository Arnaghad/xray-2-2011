cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/debug
set(DEBUG_SOURCES
    sources/bugtrap_win.cpp
    sources/call_stack_win.cpp
    sources/debug.cpp
    sources/debug_win.cpp
    sources/debug_win_shared.cpp
    sources/initialize.cpp
    sources/initialize_win.cpp
    sources/initialize_win_shared.cpp
    sources/macros.cpp
    sources/pch.cpp
    sources/unhandled_win.cpp
    sources/unhandled_win_shared.cpp
)

# Create static library for xray/debug
add_library(xray_debug STATIC ${DEBUG_SOURCES})

# Include directories (root of sources)
target_include_directories(xray_debug PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Preprocessor definitions
target_compile_definitions(xray_debug PRIVATE XRAY_DEBUG_BUILDING)

target_link_libraries(xray_debug PUBLIC dbghelp)
if(NOT MSVC)
    target_compile_options(xray_debug PRIVATE -Werror=return-local-addr)
endif()
