cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE ODE_SOURCES "sources/*.cpp" "sources/*.h" "sources/*.c" "*.h")

add_library(ode STATIC ${ODE_SOURCES})

target_include_directories(ode PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

target_compile_definitions(ode PRIVATE dSingle)
