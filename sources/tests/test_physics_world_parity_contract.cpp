#define XRAY_LOG_MODULE_INITIATOR "test_physics_world_parity_contract"
#include "test_check.hpp"

#include <xray/extensions.h>
#include <xray/collision/api.h>
#include <xray/collision/geometry_instance.h>
#include <xray/physics/api.h>
#include <xray/physics/collision_shapes.h>
#include <xray/physics/editor_physics_scene.h>
#include <xray/physics/engine.h>
#include <xray/physics/model_physics.h>
#include <xray/physics/rigid_body.h>
#include <xray/physics/shell.h>
#include <xray/physics/world.h>

#include "bullet_include.h"
#include "bullet_physics_world.h"
#include "integration_constants.h"
#include "physics_world.h"

#include <BulletDynamics/ConstraintSolver/btPoint2PointConstraint.h>

#include <cmath>
#include <iostream>
#include <vector>

namespace {

struct stub_physics_engine final : xray::physics::engine {
};

struct capture_update_object final : xray::physics::update_object_interface {
    xray::math::float4x4 last_transform = xray::math::float4x4().identity();
    u32 updates = 0;

    void physics_update_transform(xray::math::float4x4 const* transforms, u32 count) override {
        if (transforms && count > 0) {
            last_transform = transforms[0];
            ++updates;
        }
    }
};

float run_ode_freefall(xray::memory::crt_allocator& alloc, float start_y, int steps, float dt) {
    stub_physics_engine eng;
    xray::physics::world* w = xray::physics::create_world(&alloc, eng);
    XRAY_TEST_CHECK(w != nullptr);
    auto* ode_world = static_cast<xray::physics::physics_world*>(w);
    ode_world->initialize();
    ode_world->set_ready(true);

    xray::math::float4x4 pose = xray::math::float4x4().identity();
    pose.c.xyz().set(0.0f, start_y, 0.0f);
    auto geom = xray::collision::new_sphere_geometry_instance(&alloc, pose, 0.5f);
    auto sh = ode_world->new_physics_shell(geom);
    capture_update_object updater;
    sh->set_update_object(&updater);

    for (int i = 0; i < steps; ++i) {
        ode_world->step_fixed(dt);
    }
    XRAY_TEST_CHECK(updater.updates > 0);
    float const final_y = updater.last_transform.c.y;

    sh->set_update_object(nullptr);
    ode_world->delete_shell(sh);
    xray::physics::destroy_world(&alloc, w);
    return final_y;
}

float run_bullet_freefall(xray::memory::crt_allocator& alloc, float start_y, int steps, float dt, float gravity_mag) {
    stub_physics_engine eng;
    xray::physics::world* w = xray::physics::create_world_bt(&alloc, eng);
    XRAY_TEST_CHECK(w != nullptr);
    auto* bt_world = static_cast<xray::physics::bullet_physics_world*>(w);
    bt_world->initialize();
    bt_world->get_bt_internal()->setGravity(btVector3(0.0f, -gravity_mag, 0.0f));

    auto* raw_shape = xray::physics::create_primitive_shape(alloc, xray::collision::primitive_sphere, xray::math::float3(0.5f, 0.5f, 0.5f));
    raw_shape->set_no_delete();
    xray::physics::bt_rigid_body_construction_info info;
    info.m_collisionShape = raw_shape;
    info.m_mass = 1.0f;
    xray::physics::bt_rigid_body* body = xray::physics::create_rigid_body(info);
    body->set_transform(xray::math::create_translation(xray::math::float3(0.0f, start_y, 0.0f)));
    bt_world->add_rigid_body(body);

    for (int i = 0; i < steps; ++i) {
        bt_world->step_fixed(dt, 1, dt);
    }
    float const final_y = body->get_transform().c.y;

    bt_world->remove_rigid_body(body);
    xray::physics::destroy_rigid_body(body);
    xray::physics::destroy_shape(alloc, raw_shape);
    xray::physics::destroy_world(&alloc, w);
    return final_y;
}

void test_ode_vs_bullet_fixed_step_parity(xray::memory::crt_allocator& alloc) {
    constexpr float start_y = 50.0f;
    constexpr int steps = 50;
    constexpr float dt = 0.01f;
    float const t_total = steps * dt;

    float const y_ode_1 = run_ode_freefall(alloc, start_y, steps, dt);
    float const y_ode_2 = run_ode_freefall(alloc, start_y, steps, dt);
    float const y_bt_1 = run_bullet_freefall(alloc, start_y, steps, dt, default_gravity);
    float const y_bt_2 = run_bullet_freefall(alloc, start_y, steps, dt, default_gravity);

    // Deterministic across identical runs
    XRAY_TEST_CHECK(y_ode_1 == y_ode_2);
    XRAY_TEST_CHECK(y_bt_1 == y_bt_2);

    float const expected_y = start_y - 0.5f * default_gravity * t_total * t_total;
    XRAY_TEST_CHECK(std::abs(y_ode_1 - expected_y) < 0.25f);
    XRAY_TEST_CHECK(std::abs(y_bt_1 - expected_y) < 0.25f);
    XRAY_TEST_CHECK(std::abs(y_ode_1 - y_bt_1) < 0.20f);

    std::cout << "PARITY freefall (t=" << t_total << "s, g=" << default_gravity
              << "): analytical=" << expected_y
              << ", ode=" << y_ode_1
              << ", bullet=" << y_bt_1 << "\n";
}

void test_contacts_constraints_terrain_and_picking(xray::memory::crt_allocator& alloc) {
    stub_physics_engine eng;
    xray::physics::world* w = xray::physics::create_world_bt(&alloc, eng);
    auto* bt_world = static_cast<xray::physics::bullet_physics_world*>(w);
    bt_world->initialize();
    bt_world->get_bt_internal()->setGravity(btVector3(0.0f, -9.81f, 0.0f));

    // 1. Static triangle mesh floor + dynamic falling box contact
    std::vector<xray::math::float3> const floor_verts = {
        {-20.0f, 0.0f, -20.0f},
        { 20.0f, 0.0f, -20.0f},
        { 20.0f, 0.0f,  20.0f},
        {-20.0f, 0.0f,  20.0f},
    };
    std::vector<u32> const floor_indices = {
        0, 1, 2,
        0, 2, 3,
    };
    auto* floor_shape = xray::physics::create_owned_static_triangle_mesh_shape(
        alloc, floor_verts, floor_indices, xray::math::float3(0.0f, 0.0f, 0.0f)
    );
    floor_shape->set_no_delete();
    xray::physics::bt_rigid_body_construction_info floor_info;
    floor_info.m_collisionShape = floor_shape;
    floor_info.m_mass = 0.0f;
    xray::physics::bt_rigid_body* floor_body = xray::physics::create_rigid_body(floor_info);
    floor_body->set_transform(xray::math::float4x4().identity());
    bt_world->add_rigid_body(floor_body);

    auto* box_shape = xray::physics::create_primitive_shape(
        alloc, xray::collision::primitive_box, xray::math::float3(0.5f, 0.5f, 0.5f)
    );
    box_shape->set_no_delete();
    xray::physics::bt_rigid_body_construction_info box_info;
    box_info.m_collisionShape = box_shape;
    box_info.m_mass = 10.0f;
    box_info.m_friction = 0.7f;
    xray::physics::bt_rigid_body* box_body = xray::physics::create_rigid_body(box_info);
    box_body->set_transform(xray::math::create_translation(xray::math::float3(0.0f, 3.0f, 0.0f)));
    bt_world->add_rigid_body(box_body);

    // 2. Point-to-point pendulum constraint (arm length = 2.0m at X=8, anchor=(8, 6, 0))
    auto* sphere_shape = xray::physics::create_primitive_shape(
        alloc, xray::collision::primitive_sphere, xray::math::float3(0.3f, 0.3f, 0.3f)
    );
    sphere_shape->set_no_delete();
    xray::physics::bt_rigid_body_construction_info pend_info;
    pend_info.m_collisionShape = sphere_shape;
    pend_info.m_mass = 2.0f;
    xray::physics::bt_rigid_body* pend_body = xray::physics::create_rigid_body(pend_info);
    pend_body->set_transform(xray::math::create_translation(xray::math::float3(10.0f, 6.0f, 0.0f)));
    bt_world->add_rigid_body(pend_body);

    btVector3 const local_pivot = btVector3(-2.0f, 0.0f, 0.0f);
    btVector3 const world_anchor = btVector3(8.0f, 6.0f, 0.0f);
    auto* p2p = new btPoint2PointConstraint(*pend_body->get_bt_body(), local_pivot);
    p2p->setPivotB(world_anchor);
    bt_world->get_bt_internal()->addConstraint(p2p, true);

    // 3. Heightfield terrain at (-20, 0, 0) - 17x17 sloping patch
    constexpr u32 grid_n = 17;
    std::vector<float> heights(grid_n * grid_n, 0.0f);
    for (u32 z = 0; z < grid_n; ++z) {
        for (u32 x = 0; x < grid_n; ++x) {
            heights[z * grid_n + x] = 0.1f * static_cast<float>(x);
        }
    }
    auto* terrain_shape = xray::physics::create_terrain_shape(alloc, heights.data(), grid_n, 0.0f, 2.0f);
    terrain_shape->set_no_delete();
    xray::physics::bt_rigid_body_construction_info terrain_info;
    terrain_info.m_collisionShape = terrain_shape;
    terrain_info.m_mass = 0.0f;
    xray::physics::bt_rigid_body* terrain_body = xray::physics::create_rigid_body(terrain_info);
    // Shift terrain to X=-30 (Y=1.0 origin -> terrain top around Y=0..2) so it is isolated from floor_body
    terrain_body->set_transform(xray::math::create_translation(xray::math::float3(-40.0f, 1.0f, 0.0f)));
    bt_world->add_rigid_body(terrain_body);

    xray::physics::bt_rigid_body* terrain_ball = xray::physics::create_rigid_body(pend_info);
    terrain_ball->set_transform(xray::math::create_translation(xray::math::float3(-40.0f, 5.0f, 0.0f)));
    bt_world->add_rigid_body(terrain_ball);

    for (int i = 0; i < 180; ++i) {
        bt_world->step_fixed(1.0f / 60.0f, 4, 1.0f / 60.0f);
        if (i == 60) {
            // Check pendulum arm constraint mid-swing
            xray::math::float3 const pos = pend_body->get_transform().c.xyz();
            xray::math::float3 const anchor_xr(8.0f, 6.0f, 0.0f);
            float const arm_dist = (pos - anchor_xr).length();
            XRAY_TEST_CHECK(std::isfinite(arm_dist) && std::abs(arm_dist - 2.0f) < 0.08f);
            XRAY_TEST_CHECK(pos.y < 6.0f);
        }
    }

    // Box should settle on Y=0 floor with half-extent 0.5 -> center near Y=0.5
    xray::math::float3 const box_pos = box_body->get_transform().c.xyz();
    XRAY_TEST_CHECK(std::isfinite(box_pos.y) && box_pos.y > 0.35f && box_pos.y < 0.65f);

    // Terrain ball should not tunnel below heightfield
    xray::math::float3 const tb_pos = terrain_ball->get_transform().c.xyz();
    XRAY_TEST_CHECK(std::isfinite(tb_pos.y) && tb_pos.y > 0.0f && tb_pos.y < 4.5f);

    // 4. Picking / ray_test verification (note Bullet world uses -Z convention in from_xray/from_bullet,
    // ray_test converts both ray_from and hit_point consistently via from_xray/from_bullet)
    xray::physics::closest_ray_result const hit = bt_world->ray_test(
        xray::math::float3(0.0f, 5.0f, 0.0f),
        xray::math::float3(0.0f, -1.0f, 0.0f),
        10.0f
    );
    XRAY_TEST_CHECK(hit.m_object == box_body);
    XRAY_TEST_CHECK(hit.m_hit_point_world.y > 0.85f && hit.m_hit_point_world.y < 1.15f);

    xray::physics::closest_ray_result const miss = bt_world->ray_test(
        xray::math::float3(100.0f, 5.0f, 100.0f),
        xray::math::float3(0.0f, -1.0f, 0.0f),
        10.0f
    );
    XRAY_TEST_CHECK(miss.m_object == nullptr);

    // 5. Editor physics scene verification
    xray::physics::editor_physics_scene editor_scene;
    unsigned const floor_id = editor_scene.add_static_triangles(
        floor_verts, floor_indices, xray::math::float4x4().identity(), 0.7f, 0.0f
    );
    xray::physics::model_physics_parameters params;
    params.mass = 5.0f;
    params.primitives.push_back(xray::physics::model_collision_primitive{1, {0, 0, 0}, {0, 0, 0}, {0.5f, 0.5f, 0.5f}});
    unsigned const body_id = editor_scene.add(
        params, xray::math::create_translation(xray::math::float3(0.0f, 2.0f, 0.0f))
    );
    XRAY_TEST_CHECK(editor_scene.size() == 2 && floor_id == 0 && body_id == 1);
    for (int i = 0; i < 90; ++i) {
        editor_scene.step(1.0f / 60.0f);
    }
    xray::math::float4x4 const ed_tr = editor_scene.transform(body_id);
    xray::math::aabb const ed_bounds = editor_scene.bounds(body_id);
    XRAY_TEST_CHECK(ed_tr.c.y > 0.35f && ed_tr.c.y < 0.7f);
    XRAY_TEST_CHECK(ed_bounds.max.y > ed_bounds.min.y);

    // Clean teardown in reverse dependency order
    bt_world->get_bt_internal()->removeConstraint(p2p);
    delete p2p;
    bt_world->remove_rigid_body(terrain_ball);
    xray::physics::destroy_rigid_body(terrain_ball);
    bt_world->remove_rigid_body(terrain_body);
    xray::physics::destroy_rigid_body(terrain_body);
    bt_world->remove_rigid_body(pend_body);
    xray::physics::destroy_rigid_body(pend_body);
    bt_world->remove_rigid_body(box_body);
    xray::physics::destroy_rigid_body(box_body);
    bt_world->remove_rigid_body(floor_body);
    xray::physics::destroy_rigid_body(floor_body);
    xray::physics::destroy_shape(alloc, terrain_shape);
    xray::physics::destroy_shape(alloc, sphere_shape);
    xray::physics::destroy_shape(alloc, box_shape);
    xray::physics::destroy_shape(alloc, floor_shape);
    xray::physics::destroy_world(&alloc, w);
}

} // namespace

int main() {
    xray::memory::crt_allocator alloc;
    xray::physics::set_memory_allocator(&alloc);

    test_ode_vs_bullet_fixed_step_parity(alloc);
    test_contacts_constraints_terrain_and_picking(alloc);

    std::cout << "PASS: xray_physics_world_parity_contract\n";
    return 0;
}
