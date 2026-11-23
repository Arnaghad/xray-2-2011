cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE INPUT_SOURCES "sources/*.cpp" "sources/*.h" "*.h")

add_library(xray_input STATIC ${INPUT_SOURCES})
target_link_libraries(xray_input PUBLIC sol2::sol2)

target_include_directories(xray_input PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)

target_link_libraries(xray_input PUBLIC SDL2::SDL2)

target_compile_definitions(xray_input PRIVATE XRAY_INPUT_BUILDING)
