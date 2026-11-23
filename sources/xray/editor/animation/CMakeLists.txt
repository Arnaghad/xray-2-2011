# xray_editor_animation (Native part)
project(xray_editor_animation)

set(SOURCES
    "sources/animation_editor_entry_point.cpp"
    "sources/animation_editor_library_linkage.cpp"
    "sources/editor_creator.cpp"
    "sources/pch.cpp"
    # Modernized data model
    "sources/animation_item.cpp"
    "sources/animation_single_item.cpp"
    "sources/animation_collection_item.cpp"
)

set(HEADERS
    "sources/animation_collections_editor_native.h"
    "sources/animation_item.h"
    "sources/animation_single_item.h"
    "sources/animation_collection_item.h"
    "sources/animation_item_type.h"
    "sources/pch.h"
    "sources/editor_creator.h"
    "sources/animation_editor_memory.h"
)

add_library(xray_editor_animation SHARED ${SOURCES} ${HEADERS})

target_include_directories(xray_editor_animation PUBLIC 
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/sources
    ${XRAY_ENGINE_DIR}/sources
)

target_link_libraries(xray_editor_animation PRIVATE 
    xray_editor_base
    xray_core
)
