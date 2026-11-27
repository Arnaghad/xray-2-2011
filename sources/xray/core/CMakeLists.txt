cmake_minimum_required(VERSION 3.25)

# Collect all source files
set(CORE_COMMON_SOURCES
    sources/buffer_string.cpp
    sources/build_extensions.cpp
    sources/command_line.cpp
    sources/compressor_ppmd.cpp
    sources/compressors_test.cpp
    sources/configs_binary_config.cpp
    sources/configs_binary_config_cook.cpp
    sources/configs_binary_config_value.cpp
    sources/configs_checked_binary.cpp
    sources/configs_ltx_config.cpp
    sources/configs_ltx_config_cook.cpp
    sources/configs_lua_config.cpp
    sources/configs_lua_config_cook.cpp
    sources/console_command.cpp
    sources/console_command_processor.cpp
    sources/core_debug_engine.cpp
    sources/core_entry_point.cpp
    sources/core_library_linkage.cpp
    sources/debug_platform.cpp
    sources/fs_file_system.cpp
    sources/fs_file_system_init.cpp
    sources/fs_file_system_impl_disk_ops.cpp
    sources/fs_file_system_impl_mount_disk.cpp
    sources/fs_file_system_impl_nodes.cpp
    sources/fs_file_system_impl_replication.cpp
    sources/fs_file_system_impl_saving_db.cpp
    sources/fs_file_system_impl_unmount.cpp
    sources/fs_file_system_impl_unpacking.cpp
    sources/fs_file_system_impl_utils.cpp
    sources/fs_file_system_impl_watcher.cpp
    sources/fs_file_system_nodes_impl.cpp
    sources/fs_helper.cpp
    sources/fs_impl_finding_async_no_pin.cpp
    sources/fs_impl_finding_async_pin.cpp
    sources/fs_impl_finding_async_pin_tree.cpp
    sources/fs_impl_finding_disk.cpp
    sources/fs_impl_finding_helper.cpp
    sources/fs_impl_finding_pin.cpp
    sources/fs_impl_finding_sub_fat.cpp
    sources/fs_impl_finding_sync.cpp
    sources/fs_impl_mount_db.cpp
    sources/fs_impl_saving_db.cpp
    sources/fs_impl_saving_file.cpp
    sources/fs_impl_saving_info_tree.cpp
    sources/fs_impl_saving_node.cpp
    sources/fs_path.cpp
    sources/fs_sub_fat_cook.cpp
    sources/fs_sub_fat_resource.cpp
    sources/fs_utils.cpp
    sources/fs_utils_no_watcher.cpp
    sources/fs_utils_path.cpp
    sources/fs_utils_watcher.cpp
    sources/game_resman.cpp
    sources/game_resman_free.cpp
    sources/game_resman_interface.cpp
    sources/game_resman_out_of_memory.cpp
    sources/game_resman_quality_decrease.cpp
    sources/game_resman_quality_increase.cpp
    sources/game_resman_release.cpp
    sources/game_resman_satisfaction_tree.cpp
    sources/game_resman_sorting.cpp
    sources/game_resman_test.cpp
    sources/game_resman_test_allocator.cpp
    sources/game_resman_test_resource.cpp
    sources/game_resman_test_resource_cook.cpp
    sources/game_resman_test_resource_with_quality.cpp
    sources/game_resman_test_resource_with_quality_cook.cpp
    sources/game_resman_test_utils.cpp
    sources/geometry_primitives.cpp
    sources/geometry_utils.cpp
    sources/input_event_queue.cpp
    sources/managed_allocator.cpp
    sources/managed_allocator_base.cpp
    sources/managed_node.cpp
    sources/managed_node_owner.cpp
    sources/math_aabb.cpp
    sources/math_aabb_plane.cpp
    sources/math_aabb_ray_intersection.cpp
    sources/math_constants.cpp
    sources/math_convex.cpp
    sources/math_cuboid.cpp
    sources/math_curve.cpp
    sources/math_cylinder.cpp
    sources/math_float3.cpp
    sources/math_float4x4.cpp
    sources/math_frustum.cpp
    sources/math_functions.cpp
    sources/math_obb.cpp
    sources/math_permutations.cpp
    sources/math_quaternion.cpp
    sources/math_sphere.cpp
    sources/math_tests.cpp
    sources/memory.cpp
    sources/memory_base_allocator.cpp
    sources/memory_chunk_reader.cpp
    sources/memory_crt_allocator.cpp
    sources/memory_doug_lea_allocator.cpp
    sources/memory_doug_lea_mt_allocator.cpp
    sources/memory_guard.cpp
    sources/memory_leak_detector.cpp
    sources/memory_mimalloc_allocator.cpp
    sources/memory_monitor.cpp
    sources/memory_override_operators.cpp
    sources/memory_process_allocator.cpp
    sources/memory_pthreads3_allocator.cpp
    sources/memory_reader.cpp
    sources/memory_stack_allocator.cpp
    sources/memory_stack_arena_allocator.cpp
    sources/memory_stream.cpp
    sources/memory_video.cpp
    sources/memory_writer.cpp
    sources/os_functions.cpp
    sources/pch.cpp
    sources/profiler.cpp
    sources/resources_allocator.cpp
    sources/resources_command.cpp
    sources/resources_cook_base.cpp
    sources/resources_cook_classes.cpp
    sources/resources_device_manager.cpp
    sources/resources_device_manager_thread.cpp
    sources/resources_fs.cpp
    sources/resources_fs_impl.cpp
    sources/resources_fs_iterator.cpp
    sources/resources_fs_iterator_functions.cpp
    sources/resources_fs_task.cpp
    sources/resources_fs_task_composite.cpp
    sources/resources_fs_task_erase.cpp
    sources/resources_fs_task_iterator.cpp
    sources/resources_fs_task_mount.cpp
    sources/resources_hdd_manager.cpp
    sources/resources_impl.cpp
    sources/resources_intrusive_base.cpp
    sources/resources_managed_allocator.cpp
    sources/resources_managed_resource.cpp
    sources/resources_manager.cpp
    sources/resources_manager_allocation.cpp
    sources/resources_manager_cook.cpp
    sources/resources_manager_deallocation.cpp
    sources/resources_manager_debug.cpp
    sources/resources_manager_helper.cpp
    sources/resources_manager_new_queries.cpp
    sources/resources_manager_user_thread.cpp
    sources/resources_manager_watcher.cpp
    sources/resources_memory.cpp
    sources/resources_memory_allocator.cpp
    sources/resources_mount_ptr.cpp
    sources/resources_mounts.cpp
    sources/resources_name_registry_entry.cpp
    sources/resources_queries_result.cpp
    sources/resources_query_fs_iterator.cpp
    sources/resources_query_result.cpp
    sources/resources_query_result_allocation.cpp
    sources/resources_query_result_cache.cpp
    sources/resources_query_result_cook.cpp
    sources/resources_query_result_finalization.cpp
    sources/resources_query_result_initialization.cpp
    sources/resources_query_result_path.cpp
    sources/resources_query_result_replication.cpp
    sources/resources_query_result_requery.cpp
    sources/resources_resource_base.cpp
    sources/resources_resource_children.cpp
    sources/resources_resource_flags.cpp
    sources/resources_resource_quality.cpp
    sources/resources_resource_reconstruction_info.cpp
    sources/resources_save_generated_data.cpp
    sources/resources_test.cpp
    sources/resources_unmanaged_resource.cpp
    sources/resources_vfs_associations.cpp
    sources/script_engine_wrapper.cpp
    sources/strings_detail_tuples.cpp
    sources/strings_functions.cpp
    sources/strings_shared_manager.cpp
    sources/strings_shared_profile.cpp
    sources/strings_stream.cpp
    sources/tasks_allocator.cpp
    sources/tasks_aware_event.cpp
    sources/tasks_aware_functions.cpp
    sources/tasks_aware_mutex.cpp
    sources/tasks_manager.cpp
    sources/tasks_task.cpp
    sources/tasks_thread_pool.cpp
    sources/tasks_thread_pool_utils.cpp
    sources/tasks_type.cpp
    sources/testing_impl.cpp
    sources/text_tree.cpp
    sources/text_tree_item.cpp
    sources/threading_active_object.cpp
    sources/threading_event.cpp
    sources/threading_functions.cpp
    sources/threading_mutex.cpp
    sources/threading_reader_writer_lock.cpp
    sources/threading_test.cpp
    sources/unmanaged_allocation_cook.cpp
    sources/vfs_sub_fat_cook.cpp
    sources/vfs_sub_fat_resource.cpp
    sources/virtual_alloc.cpp
)

