cmake_minimum_required(VERSION 3.25)

find_package(PkgConfig REQUIRED)
pkg_check_modules(BULLET REQUIRED IMPORTED_TARGET GLOBAL bullet)

file(GLOB_RECURSE PHYSICS_SOURCES CONFIGURE_DEPENDS "sources/*.cpp" "sources/*.h" "*.h")
list(REMOVE_ITEM PHYSICS_SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/sources/collision_shape_resource.cpp")

# Shape resources are also needed by render cooks, independently of physics worlds.
add_library(xray_physics_shapes STATIC sources/collision_shape_resource.cpp)
target_include_directories(xray_physics_shapes PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../ ${BULLET_INCLUDE_DIRS})
target_link_libraries(xray_physics_shapes PUBLIC ${BULLET_LIBRARIES} xray_core)
target_compile_definitions(xray_physics_shapes PRIVATE XRAY_PHYSICS_BUILDING XRAY_ENGINE_BUILDING)

add_library(xray_physics STATIC ${PHYSICS_SOURCES})

target_include_directories(xray_physics PUBLIC 
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../ 
    ${BULLET_INCLUDE_DIRS}
)

target_link_libraries(xray_physics PUBLIC xray_physics_shapes ode opcode xray_core xray_render_vulkan)

target_compile_definitions(xray_physics PRIVATE XRAY_PHYSICS_BUILDING XRAY_ENGINE_BUILDING)
