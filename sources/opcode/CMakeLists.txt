cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE OPCODE_SOURCES "*.cpp" "*.h")

add_library(opcode STATIC ${OPCODE_SOURCES})

target_include_directories(opcode PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/.. ${CMAKE_CURRENT_SOURCE_DIR})

target_link_libraries(opcode PUBLIC xray_core)

if(NOT MSVC)
    target_compile_definitions(opcode PRIVATE __noop=)
endif()

target_compile_options(opcode PRIVATE -Wno-unknown-pragmas -fpermissive)

list(FILTER OPCODE_SOURCES EXCLUDE REGEX "(StdAfx|entry_point)\\.(cpp|h)$")