set(CORE_WIN_SOURCES
    sources/core_entry_point_win.cpp
    sources/core_entry_point_win_shared.cpp
    sources/delayhlp.cpp
    sources/fs_file_system_impl_mount_disk_win.cpp
    sources/fs_folder_watcher_win.cpp
    sources/fs_path_win.cpp
    sources/fs_utils_win.cpp
    sources/fs_utils_win_shared.cpp
    sources/fs_watcher_win.cpp
    sources/math_functions_win.cpp
    sources/memory_crt_allocator_win.cpp
    sources/memory_win.cpp
    sources/os_functions_win.cpp
    sources/os_platform_win.cpp
    sources/platform_extensions_win.cpp
    sources/resources_device_manager_thread_win.cpp
    sources/threading_event_win.cpp
    sources/threading_functions_win.cpp
    sources/threading_functions_win_shared.cpp
    sources/threading_mutex_win.cpp
    sources/timing_functions_win.cpp
)

set(CORE_LINUX_SOURCES
    sources/core_entry_point_posix.cpp
    sources/fs_file_system_impl_mount_disk_posix.cpp
    sources/fs_path_posix.cpp
    sources/fs_utils_posix.cpp
    sources/fs_watcher_multi.cpp
    sources/math_functions_posix.cpp
    sources/memory_crt_allocator_posix.cpp
    sources/memory_posix.cpp
    sources/os_functions_posix.cpp
    sources/os_platform_posix.cpp
    sources/threading_event_posix.cpp
    sources/threading_functions_posix.cpp
    sources/threading_mutex_posix.cpp
    sources/timing_functions_posix.cpp
)

