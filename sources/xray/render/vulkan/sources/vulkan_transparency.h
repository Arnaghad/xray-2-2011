#ifndef XRAY_VULKAN_TRANSPARENCY_H_INCLUDED
#define XRAY_VULKAN_TRANSPARENCY_H_INCLUDED
#include <xray/render/vulkan/model_resources.h>
#include <algorithm>
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
inline void sort_transparent_draws(std::vector<model_draw>& models,math::float4x4 const& view) {
    auto depth=[&](model_draw const& item) {
        double z=view.elements[3][2];
        for (unsigned c=0;c<3;++c) {
            double world=item.transform.elements[3][c];
            for (unsigned r=0;r<3;++r)
                world+=(double(item.mesh->minimum[r])+item.mesh->maximum[r])*.5*item.transform.elements[r][c];
            z+=world*view.elements[c][2];
        }
        return z;
    };
    for (auto const& item : models) if (!std::isfinite(depth(item))) throw std::runtime_error("Non-finite transparent sort depth");
    // Equal-depth surfaces preserve submission order. Intersecting surfaces need another technique.
    std::stable_sort(models.begin(),models.end(),[&](auto const& a,auto const& b) { return depth(a)>depth(b); });
}
} } }
#endif
