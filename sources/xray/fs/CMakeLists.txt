cmake_minimum_required(VERSION 3.25)

# Collect all source files for xray/fs
set(FS_COMMON_SOURCES
    sources/asynchronous_device_commands.cpp
    sources/asynchronous_device_interface.cpp
    sources/asynchronous_device_interface_commands.cpp
    sources/case_insensitive_path.cpp
    sources/device_file_system_no_watcher.cpp
    sources/device_file_system_proxy_base.cpp
    sources/device_file_system_watcher.cpp
    sources/device_utils.cpp
    sources/library_linkage.cpp
    sources/native_hdd_file_system.cpp
    sources/path_string_impl.cpp
    sources/path_string_utils.cpp
    sources/pch.cpp
    sources/physical_path_info_iterator.cpp
    sources/simple_asynchronous_interface.cpp
    sources/synchronous_device_interface.cpp
    sources/watcher.cpp
    sources/watcher_notification.cpp
    sources/watcher_subscriber.cpp
    sources/watcher_utils.cpp
)

set(FS_WIN_SOURCES
    sources/folder_watcher_win.cpp
    sources/path_string_utils_win.cpp
    sources/windows_hdd_file_system.cpp
    sources/windows_hdd_file_system_iteration.cpp
)

set(FS_LINUX_SOURCES
    sources/folder_watcher_posix.cpp
    sources/path_string_utils_posix.cpp
    sources/posix_hdd_file_system.cpp
    sources/posix_hdd_file_system_iteration.cpp
)

set(FS_SOURCES ${FS_COMMON_SOURCES})
if(WIN32)
    list(APPEND FS_SOURCES ${FS_WIN_SOURCES})
elseif(UNIX)
    list(APPEND FS_SOURCES ${FS_LINUX_SOURCES})
endif()

# Create shared library for xray/fs
add_library(xray_fs STATIC ${FS_SOURCES})
target_link_libraries(xray_fs PUBLIC sol2::sol2)

# Include directories (root of sources)
target_include_directories(xray_fs PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Link dependencies (xray_core and Boost)
target_link_libraries(xray_fs PUBLIC Boost::filesystem Boost::system)

# Preprocessor definitions to export symbols
target_compile_definitions(xray_fs PRIVATE XRAY_FS_BUILDING)
