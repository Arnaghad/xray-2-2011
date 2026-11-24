cmake_minimum_required(VERSION 3.25)

set(INPUT_SOURCES
    sources/input_entry_point.cpp
    sources/input_library_linkage.cpp
    sources/input_world.cpp
    sources/input_world_win.cpp
    sources/pch.cpp
    sources/receiver_gamepad_win.cpp
    sources/receiver_keyboard.cpp
    sources/receiver_keyboard_win.cpp
    sources/receiver_mouse.cpp
    sources/receiver_mouse_win.cpp
)

add_library(xray_input STATIC ${INPUT_SOURCES})
target_link_libraries(xray_input PUBLIC sol2::sol2 SDL3::SDL3)

target_include_directories(xray_input PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

target_compile_definitions(xray_input PRIVATE XRAY_INPUT_BUILDING)
if(NOT MSVC)
    target_compile_options(xray_input PRIVATE
        -Wall -Wextra -Wstrict-aliasing=2 -Werror=strict-aliasing -Werror=return-local-addr -Wformat
    )
endif()
