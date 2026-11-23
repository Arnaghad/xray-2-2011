cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE AI_SOURCES "sources/*.cpp" "sources/*.h" "*.h")

add_library(xray_ai STATIC ${AI_SOURCES})
target_link_libraries(xray_ai PUBLIC sol2::sol2)

target_include_directories(xray_ai PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)



target_compile_definitions(xray_ai PRIVATE XRAY_AI_BUILDING)
