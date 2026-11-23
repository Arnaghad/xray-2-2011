cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE AI_NAV_SOURCES "sources/*.cpp" "sources/*.h" "*.h")

add_library(xray_ai_navigation STATIC ${AI_NAV_SOURCES})
target_link_libraries(xray_ai_navigation PUBLIC sol2::sol2)

target_include_directories(xray_ai_navigation PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)



target_compile_definitions(xray_ai_navigation PRIVATE XRAY_AI_NAVIGATION_BUILDING)
