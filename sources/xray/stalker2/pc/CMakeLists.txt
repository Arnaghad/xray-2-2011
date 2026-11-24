project(stalker2)
enable_language(RC)

file(GLOB_RECURSE STALKER2_SOURCES "sources/*.cpp" "sources/*.h")

add_executable(stalker2 WIN32 ${STALKER2_SOURCES} stalker2.rc)
set_source_files_properties(stalker2.rc PROPERTIES OBJECT_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/stalker2.manifest")
if(MSVC)
    target_link_options(stalker2 PRIVATE /MANIFEST:NO)
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
    $<TARGET_LINKER_FILE:xray_engine>
)

target_compile_definitions(stalker2 PRIVATE STALKER2_BUILDING)
