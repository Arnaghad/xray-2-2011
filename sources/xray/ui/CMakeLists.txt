cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE UI_SOURCES "sources/*.cpp" "sources/*.h" "*.h")

add_library(xray_ui STATIC ${UI_SOURCES})
target_link_libraries(xray_ui PUBLIC sol2::sol2)

target_include_directories(xray_ui PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)



target_compile_definitions(xray_ui PRIVATE XRAY_UI_BUILDING)
