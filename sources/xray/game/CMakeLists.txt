project(xray_game)

file(GLOB_RECURSE GAME_SOURCES CONFIGURE_DEPENDS "sources/*.cpp" "sources/*.h")

add_library(xray_game STATIC ${GAME_SOURCES})

target_include_directories(xray_game PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

target_link_libraries(xray_game PUBLIC
    xray_engine
    xray_rtp
)
target_link_libraries(xray_game PRIVATE xray_render_vulkan)
target_compile_definitions(xray_game PRIVATE
    XRAY_GAME_PHYSICS_USE_BULLET=$<BOOL:${XRAY_GAME_PHYSICS_USE_BULLET}>
)
target_link_libraries(xray_game INTERFACE
    xray_sound
    xray_network
    xray_collision
    xray_animation
    xray_physics
)