set(CORE_SOURCES ${CORE_COMMON_SOURCES})
if(WIN32)
    list(APPEND CORE_SOURCES ${CORE_WIN_SOURCES})
elseif(UNIX)
    list(APPEND CORE_SOURCES ${CORE_LINUX_SOURCES})
endif()





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
target_link_libraries(xray_core PUBLIC sol2::sol2 Boost::headers Boost::filesystem Boost::system Boost::thread)
target_link_libraries(xray_core PRIVATE lua mimalloc)
if(WIN32)
    target_link_libraries(xray_core PRIVATE winmm shlwapi)
elseif(UNIX)
    target_link_libraries(xray_core PRIVATE pthread ${CMAKE_DL_LIBS})
endif()

# Preprocessor definitions
target_compile_definitions(xray_core PRIVATE XRAY_CORE_BUILDING XRAY_LOGGING_BUILDING XRAY_DEBUG_BUILDING)

# Keep XRAY_STATIC_LIBRARIES defined (from root CMakeLists.txt) so all other
# module API macros (XRAY_VFS_API, XRAY_FS_API, etc.) remain empty.
# On MSVC: WINDOWS_EXPORT_ALL_SYMBOLS exports everything from the DLL.
# On MinGW/GCC: symbols are exported by default from a DLL.
