cmake_minimum_required(VERSION 3.25)

# Collect all source files
file(GLOB_RECURSE CORE_SOURCES "*.cpp" "*.h")
list(FILTER CORE_SOURCES EXCLUDE REGEX "(game_resources_manager_test|managed_allocator_test_win|resources_managed_allocator_test_win|resources_managed_allocator_test_stub|core_entry_point_multi|fs_watcher_multi|fs_impl_nodes|fs_impl_mount_disk|fs_impl_mount_disk_win|fs_file_system_impl_mount_db|fs_impl_replication|fs_impl_unmount|fs_impl_unpacking|fs_impl_utils|fs_impl_watcher|fs_nodes|memory_crt_allocator_multi|memory_process_allocator_multi|resources_query_mount|debug\\.cpp|logging\\.cpp)\\.cpp")





# No need to exclude console files since they have been physically removed

add_library(xray_core SHARED ${CORE_SOURCES})

# DLL CRT startup allocates before the engine can initialize its allocators.
# Preserve the explicit allocator/ogg bridges, but do not override this DLL's CRT.
set_property(SOURCE sources/memory_override_operators.cpp APPEND PROPERTY
    COMPILE_DEFINITIONS XRAY_DISABLE_CRT_OVERRIDES=1)

# MSVC: auto-export all symbols from the DLL
if(MSVC)
    set_target_properties(xray_core PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS TRUE)
endif()

# Include directories
target_include_directories(xray_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

# Link dependencies
# Core exposes the public debug API to its DLL consumers. Release can remove
# the only internal reference to an API's object file (e.g. enable_fpe and
# change_bugtrap_usage share one object), so ordinary archive extraction is
# insufficient to preserve that API.
target_link_libraries(xray_core PRIVATE "$<LINK_LIBRARY:WHOLE_ARCHIVE,xray_debug>" xray_logging)
target_link_libraries(xray_core PRIVATE cs_script cs_core xray_fs xray_vfs)
target_link_libraries(xray_core PUBLIC sol2::sol2 Boost::filesystem Boost::system Boost::thread)
target_link_libraries(xray_core PRIVATE lua mimalloc winmm shlwapi)

# Preprocessor definitions
target_compile_definitions(xray_core PRIVATE XRAY_CORE_BUILDING XRAY_LOGGING_BUILDING XRAY_DEBUG_BUILDING)

# Keep XRAY_STATIC_LIBRARIES defined (from root CMakeLists.txt) so all other
# module API macros (XRAY_VFS_API, XRAY_FS_API, etc.) remain empty.
# On MSVC: WINDOWS_EXPORT_ALL_SYMBOLS exports everything from the DLL.
# On MinGW/GCC: symbols are exported by default from a DLL.
