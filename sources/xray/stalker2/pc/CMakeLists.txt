project(stalker2)

set(STALKER2_COMMON_SOURCES
    sources/pch.cpp
    sources/stalker2_pc_application.cpp
    sources/stalker2_pc_entry_point.cpp
    sources/stalker2_pc_library_linkage.cpp
)

set(STALKER2_WIN_SOURCES
    sources/math_error_win.cpp
    sources/stalker2_pc_application_win.cpp
)

if(WIN32)
    enable_language(RC)
    add_executable(stalker2 WIN32 ${STALKER2_COMMON_SOURCES} ${STALKER2_WIN_SOURCES} stalker2.rc)
    set_source_files_properties(stalker2.rc PROPERTIES OBJECT_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/stalker2.manifest")
    if(MSVC)
        target_link_options(stalker2 PRIVATE /MANIFEST:NO)
    endif()
else()
    add_executable(stalker2 ${STALKER2_COMMON_SOURCES} sources/stalker2_pc_application_posix.cpp)
    target_link_libraries(stalker2 PRIVATE SDL3::SDL3)
endif()
if(TARGET xray_editor_world)
    add_dependencies(stalker2 xray_editor_world)
endif()

target_include_directories(stalker2 PRIVATE 
    "sources"
    "${CMAKE_CURRENT_SOURCE_DIR}"
    "${CMAKE_CURRENT_SOURCE_DIR}/../../../"
)

target_sources(stalker2 PRIVATE 
    ../finger_print.cpp
    ../game_module_proxy.cpp
)

target_link_libraries(stalker2 PRIVATE 
    xray_game
    xray_input
    xray_ui
    xray_ai
    xray_ai_navigation
    xray_animation
    xray_render_vulkan
    xray_engine
)
if(MINGW)
    target_link_libraries(stalker2 PRIVATE $<TARGET_LINKER_FILE:xray_engine>)
endif()

target_compile_definitions(stalker2 PRIVATE STALKER2_BUILDING)
