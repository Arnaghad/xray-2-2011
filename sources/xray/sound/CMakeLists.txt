cmake_minimum_required(VERSION 3.25)

set(SOUND_SOURCES
    sources/composite_sound.cpp
    sources/composite_sound_cook.cpp
    sources/console_commands.cpp
    sources/debug_statistic.cpp
    sources/encoded_sound_interface.cpp
    sources/encoded_sound_with_qualities.cpp
    sources/encoded_sound_with_qualities_cook.cpp
    sources/functor_command.cpp
    sources/listener.cpp
    sources/ogg_encode_impl.cpp
    sources/ogg_encoded_sound_interface.cpp
    sources/ogg_encoded_sound_interface_cook.cpp
    sources/ogg_file_contents.cpp
    sources/ogg_file_contents_cook.cpp
    sources/ogg_sound.cpp
    sources/ogg_sound_cook.cpp
    sources/ogg_source_cook.cpp
    sources/ogg_utils.cpp
    sources/panning_lut.cpp
    sources/panning_lut_cook.cpp
    sources/pch.cpp
    sources/single_sound.cpp
    sources/single_sound_cook.cpp
    sources/sound_buffer.cpp
    sources/sound_buffer_factory.cpp
    sources/sound_collection.cpp
    sources/sound_collection_cook.cpp
    sources/sound_cooker.cpp
    sources/sound_debug_stats.cpp
    sources/sound_emitter.cpp
    sources/sound_entry_point.cpp
    sources/sound_instance_proxy.cpp
    sources/sound_instance_proxy_internal.cpp
    sources/sound_instance_proxy_order.cpp
    sources/sound_item.cpp
    sources/sound_item_callback.cpp
    sources/sound_library_linkage.cpp
    sources/sound_object.cpp
    sources/sound_object_callback.cpp
    sources/sound_object_commands.cpp
    sources/sound_producer.cpp
    sources/sound_propagator.cpp
    sources/sound_receiver.cpp
    sources/sound_rms.cpp
    sources/sound_rms_cook.cpp
    sources/sound_scene.cpp
    sources/sound_scene_cook.cpp
    sources/sound_spl.cpp
    sources/sound_spl_cook.cpp
    sources/sound_stream.cpp
    sources/sound_stream_cook.cpp
    sources/sound_stream_cooker.cpp
    sources/sound_stream_rms.cpp
    sources/sound_voice.cpp
    sources/sound_voice_callbacks.cpp
    sources/sound_world.cpp
    sources/sound_world_command_processor.cpp
    sources/sound_world_orders.cpp
    sources/spl_cook.cpp
    sources/spl_utils.cpp
    sources/voice_bridge.cpp
    sources/voice_factory.cpp
    sources/wav_encoded_sound_interface.cpp
    sources/wav_encoded_sound_interface_cook.cpp
    sources/wav_utils.cpp
    sources/world.cpp
    sources/world_user.cpp
)

find_package(PkgConfig REQUIRED)
pkg_check_modules(OGG_VORBIS REQUIRED IMPORTED_TARGET GLOBAL vorbisfile vorbisenc vorbis ogg)

add_library(xray_sound STATIC ${SOUND_SOURCES})
target_link_libraries(xray_sound PUBLIC sol2::sol2)

target_include_directories(xray_sound PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

target_link_libraries(xray_sound PUBLIC xaudio2_9 ole32 PkgConfig::OGG_VORBIS)

target_compile_definitions(xray_sound PRIVATE XRAY_SOUND_BUILDING XRAY_ENGINE_BUILDING)
