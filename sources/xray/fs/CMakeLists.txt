cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/fs
file(GLOB_RECURSE FS_SOURCES "*.cpp" "*.h")
list(FILTER FS_SOURCES EXCLUDE REGEX "simple_synchronous_interace\.cpp")

# Create shared library for xray/fs
add_library(xray_fs STATIC ${FS_SOURCES})
target_link_libraries(xray_fs PUBLIC sol2::sol2)

# Include directories (root of sources)
target_include_directories(xray_fs PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Link dependencies (xray_core and Boost)
target_link_libraries(xray_fs PUBLIC Boost::filesystem Boost::system)

# Preprocessor definitions to export symbols
target_compile_definitions(xray_fs PRIVATE XRAY_FS_BUILDING)
