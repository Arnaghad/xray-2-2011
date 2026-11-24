#define XRAY_LOG_MODULE_INITIATOR "test_collision_bvh_parity_contract"
#include "test_check.hpp"

#include <xray/extensions.h>
#include <xray/collision/api.h>
#include <xray/collision/geometry.h>
#include <xray/collision/object.h>
#include "bullet_mesh_bvh.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <iostream>
#include <vector>

namespace {

struct mesh_data {
    std::vector<xray::math::float3> vertices;
    std::vector<u32> indices;
    std::vector<u32> custom_data;
};

mesh_data make_synthetic_feature_mesh() {
    mesh_data m;
    // Large floor quad (2 triangles)
    m.vertices.push_back({-10.0f, 0.0f, -10.0f});
    m.vertices.push_back({ 10.0f, 0.0f, -10.0f});
    m.vertices.push_back({ 10.0f, 0.0f,  10.0f});
    m.vertices.push_back({-10.0f, 0.0f,  10.0f});
    m.indices.insert(m.indices.end(), {0, 1, 2, 0, 2, 3});

    // Raised platform / box-like top & sides (4 quads = 8 triangles)
    u32 base = static_cast<u32>(m.vertices.size());
    m.vertices.push_back({-2.0f, 2.0f, -2.0f});
    m.vertices.push_back({ 2.0f, 2.0f, -2.0f});
    m.vertices.push_back({ 2.0f, 2.0f,  2.0f});
    m.vertices.push_back({-2.0f, 2.0f,  2.0f});
    m.indices.insert(m.indices.end(), {base, base + 1, base + 2, base, base + 2, base + 3});

    // Sloped ramp connecting y=0 to y=3
    base = static_cast<u32>(m.vertices.size());
    m.vertices.push_back({3.0f, 0.0f, -2.0f});
    m.vertices.push_back({7.0f, 3.0f, -2.0f});
    m.vertices.push_back({7.0f, 3.0f,  2.0f});
    m.vertices.push_back({3.0f, 0.0f,  2.0f});
    m.indices.insert(m.indices.end(), {base, base + 1, base + 2, base, base + 2, base + 3});

    // Vertical thin wall at x = -5
    base = static_cast<u32>(m.vertices.size());
    m.vertices.push_back({-5.0f, 0.0f, -3.0f});
    m.vertices.push_back({-5.0f, 4.0f, -3.0f});
    m.vertices.push_back({-5.0f, 4.0f,  3.0f});
    m.vertices.push_back({-5.0f, 0.0f,  3.0f});
    m.indices.insert(m.indices.end(), {base, base + 1, base + 2, base, base + 2, base + 3});

    u32 const tri_count = static_cast<u32>(m.indices.size() / 3);
    m.custom_data.resize(tri_count);
    for (u32 i = 0; i < tri_count; ++i) {
        m.custom_data[i] = 100u + i;
    }
    return m;
}

mesh_data make_terrain_grid_mesh(u32 grid_n) {
    mesh_data m;
    m.vertices.reserve(grid_n * grid_n);
    for (u32 z = 0; z < grid_n; ++z) {
        for (u32 x = 0; x < grid_n; ++x) {
            float const fx = static_cast<float>(x) - 0.5f * static_cast<float>(grid_n - 1);
            float const fz = static_cast<float>(z) - 0.5f * static_cast<float>(grid_n - 1);
            float const fy = 1.5f * std::sin(fx * 0.25f) * std::cos(fz * 0.25f);
            m.vertices.emplace_back(fx, fy, fz);
        }
    }
    m.indices.reserve((grid_n - 1) * (grid_n - 1) * 6);
    for (u32 z = 0; z < grid_n - 1; ++z) {
        for (u32 x = 0; x < grid_n - 1; ++x) {
            u32 const i0 = z * grid_n + x;
            u32 const i1 = i0 + 1;
            u32 const i2 = i0 + grid_n;
            u32 const i3 = i2 + 1;
            m.indices.insert(m.indices.end(), {i0, i1, i2, i1, i3, i2});
        }
    }
    m.custom_data.assign(m.indices.size() / 3, 1u);
    return m;
}

struct ray_collector {
    float closest_distance = 1e9f;
    bool hit = false;

