#pragma once

// Compatibility include for cooker clients; the implementation is DCC-neutral.
#include <xray/animation_math/bspline_fit.hpp>

namespace xray::maya_cook::detail {
using animation_math::evaluate_cubic_bspline;
using animation_math::fit_cubic_bspline_channel;
using animation_math::fitted_bspline_channel;
} // namespace xray::maya_cook::detail
