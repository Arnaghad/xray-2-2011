cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE NETWORK_SOURCES "sources/*.cpp" "sources/*.h" "*.h")

add_library(xray_network STATIC ${NETWORK_SOURCES})
target_link_libraries(xray_network PUBLIC sol2::sol2)

target_include_directories(xray_network PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)
target_include_directories(xray_network PRIVATE sources)


target_link_libraries(xray_network PUBLIC ws2_32)

target_compile_definitions(xray_network PRIVATE XRAY_NETWORK_BUILDING)
