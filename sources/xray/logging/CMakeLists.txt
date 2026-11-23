cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/logging
file(GLOB_RECURSE LOGGING_SOURCES "*.cpp" "*.h")
list(FILTER LOGGING_SOURCES EXCLUDE REGEX "(_multi)\\.cpp")

# Create static library for xray/logging
add_library(xray_logging STATIC ${LOGGING_SOURCES})

# Include directories
target_include_directories(xray_logging PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Preprocessor definitions
target_compile_definitions(xray_logging PRIVATE XRAY_LOGGING_BUILDING)
