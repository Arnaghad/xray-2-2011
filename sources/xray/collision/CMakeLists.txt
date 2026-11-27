cmake_minimum_required(VERSION 3.25)

set(COLLISION_SOURCES
    sources/aabb_object.cpp
    sources/animated_object.cpp
    sources/api.cpp
    sources/box_geometry.cpp
    sources/box_geometry_instance.cpp
    sources/bullet_mesh_bvh.cpp
    sources/capsule_geometry_instance.cpp
    sources/colliders_aabb_geometry_triangle_intersection.cpp
    sources/colliders_aabb_object.cpp
    sources/colliders_cuboid_object.cpp
    sources/colliders_ray_aabb_all.cpp
    sources/colliders_ray_aabb_win.cpp
    sources/colliders_ray_base_win.cpp
    sources/colliders_ray_object.cpp
    sources/collision_cook.cpp
    sources/collision_library_linkage.cpp
    sources/collision_object.cpp
    sources/composite_geometry.cpp
    sources/composite_geometry_instance.cpp
    sources/containment_double_dispatcher.cpp
    sources/containment_double_dispatcher_box.cpp
    sources/containment_double_dispatcher_capsule.cpp
    sources/containment_double_dispatcher_cylinder.cpp
    sources/containment_double_dispatcher_sphere.cpp
    sources/containment_double_dispatcher_truncated_sphere.cpp
    sources/cylinder_geometry.cpp
    sources/cylinder_geometry_instance.cpp
    sources/geometry_instance.cpp
    sources/geometry_object.cpp
    sources/intersection_double_dispatcher.cpp
    sources/intersection_double_dispatcher_box.cpp
    sources/intersection_double_dispatcher_capsule.cpp
    sources/intersection_double_dispatcher_cylinder.cpp
    sources/intersection_double_dispatcher_sphere.cpp
    sources/intersection_double_dispatcher_truncated_sphere.cpp
    sources/loose_oct_tree.cpp
    sources/pch.cpp
    sources/sphere_geometry.cpp
    sources/sphere_geometry_instance.cpp
    sources/terrain_data.cpp
    sources/terrain_geometry_instance.cpp
    sources/triangle_box_contact_tests.cpp
    sources/triangle_box_contact_tests_old.cpp
    sources/triangle_cylinder_contact_tests.cpp
    sources/triangle_mesh_base.cpp
    sources/triangle_mesh_buffer.cpp
    sources/triangle_mesh_geometry.cpp
    sources/triangle_mesh_geometry_instance.cpp
    sources/triangle_mesh_primitiv_res.cpp
    sources/triangle_mesh_primitive_contact_tests.cpp
    sources/triangle_mesh_resource.cpp
    sources/triangle_mesh_sphere.cpp
    sources/triangle_sphere.cpp
    sources/triangle_sphere_contact_tests.cpp
    sources/truncated_sphere_geometry_instance.cpp
)
set(COLLISION_OPCODE_GEOMETRY_SOURCES
    sources/colliders_aabb_geometry.cpp
    sources/colliders_convex_geometry.cpp
    sources/colliders_cuboid_geometry.cpp
    sources/colliders_prismoid_geometry.cpp
    sources/colliders_ray_geometry_base.cpp
    sources/colliders_ray_query_geometry.cpp
    sources/colliders_ray_test_geometry.cpp
)
if(NOT XRAY_COLLISION_USE_BULLET_BVH)
    list(APPEND COLLISION_SOURCES ${COLLISION_OPCODE_GEOMETRY_SOURCES})
endif()

add_library(xray_collision STATIC ${COLLISION_SOURCES})

target_include_directories(xray_collision PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../)
target_include_directories(xray_collision PRIVATE sources)


# Existing geometry debug drawing uses the renderer, but does not need its build flags.
target_link_libraries(xray_collision PUBLIC
    $<$<NOT:$<BOOL:${XRAY_COLLISION_USE_BULLET_BVH}>>:opcode>
    xray_core
    xray_animation
)
if(TARGET xray_render_vulkan)
    target_link_libraries(xray_collision PUBLIC "$<LINK_ONLY:xray_render_vulkan>")
elseif(TARGET xray_render_headless_stub)
    target_link_libraries(xray_collision PUBLIC xray_render_headless_stub)
endif()
target_link_libraries(xray_collision PRIVATE PkgConfig::BULLET)

target_compile_definitions(xray_collision PRIVATE
    XRAY_COLLISION_BUILDING
    XRAY_ENGINE_BUILDING
    XRAY_COLLISION_USE_BULLET_BVH=$<BOOL:${XRAY_COLLISION_USE_BULLET_BVH}>
)
