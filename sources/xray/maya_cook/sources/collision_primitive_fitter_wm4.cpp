#include "collision_primitive_fitter.hpp"

#include "Wm4ContBox3.h"
#include "Wm4ContSphere3.h"

#include <cmath>
#include <stdexcept>
#include <vector>

namespace xray::maya_cook::detail {

namespace {

constexpr double kDefaultSinglePointRadius = 0.005;

bool wm_vec_is_finite(Wm4::Vector3d const& v) {
    return std::isfinite(v.X()) && std::isfinite(v.Y()) && std::isfinite(v.Z());
}

Wm4::Vector3d wm_normalize_or(Wm4::Vector3d v, Wm4::Vector3d const& fallback) {
    double const len = v.Length();
    if (!(len > 1e-12) || !std::isfinite(len)) {
        return fallback;
    }
    return v / len;
}

vec3d to_vec3d(Wm4::Vector3d const& v) {
    return {v.X(), v.Y(), v.Z()};
}

} // namespace

fitted_obb fit_oriented_box_wm4(std::span<vec3d const> points) {
    if (points.empty()) {
        throw std::runtime_error("fit_oriented_box_wm4 requires at least one point");
    }

    if (points.size() == 1) {
        fitted_obb single;
        single.center = points[0];
        single.extent[0] = kDefaultSinglePointRadius;
        single.extent[1] = kDefaultSinglePointRadius;
        single.extent[2] = kDefaultSinglePointRadius;
        return single;
    }

    std::vector<Wm4::Vector3d> pts;
    pts.reserve(points.size());
    for (auto const& p : points) {
        pts.emplace_back(p.x, p.y, p.z);
    }

    int const qty = static_cast<int>(pts.size());
    Wm4::Box3d box = Wm4::ContMinBox<double>(qty, pts.data(), 1.0e-5, Wm4::Query::QT_FILTERED);
    bool const valid = wm_vec_is_finite(box.Center) &&
        wm_vec_is_finite(box.Axis[0]) && wm_vec_is_finite(box.Axis[1]) && wm_vec_is_finite(box.Axis[2]) &&
        std::isfinite(box.Extent[0]) && std::isfinite(box.Extent[1]) && std::isfinite(box.Extent[2]);
    if (!valid || box.Axis[0].SquaredLength() < 1e-8 ||
        box.Axis[1].SquaredLength() < 1e-8 || box.Axis[2].SquaredLength() < 1e-8) {
        box = Wm4::ContOrientedBox<double>(qty, pts.data());
        if (!wm_vec_is_finite(box.Center) || box.Axis[0].SquaredLength() < 1e-8) {
            box = Wm4::ContAlignedBox<double>(qty, pts.data());
        }
    }

    Wm4::Vector3d u0 = wm_normalize_or(box.Axis[0], Wm4::Vector3d::UNIT_X);
    Wm4::Vector3d u1 = wm_normalize_or(box.Axis[1] - u0 * u0.Dot(box.Axis[1]), Wm4::Vector3d::UNIT_Y);
    if (std::abs(u0.Dot(u1)) > 0.1) {
        u1 = std::abs(u0.Y()) < 0.9 ? Wm4::Vector3d::UNIT_Y : Wm4::Vector3d::UNIT_Z;
        u1 = wm_normalize_or(u1 - u0 * u0.Dot(u1), Wm4::Vector3d::UNIT_Y);
    }
    Wm4::Vector3d u2 = u0.Cross(u1);
    if (u2.Dot(box.Axis[2]) < 0.0) {
        u2 = -u2;
    }

    fitted_obb out;
    out.center = to_vec3d(box.Center);
    out.axis[0] = to_vec3d(u0);
    out.axis[1] = to_vec3d(u1);
    out.axis[2] = to_vec3d(u2);
    out.extent[0] = std::abs(box.Extent[0]);
    out.extent[1] = std::abs(box.Extent[1]);
    out.extent[2] = std::abs(box.Extent[2]);
    return out;
}

fitted_sphere fit_bounding_sphere_wm4(std::span<vec3d const> points) {
    if (points.empty()) {
        throw std::runtime_error("fit_bounding_sphere_wm4 requires at least one point");
    }

    if (points.size() == 1) {
        return {points[0], kDefaultSinglePointRadius};
    }

    std::vector<Wm4::Vector3d> pts;
    pts.reserve(points.size());
    for (auto const& p : points) {
        pts.emplace_back(p.x, p.y, p.z);
    }

    int const qty = static_cast<int>(pts.size());
    Wm4::Sphere3d sphere;
    Wm4::MinSphere3<double>(qty, pts.data(), sphere, 1.0e-4);
    if (!wm_vec_is_finite(sphere.Center) || !std::isfinite(sphere.Radius) || sphere.Radius <= 0.0) {
        sphere = Wm4::ContSphereAverage<double>(qty, pts.data());
    }

    return {to_vec3d(sphere.Center), std::max(sphere.Radius, 0.0)};
}

} // namespace xray::maya_cook::detail
