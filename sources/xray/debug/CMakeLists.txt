cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/debug
file(GLOB_RECURSE DEBUG_SOURCES "*.cpp" "*.h")
list(FILTER DEBUG_SOURCES EXCLUDE REGEX "(_multi)\\.cpp")

# Create static library for xray/debug
add_library(xray_debug STATIC ${DEBUG_SOURCES})

# Include directories (root of sources)
target_include_directories(xray_debug PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Preprocessor definitions
target_compile_definitions(xray_debug PRIVATE XRAY_DEBUG_BUILDING)
