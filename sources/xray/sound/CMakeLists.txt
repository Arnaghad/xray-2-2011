cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE SOUND_SOURCES "sources/*.cpp" "sources/*.h" "*.h")

list(REMOVE_ITEM SOUND_SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/sources/ogg_source_cooker.cpp")
list(REMOVE_ITEM SOUND_SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/sources/ogg_source_cooker.h")
list(REMOVE_ITEM SOUND_SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/sources/debug_snapshot.cpp")
list(REMOVE_ITEM SOUND_SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/sources/debug_snapshot.h")

add_library(xray_sound STATIC ${SOUND_SOURCES})
target_link_libraries(xray_sound PUBLIC sol2::sol2)

target_include_directories(xray_sound PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

target_link_libraries(xray_sound PUBLIC xaudio2_8 ole32 vorbisfile vorbisenc vorbis ogg)

target_compile_definitions(xray_sound PRIVATE XRAY_SOUND_BUILDING XRAY_ENGINE_BUILDING)