    bool on_tri(xray::collision::ray_triangle_result const& r) {
        hit = true;
        if (r.distance < closest_distance) {
            closest_distance = r.distance;
        }
        return false;
    }
};

std::vector<u32> extract_sorted_tris(xray::collision::triangles_type const& res) {
    std::vector<u32> ids;
    ids.reserve(res.size());
    for (auto const& item : res) {
        ids.push_back(item.triangle_id);
    }
    std::sort(ids.begin(), ids.end());
    return ids;
}

std::vector<u32> extract_sorted_ray_tris(xray::collision::ray_triangles_type const& res) {
    std::vector<u32> ids;
    ids.reserve(res.size());
    for (auto const& item : res) {
        ids.push_back(item.triangle_id);
    }
    std::sort(ids.begin(), ids.end());
    return ids;
}

void verify_mesh_parity(
    xray::memory::crt_allocator& alloc,
    mesh_data const& mesh,
    char const* label,
    int ray_count,
    int aabb_count
) {
    auto t0 = std::chrono::steady_clock::now();
    auto geom = xray::collision::new_triangle_mesh_geometry(
        &alloc,
        mesh.vertices.data(),
        static_cast<u32>(mesh.vertices.size()),
        mesh.indices.data(),
        static_cast<u32>(mesh.indices.size()),
        mesh.custom_data.data(),
        static_cast<u32>(mesh.custom_data.size())
    );
    auto t1 = std::chrono::steady_clock::now();

    xray::collision::bullet_mesh_bvh bt_bvh;
    bt_bvh.initialize(
        mesh.vertices.data(),
        static_cast<u32>(mesh.vertices.size()),
        mesh.indices.data(),
        static_cast<u32>(mesh.indices.size())
    );
    auto t2 = std::chrono::steady_clock::now();

    xray::collision::ray_triangles_type res_geom(alloc);
    xray::collision::ray_triangles_type res_bt(alloc);
    xray::collision::triangles_type aabb_geom_res(alloc);
    xray::collision::triangles_type aabb_bt_res(alloc);

    double op_ray_ms = 0.0;
    double bt_ray_ms = 0.0;
    int ray_hits = 0;

    for (int i = 0; i < ray_count; ++i) {
        float const angle = static_cast<float>(i) * 0.37f;
        float const ox = 5.0f * std::cos(angle);
        float const oz = 5.0f * std::sin(angle);
        xray::math::float3 org(ox, 8.0f, oz);
        xray::math::float3 dir;
        if (i % 7 == 0) {
            dir = xray::math::float3(0.0f, -1.0f, 0.0f); // exact vertical down
        } else if (i % 7 == 1) {
            org.y = -8.0f;
            dir = xray::math::float3(0.0f, 1.0f, 0.0f);  // exact vertical up
        } else {
            dir = xray::math::normalize(xray::math::float3(-0.3f * std::sin(angle), -1.0f, 0.25f * std::cos(angle)));
        }
        float const max_dist = (i % 11 == 0) ? 2.0f : 40.0f; // test max_distance cutoff & misses too

        res_geom.clear();
        res_bt.clear();
        ray_collector c_geom, c_bt;
        float dist_g = 0.0f;

        auto r0 = std::chrono::steady_clock::now();
        bool const ok_g = geom->ray_query(
            nullptr, org, dir, max_dist, dist_g, res_geom,
            xray::collision::triangles_predicate_type(&c_geom, &ray_collector::on_tri)
        );
        auto r1 = std::chrono::steady_clock::now();
        bool const ok_b = bt_bvh.ray_query(
            nullptr, org, dir, max_dist, res_bt,
            xray::collision::triangles_predicate_type(&c_bt, &ray_collector::on_tri)
        );
        auto r2 = std::chrono::steady_clock::now();

        op_ray_ms += std::chrono::duration<double, std::milli>(r1 - r0).count();
        bt_ray_ms += std::chrono::duration<double, std::milli>(r2 - r1).count();

        XRAY_TEST_CHECK(ok_g == ok_b);
        auto ids_g = extract_sorted_ray_tris(res_geom);
        auto ids_b = extract_sorted_ray_tris(res_bt);
        XRAY_TEST_CHECK(ids_g == ids_b);
        if (ok_g) {
            ++ray_hits;
            XRAY_TEST_CHECK(std::abs(c_geom.closest_distance - c_bt.closest_distance) < 1e-4f);
            for (auto tri_id : ids_g) {
                XRAY_TEST_CHECK(geom->get_custom_data(tri_id) == mesh.custom_data[tri_id]);
            }
        }

        // Also test ray_test closest distance parity
        float test_dist_g = max_dist;
        bool const rt_g = geom->ray_test(org, dir, max_dist, test_dist_g);

        struct rt_helper {
            float& dist;
            bool& hit;
            bool pred(xray::collision::ray_triangle_result const& tr) const {
                if (tr.distance > dist) return false;
                dist = tr.distance;
                hit = true;
                return false;
            }
        };
        float test_dist_b = max_dist;
        bool rt_b = false;
        rt_helper h{test_dist_b, rt_b};
        bt_bvh.ray_test(org, dir, max_dist, xray::collision::triangles_predicate_type(&h, &rt_helper::pred));

        XRAY_TEST_CHECK(rt_g == rt_b);
        if (rt_g) {
            XRAY_TEST_CHECK(std::abs(test_dist_g - test_dist_b) < 1e-4f);
        }
    }

    double op_aabb_ms = 0.0;
    double bt_aabb_ms = 0.0;
    int aabb_hits = 0;

    for (int i = 0; i < aabb_count; ++i) {
        float const cx = -6.0f + 0.35f * static_cast<float>(i % 35);
        float const cz = -6.0f + 0.45f * static_cast<float>((i * 7) % 29);
        xray::math::aabb const box = xray::math::create_aabb_center_radius(
            xray::math::float3(cx, 0.5f, cz),
            xray::math::float3(1.25f, 2.5f, 1.25f)
        );

        aabb_geom_res.clear();
        aabb_bt_res.clear();

        auto a0 = std::chrono::steady_clock::now();
        bool const ag = geom->aabb_query(nullptr, box, aabb_geom_res);
        auto a1 = std::chrono::steady_clock::now();
        bool const ab = bt_bvh.aabb_query(nullptr, box, aabb_bt_res);
        auto a2 = std::chrono::steady_clock::now();

        op_aabb_ms += std::chrono::duration<double, std::milli>(a1 - a0).count();
        bt_aabb_ms += std::chrono::duration<double, std::milli>(a2 - a1).count();

        XRAY_TEST_CHECK(ag == ab);
        auto ids_g = extract_sorted_tris(aabb_geom_res);
        auto ids_b = extract_sorted_tris(aabb_bt_res);
        XRAY_TEST_CHECK(ids_g == ids_b);
        if (ag) {
            ++aabb_hits;
        }
    }

    // Cuboid query check (inward-facing normalized planes for [-1.5, 1.5] x [-2.0, 3.0] x [-1.5, 1.5])
    xray::math::plane const cub_planes[6] = {
        xray::math::plane(xray::math::float3( 1.0f,  0.0f,  0.0f), 1.5f),
        xray::math::plane(xray::math::float3(-1.0f,  0.0f,  0.0f), 1.5f),
        xray::math::plane(xray::math::float3( 0.0f,  1.0f,  0.0f), 2.0f),
        xray::math::plane(xray::math::float3( 0.0f, -1.0f,  0.0f), 3.0f),
        xray::math::plane(xray::math::float3( 0.0f,  0.0f,  1.0f), 1.5f),
        xray::math::plane(xray::math::float3( 0.0f,  0.0f, -1.0f), 1.5f),
    };
    xray::math::cuboid const cub(cub_planes);
    aabb_geom_res.clear();
    aabb_bt_res.clear();
    bool const cg = geom->cuboid_query(nullptr, cub, aabb_geom_res);
    bool const cb = bt_bvh.cuboid_query(nullptr, cub, aabb_bt_res);
    XRAY_TEST_CHECK(cg == cb && cg);

    double const op_build_ms = std::chrono::duration<double, std::milli>(t1 - t0).count();
    double const bt_build_ms = std::chrono::duration<double, std::milli>(t2 - t1).count();
    std::cout << "BVH [" << label << ", tris=" << (mesh.indices.size() / 3)
              << "] build(op=" << op_build_ms << "ms, bt=" << bt_build_ms << "ms)"
              << " ray(" << ray_count << ", hits=" << ray_hits << ", op=" << op_ray_ms << "ms, bt=" << bt_ray_ms << "ms)"
              << " aabb(" << aabb_count << ", hits=" << aabb_hits << ", op=" << op_aabb_ms << "ms, bt=" << bt_aabb_ms << "ms)\n";

    xray::collision::delete_geometry(&alloc, &*geom);
}

} // namespace

int main() {
    xray::memory::crt_allocator alloc;

    mesh_data const synth = make_synthetic_feature_mesh();
    verify_mesh_parity(alloc, synth, "synthetic_10tri", 60, 30);

    mesh_data const terrain33 = make_terrain_grid_mesh(33);
    verify_mesh_parity(alloc, terrain33, "terrain_33x33", 200, 50);

    mesh_data const terrain65 = make_terrain_grid_mesh(65);
    verify_mesh_parity(alloc, terrain65, "terrain_65x65", 200, 50);

    std::cout << "PASS: xray_collision_bvh_parity_contract\n";
    return 0;
}
