# xray_editor_sound (Native part)
project(xray_editor_sound)

set(SOURCES
    "sources/sound_editor_entry_point.cpp"
    "sources/sound_editor_library_linkage.cpp"
    "sources/editor_creator.cpp"
    "sources/pch.cpp"
    # Native manager
    "sources/sound_editor_manager.cpp"
    # Modernized data model
    "sources/sound_object_wrapper.cpp"
    "sources/single_sound_wrapper.cpp"
    "sources/composite_sound_wrapper.cpp"
    "sources/sound_collection_wrapper.cpp"
    "sources/sound_object_instance.cpp"
    "sources/sound_collection_item.cpp"
    "sources/composite_sound_item.cpp"
    "sources/wav_file_options.cpp"
    "sources/sound_object_collision.cpp"
    "sources/sound_object_collision_dynamic.cpp"
)

set(HEADERS
    "sources/sound_editor_manager.h"
    "sources/sound_scene_document_native.h"
    "sources/sound_object_wrapper.h"
    "sources/single_sound_wrapper.h"
    "sources/composite_sound_wrapper.h"
    "sources/sound_collection_wrapper.h"
    "sources/sound_object_instance.h"
    "sources/sound_collection_item.h"
    "sources/composite_sound_item.h"
    "sources/wav_file_options.h"
    "sources/sound_object_collision.h"
    "sources/sound_object_collision_dynamic.h"
    "sources/pch.h"
    "sources/editor_creator.h"
    "sources/sound_editor_memory.h"
)

add_library(xray_editor_sound SHARED ${SOURCES} ${HEADERS})

target_include_directories(xray_editor_sound PUBLIC 
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/sources
    ${XRAY_ENGINE_DIR}/sources
)

target_link_libraries(xray_editor_sound PRIVATE 
    xray_sound
    xray_editor_base
    xray_render_vulkan
    xray_core
)
