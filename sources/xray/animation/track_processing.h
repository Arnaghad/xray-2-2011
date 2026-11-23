#pragma once

#include <xray/animation/api.h>
#include <xray/animation_math/discrete_data.hpp>

#include <string>

namespace xray::animation {

class bi_spline_skeleton_animation;
class cubic_spline_skeleton_animation;

// Input samples are already in engine coordinates and 30-fps time units.
XRAY_ANIMATION_API void process_track(animation_math::discrete_data const& samples,
    std::span<std::string const> bone_names, bi_spline_skeleton_animation& output,
    bool is_additive = false);

XRAY_ANIMATION_API animation_math::validation_result check_animation_samples(
    animation_math::discrete_data const& samples, cubic_spline_skeleton_animation const& animation,
    float precision_multiplier = 1.0f);

#ifndef MASTER_GOLD
XRAY_ANIMATION_API animation_math::discrete_data read_check_data(configs::lua_config_value const& config);
#endif

} // namespace xray::animation
