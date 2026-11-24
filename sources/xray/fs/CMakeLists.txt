cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/fs
set(FS_SOURCES
    sources/asynchronous_device_commands.cpp
    sources/asynchronous_device_interface.cpp
    sources/asynchronous_device_interface_commands.cpp
    sources/device_file_system_no_watcher.cpp
    sources/device_file_system_proxy_base.cpp
    sources/device_file_system_watcher.cpp
    sources/device_utils.cpp
    sources/folder_watcher.cpp
    sources/library_linkage.cpp
    sources/path_string_impl.cpp
    sources/path_string_utils.cpp
    sources/path_string_utils_win.cpp
    sources/pch.cpp
    sources/physical_path_info_iterator.cpp
    sources/simple_asynchronous_interface.cpp
    sources/synchronous_device_interface.cpp
    sources/watcher.cpp
    sources/watcher_notification.cpp
    sources/watcher_subscriber.cpp
    sources/watcher_utils.cpp
    sources/windows_hdd_file_system.cpp
    sources/windows_hdd_file_system_iteration.cpp
)

# Create shared library for xray/fs
add_library(xray_fs STATIC ${FS_SOURCES})
target_link_libraries(xray_fs PUBLIC sol2::sol2)

# Include directories (root of sources)
target_include_directories(xray_fs PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Link dependencies (xray_core and Boost)
target_link_libraries(xray_fs PUBLIC Boost::filesystem Boost::system)

# Preprocessor definitions to export symbols
target_compile_definitions(xray_fs PRIVATE XRAY_FS_BUILDING)
