cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE NETWORK_SOURCES "sources/*.cpp" "sources/*.h" "*.h")

find_path(XRAY_ASIO_INCLUDE_DIR NAMES asio.hpp REQUIRED)
if(NOT TARGET asio::asio)
    add_library(asio::asio INTERFACE IMPORTED GLOBAL)
    set_target_properties(asio::asio PROPERTIES
        INTERFACE_INCLUDE_DIRECTORIES "${XRAY_ASIO_INCLUDE_DIR}"
        INTERFACE_SYSTEM_INCLUDE_DIRECTORIES "${XRAY_ASIO_INCLUDE_DIR}"
    )
endif()

add_library(xray_network STATIC ${NETWORK_SOURCES})
target_link_libraries(xray_network PUBLIC sol2::sol2)

target_include_directories(xray_network PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)
target_include_directories(xray_network PRIVATE sources)


target_link_libraries(xray_network PUBLIC asio::asio ws2_32)

target_compile_definitions(xray_network PRIVATE XRAY_NETWORK_BUILDING)
