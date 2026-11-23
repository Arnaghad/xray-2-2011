#ifndef XRAY_RENDER_LIGHTING_ENVIRONMENT_H_INCLUDED
#define XRAY_RENDER_LIGHTING_ENVIRONMENT_H_INCLUDED
#include <array>

namespace xray { namespace render {
struct lighting_environment {
    std::array<float,3> ambient={}; // Gamma-encoded ambient, as in combine_1.ps.
    std::array<float,3> fog_color={}; // Linear fog radiance.
    float fog_near=0,fog_distance=100;
    bool fog_enabled=false;
};
} }
#endif
