#pragma once

#include <span>

namespace xray::maya_cook::detail {

struct vec3d {
    double x = 0.0;
    double y = 0.0;
    double z = 0.0;
};

struct fitted_obb {
    vec3d center{};
    vec3d axis[3]{{1.0, 0.0, 0.0}, {0.0, 1.0, 0.0}, {0.0, 0.0, 1.0}};
    double extent[3]{0.0, 0.0, 0.0};
};

struct fitted_sphere {
    vec3d center{};
    double radius = 0.0;
};

enum class primitive_fitter_backend {
    wildmagic4,
    native_pca_welzl,
};

primitive_fitter_backend default_primitive_fitter_backend();
bool is_primitive_fitter_backend_available(primitive_fitter_backend backend);

fitted_obb fit_oriented_box(
    std::span<vec3d const> points,
    primitive_fitter_backend backend = default_primitive_fitter_backend()
);

fitted_sphere fit_bounding_sphere(
    std::span<vec3d const> points,
    primitive_fitter_backend backend = default_primitive_fitter_backend()
);

fitted_obb fit_oriented_box_wm4(std::span<vec3d const> points);
fitted_sphere fit_bounding_sphere_wm4(std::span<vec3d const> points);

fitted_obb fit_oriented_box_native(std::span<vec3d const> points);
fitted_sphere fit_bounding_sphere_native(std::span<vec3d const> points);

} // namespace xray::maya_cook::detail
