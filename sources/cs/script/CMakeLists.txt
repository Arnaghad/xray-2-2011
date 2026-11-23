file(GLOB SCRIPT_CS_SOURCES "sources/*.cpp")
add_library(cs_script SHARED ${SCRIPT_CS_SOURCES})
target_include_directories(cs_script PUBLIC ${CMAKE_CURRENT_SOURCE_DIR} ${CMAKE_CURRENT_SOURCE_DIR}/../core)
target_link_libraries(cs_script PUBLIC sol2::sol2 PRIVATE cs_core)
