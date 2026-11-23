add_subdirectory(core)
add_subdirectory(script)

# Headers-only modules — INTERFACE libraries for include path propagation
add_library(cs_lua INTERFACE)
target_include_directories(cs_lua INTERFACE ${CMAKE_CURRENT_SOURCE_DIR}/../)
target_link_libraries(cs_lua INTERFACE lua)

add_library(cs_lua_studio_backend INTERFACE)
target_include_directories(cs_lua_studio_backend INTERFACE ${CMAKE_CURRENT_SOURCE_DIR}/../)
