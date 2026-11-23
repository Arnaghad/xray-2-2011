#include "pch.h"
#include "check_animation_data.h"
#include <xray/animation/cubic_spline_skeleton_animation.h>

#ifndef MASTER_GOLD
#include <xray/animation_math/legacy_check_data.hpp>
#include <xray/configs_lua_config.h>
#endif

namespace xray::animation {

animation_math::validation_result check_animation_samples(animation_math::discrete_data const& samples,
    cubic_spline_skeleton_animation const& animation, float precision_multiplier) {
    return animation_math::compare_samples(samples, animation.bones_count(),
        [&](std::size_t b, std::size_t c, float time) {
            float value = 0.0f;
            u32 domain = 0;
            animation.bone(u32(b)).channel(enum_channel_id(c)).evaluate(time, value, domain);
            return value;
        }, precision_multiplier);
}

#ifndef MASTER_GOLD
animation_math::discrete_data read_check_data(configs::lua_config_value const& config) {
    return animation_math::load_legacy_check_data(config);
}
#endif

#if XRAY_PLATFORM_WINDOWS && !defined(MASTER_GOLD)
bool compare(configs::lua_config_ptr check_data, cubic_spline_skeleton_animation const& animation) {
    if (!check_data) {
        LOG_ERROR("Animation check data is missing");
        return false;
    }
    try {
        auto const samples = read_check_data(check_data->get_root());
        auto const report = check_animation_samples(samples, animation, 2.0f);
        if (!report.passed())
            LOG_ERROR("Animation validation failed: bones_match=%d mismatches=%u bone=%u channel=%u frame=%u delta=%f",
                report.bone_count_matches, u32(report.mismatches), u32(report.bone), u32(report.channel), u32(report.frame), report.max_error);
        return report.passed();
    } catch (std::exception const& error) {
        LOG_ERROR("Animation check data is invalid: %s", error.what());
        return false;
    }
}
#endif

} // namespace xray::animation
