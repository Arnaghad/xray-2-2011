cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/logging
set(LOGGING_SOURCES
    sources/filter_tree.cpp
    sources/filter_tree_node.cpp
    sources/format_specifier.cpp
    sources/globals.cpp
    sources/helper.cpp
    sources/helper_win.cpp
    sources/log_file.cpp
    sources/logging.cpp
    sources/logging_library_linkage.cpp
    sources/logging_win.cpp
    sources/path_parts.cpp
    sources/pch.cpp
)

# Create static library for xray/logging
add_library(xray_logging STATIC ${LOGGING_SOURCES})

# Include directories
target_include_directories(xray_logging PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Preprocessor definitions
target_compile_definitions(xray_logging PRIVATE XRAY_LOGGING_BUILDING)
