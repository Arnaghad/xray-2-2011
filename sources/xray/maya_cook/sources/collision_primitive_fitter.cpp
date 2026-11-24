#include "collision_primitive_fitter.hpp"

#include <stdexcept>

namespace xray::maya_cook::detail {

primitive_fitter_backend default_primitive_fitter_backend() {
#if defined(XRAY_COOK_FITTER_USE_NATIVE) && XRAY_COOK_FITTER_USE_NATIVE
    return primitive_fitter_backend::native_pca_welzl;
#elif defined(XRAY_COOK_HAS_WM4) && XRAY_COOK_HAS_WM4
    return primitive_fitter_backend::wildmagic4;
#else
    return primitive_fitter_backend::native_pca_welzl;
#endif
}

bool is_primitive_fitter_backend_available(primitive_fitter_backend backend) {
    switch (backend) {
    case primitive_fitter_backend::wildmagic4:
#if defined(XRAY_COOK_HAS_WM4) && XRAY_COOK_HAS_WM4
        return true;
#else
        return false;
#endif
    case primitive_fitter_backend::native_pca_welzl:
        return true;
    }
    return false;
}

fitted_obb fit_oriented_box(
    std::span<vec3d const> points,
    primitive_fitter_backend backend
) {
    switch (backend) {
    case primitive_fitter_backend::wildmagic4:
#if defined(XRAY_COOK_HAS_WM4) && XRAY_COOK_HAS_WM4
        return fit_oriented_box_wm4(points);
#else
        throw std::runtime_error("WildMagic4 backend is disabled in this build");
#endif
    case primitive_fitter_backend::native_pca_welzl:
        return fit_oriented_box_native(points);
    }
    return fit_oriented_box_native(points);
}

fitted_sphere fit_bounding_sphere(
    std::span<vec3d const> points,
    primitive_fitter_backend backend
) {
    switch (backend) {
    case primitive_fitter_backend::wildmagic4:
#if defined(XRAY_COOK_HAS_WM4) && XRAY_COOK_HAS_WM4
        return fit_bounding_sphere_wm4(points);
#else
        throw std::runtime_error("WildMagic4 backend is disabled in this build");
#endif
    case primitive_fitter_backend::native_pca_welzl:
        return fit_bounding_sphere_native(points);
    }
    return fit_bounding_sphere_native(points);
}

} // namespace xray::maya_cook::detail
