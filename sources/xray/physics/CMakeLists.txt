cmake_minimum_required(VERSION 3.25)

find_package(PkgConfig REQUIRED)
pkg_check_modules(BULLET REQUIRED IMPORTED_TARGET GLOBAL bullet)

set(PHYSICS_SOURCES
    sources/animated_model_instance_cook.cpp
    sources/auto_sleep.cpp
    sources/autosleep_params.cpp
    sources/body.cpp
    sources/body_ode.cpp
    sources/box_geometry.cpp
    sources/bullet_physics_world.cpp
    sources/bullet_transforms.cpp
    sources/character_controller.cpp
    sources/collide_element.cpp
    sources/collision_shape_cook.cpp
    sources/collision_shapes.cpp
    sources/contact.cpp
    sources/contact_info_buffer.cpp
    sources/contact_ode.cpp
    sources/contact_storage.cpp
    sources/dynamic_object.cpp
    sources/editor_physics_scene.cpp
    sources/element.cpp
    sources/generate_contact.cpp
    sources/generate_contact_base.cpp
    sources/generic_joint_create_lua_config.cpp
    sources/geometry.cpp
    sources/geometry_cform.cpp
    sources/heightfield_terrain_shape.cpp
    sources/integrator_ode.cpp
    sources/island.cpp
    sources/joint_ball.cpp
    sources/joint_base_impl.cpp
    sources/joint_double_base_impl.cpp
    sources/joint_euler_hinge.cpp
    sources/joint_fixed.cpp
    sources/joint_hinge.cpp
    sources/joint_ode.cpp
    sources/joint_ode_base.cpp
    sources/joint_single_base_impl.cpp
    sources/joint_slider_hinge.cpp
    sources/joint_wheel.cpp
    sources/pch.cpp
    sources/physics_entry_point.cpp
    sources/physics_library_linkage.cpp
    sources/physics_world.cpp
    sources/pose_anchor.cpp
    sources/rigid_body.cpp
    sources/rigid_body_construction_info.cpp
    sources/scheduler.cpp
    sources/shell.cpp
    sources/shell_create_binary_config.cpp
    sources/shell_create_lua_config.cpp
    sources/shell_static.cpp
    sources/soft_body.cpp
    sources/space_item.cpp
    sources/space_object.cpp
    sources/static_element.cpp
    sources/step_object.cpp
    sources/temp_storage_holder.cpp
    sources/transformed_geometry.cpp
)

# Shape resources are also needed by render cooks, independently of physics worlds.
add_library(xray_physics_shapes STATIC sources/collision_shape_resource.cpp)
target_include_directories(xray_physics_shapes PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../)
target_link_libraries(xray_physics_shapes PUBLIC PkgConfig::BULLET xray_core)
target_compile_definitions(xray_physics_shapes PRIVATE XRAY_PHYSICS_BUILDING XRAY_ENGINE_BUILDING)

add_library(xray_physics STATIC ${PHYSICS_SOURCES})

target_include_directories(xray_physics PUBLIC 
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../
)

target_link_libraries(xray_physics PUBLIC
    xray_physics_shapes
    ode
    $<$<NOT:$<BOOL:${XRAY_COLLISION_USE_BULLET_BVH}>>:opcode>
    xray_core
)
if(TARGET xray_render_vulkan)
    target_link_libraries(xray_physics PUBLIC xray_render_vulkan)
elseif(TARGET xray_render_headless_stub)
    target_link_libraries(xray_physics PUBLIC xray_render_headless_stub)
endif()

target_compile_definitions(xray_physics PRIVATE XRAY_PHYSICS_BUILDING XRAY_ENGINE_BUILDING)
