set(SCRIPT_CS_SOURCES
    sources/engine.cpp
    sources/entry_point.cpp
    sources/library_linkage.cpp
    sources/lua_studio_backend_singleton.cpp
    sources/pch.cpp
    sources/property_holder.cpp
    sources/sol_compat_globals.cpp
    sources/thread.cpp
    sources/world.cpp
    sources/world_autoload.cpp
    sources/world_error.cpp
    sources/world_help.cpp
    sources/world_interface.cpp
    sources/world_object.cpp
    sources/world_process.cpp
)
add_library(cs_script SHARED ${SCRIPT_CS_SOURCES})
target_include_directories(cs_script PUBLIC ${CMAKE_CURRENT_SOURCE_DIR} ${CMAKE_CURRENT_SOURCE_DIR}/../core)
target_link_libraries(cs_script PUBLIC sol2::sol2 Boost::headers PRIVATE cs_core)
