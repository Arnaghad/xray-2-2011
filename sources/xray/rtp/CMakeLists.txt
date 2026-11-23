cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE RTP_SOURCES "sources/*.cpp" "sources/*.h" "*.h")

list(FILTER RTP_SOURCES EXCLUDE REGEX "test_.*\\.cpp")

add_library(xray_rtp STATIC ${RTP_SOURCES})

target_include_directories(xray_rtp PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)
target_include_directories(xray_rtp PRIVATE sources)

target_link_libraries(xray_rtp PUBLIC xray_core)

target_compile_definitions(xray_rtp PRIVATE XRAY_RTP_BUILDING XRAY_ENGINE_BUILDING)
