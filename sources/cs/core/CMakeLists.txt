set(CORE_CS_SOURCES
    sources/aabb3f_script.cpp
    sources/build_extensions.cpp
    sources/core.cpp
    sources/debug.cpp
    sources/debug_initialize.cpp
    sources/entry_point.cpp
    sources/frustum3f.cpp
    sources/fs.cpp
    sources/fs_readers_base.cpp
    sources/fs_readers_file.cpp
    sources/fs_writers_file.cpp
    sources/library_linkage.cpp
    sources/log.cpp
    sources/log_file.cpp
    sources/math_angles_script.cpp
    sources/matrix4f_script.cpp
    sources/memory_functions.cpp
    sources/memory_monitor.cpp
    sources/os_functions.cpp
    sources/pch.cpp
    sources/pch_script.cpp
    sources/plane3f.cpp
    sources/platform.cpp
    sources/script_export.cpp
    sources/shared_string_manager.cpp
    sources/shared_string_profile.cpp
    sources/sphere3f_script.cpp
    sources/string_functions.cpp
    sources/string_stream.cpp
    sources/threading_functions.cpp
    sources/threading_mutex.cpp
    sources/vec2f_script.cpp
    sources/vec3f_script.cpp
    sources/vec4f_script.cpp
)
add_library(cs_core SHARED ${CORE_CS_SOURCES})
target_include_directories(cs_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
target_link_libraries(cs_core PUBLIC sol2::sol2 Boost::headers PRIVATE mimalloc)
if(WIN32)
    target_link_libraries(cs_core PRIVATE winmm)
elseif(UNIX)
    target_link_libraries(cs_core PRIVATE pthread ${CMAKE_DL_LIBS})
endif()
target_compile_definitions(cs_core PRIVATE CS_CORE_BUILDING)
