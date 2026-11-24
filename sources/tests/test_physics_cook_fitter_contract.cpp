#include "test_check.hpp"

#include "collision_primitive_fitter.hpp"
#include "xray/maya_cook/cook.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <random>
#include <string>
#include <vector>

namespace {

using xray::maya_cook::detail::fitted_obb;
using xray::maya_cook::detail::fitted_sphere;
using xray::maya_cook::detail::primitive_fitter_backend;
using xray::maya_cook::detail::vec3d;

double dot(vec3d const& a, vec3d const& b) {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

vec3d sub(vec3d const& a, vec3d const& b) {
    return {a.x - b.x, a.y - b.y, a.z - b.z};
}

vec3d cross(vec3d const& a, vec3d const& b) {
    return {
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x,
    };
}

void verify_obb_invariants(fitted_obb const& box, std::vector<vec3d> const& pts) {
    XRAY_TEST_CHECK(std::isfinite(box.center.x) && std::isfinite(box.center.y) && std::isfinite(box.center.z));
    for (int i = 0; i < 3; ++i) {
        XRAY_TEST_CHECK(std::isfinite(box.extent[i]) && box.extent[i] >= 0.0);
        XRAY_TEST_CHECK(std::abs(dot(box.axis[i], box.axis[i]) - 1.0) < 1e-6);
        int const next = (i + 1) % 3;
        XRAY_TEST_CHECK(std::abs(dot(box.axis[i], box.axis[next])) < 1e-6);
    }
    double const det = dot(cross(box.axis[0], box.axis[1]), box.axis[2]);
    XRAY_TEST_CHECK(std::abs(det - 1.0) < 1e-6);

    for (auto const& p : pts) {
        vec3d const d = sub(p, box.center);
        for (int i = 0; i < 3; ++i) {
            double const proj = std::abs(dot(d, box.axis[i]));
            XRAY_TEST_CHECK(proj <= box.extent[i] + 1e-4);
        }
    }
}

void verify_sphere_invariants(fitted_sphere const& sph, std::vector<vec3d> const& pts) {
    XRAY_TEST_CHECK(std::isfinite(sph.center.x) && std::isfinite(sph.center.y) && std::isfinite(sph.center.z));
    XRAY_TEST_CHECK(std::isfinite(sph.radius) && sph.radius >= 0.0);
    for (auto const& p : pts) {
        vec3d const d = sub(p, sph.center);
        double const dist = std::sqrt(dot(d, d));
        XRAY_TEST_CHECK(dist <= sph.radius + 1e-4);
    }
}

double obb_measure(fitted_obb const& b) {
    double const eps = 1e-6;
    return 8.0 * std::max(b.extent[0], eps) * std::max(b.extent[1], eps) * std::max(b.extent[2], eps);
}

void compare_fitters(
    char const* name,
    std::vector<vec3d> const& pts,
    double max_vol_ratio,
    bool compare_wm4_volume = true
) {
    fitted_obb const nat_box = xray::maya_cook::detail::fit_oriented_box(pts, primitive_fitter_backend::native_pca_welzl);
    fitted_sphere const nat_sph = xray::maya_cook::detail::fit_bounding_sphere(pts, primitive_fitter_backend::native_pca_welzl);
    verify_obb_invariants(nat_box, pts);
    verify_sphere_invariants(nat_sph, pts);

    if (xray::maya_cook::detail::is_primitive_fitter_backend_available(primitive_fitter_backend::wildmagic4)) {
        fitted_obb const wm_box = xray::maya_cook::detail::fit_oriented_box(pts, primitive_fitter_backend::wildmagic4);
        fitted_sphere const wm_sph = xray::maya_cook::detail::fit_bounding_sphere(pts, primitive_fitter_backend::wildmagic4);
        verify_obb_invariants(wm_box, pts);
        verify_sphere_invariants(wm_sph, pts);

        double const nat_r = nat_sph.radius;
        double const wm_r = wm_sph.radius;
        XRAY_TEST_CHECK(nat_r <= wm_r * 1.02 + 1e-5);
        XRAY_TEST_CHECK(wm_r <= nat_r * 1.02 + 1e-5);

        double const nat_v = obb_measure(nat_box);
        double const wm_v = obb_measure(wm_box);
        if (compare_wm4_volume) {
            double const ratio = nat_v / wm_v;
            XRAY_TEST_CHECK(ratio <= max_vol_ratio);
            std::cout << "FITTER [" << name << ", n=" << pts.size() << "]"
                      << " obb_ratio(native/wm4)=" << ratio
                      << ", sphere_r(native=" << nat_r << ", wm4=" << wm_r << ")\n";
        } else {
            std::cout << "FITTER [" << name << ", n=" << pts.size() << "]"
                      << " degenerate sphere_r(native=" << nat_r << ", wm4=" << wm_r << ")\n";
        }
    }
}

void test_exchange_v2_physics_cook_roundtrip() {
    namespace fs = std::filesystem;
    fs::path const tmp = fs::temp_directory_path() / "xray_stage05_physics_cook_test";
    std::error_code ec;
    fs::remove_all(tmp, ec);

    fs::path const bundle = tmp / "bundle";
    fs::path const raw_out = tmp / "raw";
    fs::path const conv_out = tmp / "converted";
    fs::create_directories(bundle);

    // Build payload.bin: 8 box vertices + 16 cylinder vertices (f32x3 each)
    std::vector<float> payload_floats = {
        // body0 box corners (-0.5..0.5, -1..1, -0.3..0.3)
        -0.5f, -1.0f, -0.3f,
         0.5f, -1.0f, -0.3f,
         0.5f,  1.0f, -0.3f,
        -0.5f,  1.0f, -0.3f,
        -0.5f, -1.0f,  0.3f,
         0.5f, -1.0f,  0.3f,
         0.5f,  1.0f,  0.3f,
        -0.5f,  1.0f,  0.3f,
    };
    std::uint32_t const box_bytes = static_cast<std::uint32_t>(payload_floats.size() * sizeof(float));

    // Add 12 points for sphere/cylinder
    std::uint32_t const sph_offset = box_bytes;
    for (int i = 0; i < 12; ++i) {
        double const a = static_cast<double>(i) * (3.14159265358979323846 / 6.0);
        payload_floats.push_back(static_cast<float>(0.4 * std::cos(a)));
        payload_floats.push_back((i % 2 == 0) ? -0.6f : 0.6f);
        payload_floats.push_back(static_cast<float>(0.4 * std::sin(a)));
    }

    {
        std::ofstream pf(bundle / "payload.bin", std::ios::binary);
        pf.write(reinterpret_cast<char const*>(payload_floats.data()), static_cast<std::streamsize>(payload_floats.size() * sizeof(float)));
        XRAY_TEST_CHECK(bool(pf));
    }

    std::string const box_span =
        "{\"format\":\"f32x3\",\"offset\":0,\"bytes\":" + std::to_string(box_bytes) + ",\"count\":8}";
    std::string const sph_span =
        "{\"format\":\"f32x3\",\"offset\":" + std::to_string(sph_offset) + ",\"bytes\":" + std::to_string(12u * 12u) + ",\"count\":12}";
    std::string const dof_json =
        "{\"enable_state\":true,\"limits_enable_state\":true,\"low_limit\":-0.5,\"hi_limit\":0.5}";
    std::string const manifest =
        "{\n"
        "  \"schema\": \"xray-dcc-exchange/v2\",\n"
        "  \"asset_kind\": \"physics\",\n"
        "  \"name\": \"props/stage05_ragdoll\",\n"
        "  \"source_dcc\": \"blender\",\n"
        "  \"coord_space\": {\n"
        "    \"system\": \"xray_left_handed_y_up\",\n"
        "    \"uv_origin\": \"top_left\",\n"
        "    \"front_face_winding\": \"cw_xray\",\n"
        "    \"unit_scale_to_meters\": 1.0\n"
        "  },\n"
        "  \"fit_requests\": [\n"
        "    {\"name\":\"fit_box\",\"shape\":\"box\",\"points\":" + box_span + "},\n"
        "    {\"name\":\"fit_cyl\",\"shape\":\"cylinder\",\"points\":" + sph_span + "},\n"
        "    {\"name\":\"fit_sph\",\"shape\":\"sphere\",\"points\":" + sph_span + "}\n"
        "  ],\n"
        "  \"bodies\": [\n"
        "    {\n"
        "      \"name\": \"torso\",\n"
        "      \"anim_joint\": \"spine\",\n"
        "      \"mass\": 25.0,\n"
        "      \"mass_center\": [0.0, 0.0, 0.0],\n"
        "      \"position\": [0.0, 1.0, 0.0],\n"
        "      \"rotation\": [0.0, 0.0, 0.0],\n"
        "      \"scale\": [1.0, 1.0, 1.0],\n"
        "      \"collision\": [\n"
        "        {\"type\": \"box\", \"points\": " + box_span + "},\n"
        "        {\"type\": \"cylinder\", \"points\": " + sph_span + "}\n"
        "      ]\n"
        "    },\n"
        "    {\n"
        "      \"name\": \"head\",\n"
        "      \"mass\": 5.0,\n"
        "      \"mass_center\": [0.0, 0.0, 0.0],\n"
        "      \"position\": [0.0, 2.2, 0.0],\n"
        "      \"rotation\": [0.0, 0.0, 0.0],\n"
        "      \"scale\": [1.0, 1.0, 1.0],\n"
        "      \"collision\": [\n"
        "        {\"type\": \"sphere\", \"position\": [0.0, 0.0, 0.0], \"rotation\": [0.0, 0.0, 0.0], \"scale\": [0.25, 0.25, 0.25]}\n"
        "      ]\n"
        "    }\n"
        "  ],\n"
        "  \"joints\": [\n"
        "    {\n"
        "      \"name\": \"neck\",\n"
        "      \"type\": \"ball\",\n"
        "      \"body0\": \"torso\",\n"
        "      \"body1\": \"head\",\n"
        "      \"position\": [0.0, 2.0, 0.0],\n"
        "      \"rotation\": [0.0, 0.0, 0.0],\n"
        "      \"scale\": [1.0, 1.0, 1.0],\n"
        "      \"x_translate\": " + dof_json + ",\n"
        "      \"y_translate\": " + dof_json + ",\n"
        "      \"z_translate\": " + dof_json + ",\n"
        "      \"x_rotate\": " + dof_json + ",\n"
        "      \"y_rotate\": " + dof_json + ",\n"
        "      \"z_rotate\": " + dof_json + "\n"
        "    }\n"
        "  ]\n"
        "}\n";

    {
        std::ofstream mf(bundle / "manifest.json", std::ios::binary);
        mf.write(manifest.data(), static_cast<std::streamsize>(manifest.size()));
        XRAY_TEST_CHECK(bool(mf));
    }

    std::string const fit_json = xray::maya_cook::fit_collision_bundle_to_json(bundle);
    XRAY_TEST_CHECK(fit_json.find("\"fit_box\"") != std::string::npos);
    XRAY_TEST_CHECK(fit_json.find("\"fit_cyl\"") != std::string::npos);
    XRAY_TEST_CHECK(fit_json.find("\"fit_sph\"") != std::string::npos);

    xray::maya_cook::cook_result const res = xray::maya_cook::cook_exchange_bundle(
        bundle, raw_out, conv_out
    );
    XRAY_TEST_CHECK(res.asset_kind == "physics");
    XRAY_TEST_CHECK(res.body_count == 2 && res.joint_count == 1 && res.has_collision);
    XRAY_TEST_CHECK(fs::is_regular_file(res.output_dir) && fs::file_size(res.output_dir) > 0);
    XRAY_TEST_CHECK(fs::is_regular_file(res.converted_dir) && fs::file_size(res.converted_dir) > 0);

    std::ifstream raw_in(res.output_dir, std::ios::binary);
    std::string const raw_txt((std::istreambuf_iterator<char>(raw_in)), {});
    XRAY_TEST_CHECK(raw_txt.find("torso") != std::string::npos);
    XRAY_TEST_CHECK(raw_txt.find("neck") != std::string::npos);

    fs::remove_all(tmp, ec);
}

} // namespace

int main() {
    // 1. Degenerate sets
    compare_fitters("single_point", {{1.5, -2.0, 0.7}}, 1.01);
    compare_fitters("duplicate_pts", {{0.2, 0.3, 0.4}, {0.2, 0.3, 0.4}, {0.2, 0.3, 0.4}}, 1.01, false);
    compare_fitters("segment_2pt", {{-1.0, 0.0, 0.0}, {3.0, 2.0, -1.0}}, 1.05, false);

    std::vector<vec3d> collinear;
    for (int i = -5; i <= 5; ++i) {
        collinear.push_back({1.0 * i, 2.0 * i, -1.0 * i});
    }
    compare_fitters("collinear_11pt", collinear, 1.05, false);

    // 2. Coplanar sets (rotated 37 deg in XZ, lifted along Y = 0.5x + 1)
    double const th = 37.0 * (3.14159265358979323846 / 180.0);
    double const ct = std::cos(th), st = std::sin(th);
    std::vector<vec3d> coplanar_rot;
    for (int ix = -2; ix <= 2; ++ix) {
        for (int iz = -1; iz <= 1; ++iz) {
            double const lx = ix * 1.4;
            double const lz = iz * 0.7;
            double const x = lx * ct - lz * st;
            double const z = lx * st + lz * ct;
            coplanar_rot.push_back({x, 0.5 * x - 0.3 * z + 1.0, z});
        }
    }
    compare_fitters("coplanar_tilted_rect", coplanar_rot, 1.05);

    // 3. Tilted 3D box (known exact OBB)
    std::vector<vec3d> tilted_box;
    for (int sx : {-1, 1}) {
        for (int sy : {-1, 1}) {
            for (int sz : {-1, 1}) {
                double const x0 = sx * 2.2, y0 = sy * 0.8, z0 = sz * 0.4;
                double const x1 = x0 * ct - z0 * st;
                double const z1 = x0 * st + z0 * ct;
                tilted_box.push_back({x1 + 1.0, y0 * 0.8 - z1 * 0.6 - 0.5, y0 * 0.6 + z1 * 0.8 + 2.0});
            }
        }
    }
    compare_fitters("tilted_box_8pt", tilted_box, 1.05);

    // 4. 500-point realistic humanoid limb/torso point cloud
    std::mt19937_64 rng(20110509ULL);
    std::uniform_real_distribution<double> u01(-1.0, 1.0);
    std::vector<vec3d> cloud_500;
    cloud_500.reserve(500);
    for (int i = 0; i < 500; ++i) {
        double const a = i * 0.13;
        double const h = u01(rng) * 0.9;
        double const rx = 0.28 + 0.04 * u01(rng);
        double const rz = 0.16 + 0.03 * u01(rng);
        double const lx = rx * std::cos(a);
        double const lz = rz * std::sin(a);
        cloud_500.push_back({
            lx * ct - lz * st + 0.15 * h,
            h + 1.0,
            lx * st + lz * ct - 0.1 * h,
        });
    }
    compare_fitters("cloud_500pt", cloud_500, 1.25);

    // 5. Full .physics cook & bundle JSON fit roundtrip
    test_exchange_v2_physics_cook_roundtrip();

    std::cout << "PASS: xray_physics_cook_fitter_contract\n";
    return 0;
}
