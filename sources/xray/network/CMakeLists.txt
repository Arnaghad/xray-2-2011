cmake_minimum_required(VERSION 3.25)

set(NETWORK_COMMON_SOURCES
    sources/boost_api.cpp
    sources/io_service.cpp
    sources/network_entry_point.cpp
    sources/network_library_linkage.cpp
    sources/network_world.cpp
    sources/pch.cpp
)

set(NETWORK_WIN_SOURCES
    sources/platform_initializer_win.cpp
)

set(NETWORK_SOURCES ${NETWORK_COMMON_SOURCES})
if(WIN32)
    list(APPEND NETWORK_SOURCES ${NETWORK_WIN_SOURCES})
else()
    list(APPEND NETWORK_SOURCES sources/platform_initializer_posix.cpp)
endif()

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


target_link_libraries(xray_network PUBLIC asio::asio)
if(WIN32)
    target_link_libraries(xray_network PUBLIC ws2_32)
endif()

target_compile_definitions(xray_network PRIVATE XRAY_NETWORK_BUILDING)
