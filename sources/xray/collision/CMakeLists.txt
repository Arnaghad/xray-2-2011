cmake_minimum_required(VERSION 3.25)

file(GLOB_RECURSE COLLISION_SOURCES "sources/*.cpp" "sources/*.h" "*.h")
# Select one implementation per symbol; the duplicate multi variants are false-return stubs.
list(FILTER COLLISION_SOURCES EXCLUDE REGEX "/colliders_ray_(aabb_multi|base_multi|base_sse)\\.cpp$")

add_library(xray_collision STATIC ${COLLISION_SOURCES})

target_include_directories(xray_collision PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)
target_include_directories(xray_collision PRIVATE sources)


# Existing geometry debug drawing uses the renderer, but does not need its build flags.
target_link_libraries(xray_collision PUBLIC opcode xray_core xray_animation "$<LINK_ONLY:xray_render_vulkan>")

target_compile_definitions(xray_collision PRIVATE XRAY_COLLISION_BUILDING XRAY_ENGINE_BUILDING)
