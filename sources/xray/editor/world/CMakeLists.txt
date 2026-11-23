# xray_editor_world (Native part)
project(xray_editor_world)

set(SOURCES
    "sources/editor_library_linkage.cpp"
    "sources/pch.cpp"
    "sources/editor_entry_point.cpp"
    "sources/managed_host.cpp"
)

set(HEADERS
    "sources/editor_world.h"
    "sources/editor_world_defines.h"
    "sources/editor_world_inline.h"
    "sources/pch.h"
)

add_library(xray_editor_world SHARED ${SOURCES} ${HEADERS})
set_target_properties(xray_editor_world PROPERTIES PREFIX "")
add_dependencies(xray_editor_world xray_editor_host)
file(GLOB XRAY_HOSTFXR_HEADERS "${XRAY_DOTNET_ROOT}/packs/Microsoft.NETCore.App.Host.win-x64/*/runtimes/win-x64/native/hostfxr.h")
file(GLOB XRAY_HOSTFXR_LIBRARIES "${XRAY_DOTNET_ROOT}/host/fxr/*/hostfxr.dll")
if(NOT XRAY_HOSTFXR_HEADERS OR NOT XRAY_HOSTFXR_LIBRARIES)
    message(FATAL_ERROR "The x64 .NET hosting pack and system hostfxr are required for the editor")
endif()
list(SORT XRAY_HOSTFXR_HEADERS COMPARE NATURAL ORDER DESCENDING)
list(SORT XRAY_HOSTFXR_LIBRARIES COMPARE NATURAL ORDER DESCENDING)
list(GET XRAY_HOSTFXR_HEADERS 0 XRAY_HOSTFXR_HEADER)
list(GET XRAY_HOSTFXR_LIBRARIES 0 XRAY_HOSTFXR_LIBRARY)
get_filename_component(XRAY_HOSTFXR_INCLUDE "${XRAY_HOSTFXR_HEADER}" DIRECTORY)
target_include_directories(xray_editor_world PRIVATE "${XRAY_HOSTFXR_INCLUDE}")
target_compile_definitions(xray_editor_world PRIVATE "XRAY_HOSTFXR_PATH=L\"${XRAY_HOSTFXR_LIBRARY}\"")
if(MINGW)
    target_link_options(xray_editor_world PRIVATE -Wl,--export-all-symbols)
endif()

target_include_directories(xray_editor_world PUBLIC 
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/sources
    ${XRAY_ENGINE_DIR}/sources
)

target_link_libraries(xray_editor_world PRIVATE 
    xray_editor_base
    xray_physics
    xray_collision
    xray_terrain_collision
    xray_render_vulkan
    xray_animation
    xray_core
)

target_compile_definitions(xray_editor_world PRIVATE XRAY_EDITOR_BUILDING)

# This library is now pure C++. All UI and C++/CLI logic is removed.
