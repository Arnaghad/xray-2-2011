#include "coordinate_transform.hpp"

#include <array>

namespace xray::maya_cook::detail {
namespace {

using matrix3 = std::array<std::array<double, 3>, 3>;

matrix3 engine_rotation(float3 r) {
    double const cx = std::cos(double(r.x)), sx = std::sin(double(r.x));
    double const cy = std::cos(double(r.y)), sy = std::sin(double(r.y));
    double const cz = std::cos(double(r.z)), sz = std::sin(double(r.z));
    // Row-vector matrix, matching xray::math::create_rotation exactly.
    return {{{cy * cz, -cy * sz, sy},
             {sy * sx * cz + cx * sz, -sx * sy * sz + cx * cz, -sx * cy},
             {-cx * sy * cz + sx * sz, cx * sy * sz + sx * cz, cx * cy}}};
}

matrix3 swap_yz_basis(matrix3 const& m) {
    constexpr std::array<int, 3> axes{0, 2, 1};
    matrix3 result{};
    for (int i = 0; i < 3; ++i)
        for (int j = 0; j < 3; ++j)
            result[i][j] = m[axes[i]][axes[j]];
    return result;
}

float3 engine_euler(matrix3 const& m) {
    double const cy = std::hypot(m[0][0], m[0][1]);
    double const y = std::atan2(m[0][2], cy);
    if (cy > 1e-10) {
        return {float(std::atan2(-m[1][2], m[2][2])), float(y),
                float(std::atan2(-m[0][1], m[0][0]))};
    }
    // At gimbal lock X=0 is a valid representative for either sign of Y.
    return {0.0f, float(y), float(std::atan2(m[1][0], m[1][1]))};
}

} // namespace

float3 coord_transform::euler(float3 r) const {
    if (swap_yz) {
        // Source RH XYZ Euler -> row matrix -> C^-1 R C -> engine LH XYZ.
        return engine_euler(swap_yz_basis(engine_rotation(-r)));
    }
    if (flip_z) r.z = -r.z;
    return r;
}

float3 coord_transform::inverse_euler(float3 r) const {
    if (swap_yz) return -engine_euler(swap_yz_basis(engine_rotation(r)));
    if (flip_z) r.z = -r.z;
    return r;
}

float3 coord_transform::inverse_pos(float3 p) const {
    p = dir(p); // Both coordinate maps are their own inverse.
    double const inverse_scale = 1.0 / double(scale);
    return {float(p.x * inverse_scale), float(p.y * inverse_scale), float(p.z * inverse_scale)};
}

float3 coord_transform::inverse_dimensions(float3 v) const {
    v = scaling(v);
    double const inverse_scale = 1.0 / double(scale);
    return {float(v.x * inverse_scale), float(v.y * inverse_scale), float(v.z * inverse_scale)};
}

coord_transform parse_coord_transform(json_value const& manifest) {
    auto const& cs = manifest.require("coord_space");
    auto const& system = cs.require("system").as_string();
    bool const v2 = manifest.require("schema").as_string() == "xray-dcc-exchange/v2";
    coord_transform xform;
    xform.flip_z = system == "maya_right_handed_y_up" || (v2 && system == "right_handed_y_up");
    xform.swap_yz = v2 && system == "blender_right_handed_z_up";
    if (!xform.right_handed() && system != "xray_left_handed_y_up")
        throw std::runtime_error("Invalid coord_space.system: " + system);
    auto const& uv = cs.require("uv_origin").as_string();
    if (uv != "bottom_left" && uv != "top_left")
        throw std::runtime_error("Invalid coord_space.uv_origin: " + uv);
    xform.flip_uv_v = uv == "bottom_left";
    auto const& winding = cs.require("front_face_winding").as_string();
    if (winding != "ccw_maya" && winding != "cw_xray" && !(v2 && winding == "ccw"))
        throw std::runtime_error("Invalid coord_space.front_face_winding: " + winding);
    xform.flip_winding = winding == "ccw_maya" || winding == "ccw";
    if (v2 && xform.flip_winding != xform.right_handed())
        throw std::runtime_error("coord_space.front_face_winding disagrees with coordinate system");
    xform.scale = static_cast<float>(cs.require("unit_scale_to_meters").as_double());
    if (!(xform.scale > 0.0f) || !std::isfinite(xform.scale))
        throw std::runtime_error("coord_space.unit_scale_to_meters must be positive finite");
    return xform;
}

} // namespace xray::maya_cook::detail
