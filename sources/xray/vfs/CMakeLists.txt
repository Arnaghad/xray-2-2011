cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/vfs
file(GLOB_RECURSE VFS_SOURCES "*.cpp" "*.h")

# Exclude test files from the library build
# list(FILTER VFS_SOURCES EXCLUDE REGEX "test.*\\.cpp")

# Create static library for xray/vfs
add_library(xray_vfs STATIC ${VFS_SOURCES})
target_link_libraries(xray_vfs PUBLIC sol2::sol2)

# Include directories (root of sources)
target_include_directories(xray_vfs PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Link dependencies
# NOTE: xray_core is NOT linked here to avoid a circular dependency:
#   xray_core (SHARED) --PRIVATE--> xray_vfs (STATIC)
#   xray_vfs (STATIC)  --PUBLIC-->  xray_core (SHARED)
# CMake does not allow cycles involving non-static libraries.
# xray_vfs symbols are resolved when xray_core links them PRIVATE.
target_link_libraries(xray_vfs PUBLIC Boost::filesystem Boost::system)

# Preprocessor definitions to export symbols
target_compile_definitions(xray_vfs PRIVATE XRAY_VFS_BUILDING)
