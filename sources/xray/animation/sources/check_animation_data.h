#pragma once

#include <xray/animation/track_processing.h>

#if XRAY_PLATFORM_WINDOWS && !defined(MASTER_GOLD)
#include <xray/configs_lua_config.h>
namespace xray::animation {
bool compare(configs::lua_config_ptr check_data, cubic_spline_skeleton_animation const& animation);
} // namespace xray::animation
#endif
