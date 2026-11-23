#include "pch.h"
#include <xray/render/vulkan/gbuffer_resources.h>
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
gbuffer_camera_data gbuffer_camera(math::float4x4 const& projection) {
    for (auto const& row : projection.elements) for (float value : row)
        if (!std::isfinite(value)) throw std::runtime_error("Non-finite GBuffer projection");
    math::float4x4 inverse;
    if (!math::try_invert4x4(projection,inverse)) throw std::runtime_error("Singular GBuffer projection");
    gbuffer_camera_data data;
    for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) {
        if (!std::isfinite(inverse.elements[c][r])) throw std::runtime_error("Non-finite inverse GBuffer projection");
        data.inverse_projection[r*4+c]=inverse.elements[c][r];
    }
    auto depth=[&](float ndc) {
        auto p=inverse.transform(math::float4(0,0,ndc,1));
        if (!std::isfinite(p.w) || std::abs(p.w)<1e-8f) throw std::runtime_error("Infinite GBuffer projection range");
        return p.z/p.w;
    };
    data.near_plane=depth(0); data.far_plane=depth(1);
    if (!std::isfinite(data.near_plane) || !std::isfinite(data.far_plane) || data.near_plane<0 || data.far_plane<=data.near_plane)
        throw std::runtime_error("GBuffer requires finite forward-Z, nonnegative view depth");
    return data;
}
void validate_gbuffer_transform(math::float4x4 const& m) {
    for (auto const& row : m.elements) for (float value : row)
        if (!std::isfinite(value)) throw std::runtime_error("Non-finite GBuffer transform");
    if (m.elements[0][3]!=0 || m.elements[1][3]!=0 || m.elements[2][3]!=0 || m.elements[3][3]!=1)
        throw std::runtime_error("GBuffer view/model transforms must be affine");
    double a=m.elements[0][0],b=m.elements[0][1],c=m.elements[0][2];
    double d=m.elements[1][0],e=m.elements[1][1],f=m.elements[1][2];
    double g=m.elements[2][0],h=m.elements[2][1],i=m.elements[2][2];
    double scale=std::max({std::abs(a),std::abs(b),std::abs(c),std::abs(d),std::abs(e),std::abs(f),std::abs(g),std::abs(h),std::abs(i)});
    if (!scale || std::abs(a*(e*i-f*h)-b*(d*i-f*g)+c*(d*h-e*g))<=1e-8*scale*scale*scale)
        throw std::runtime_error("Singular/ill-conditioned GBuffer normal transform");
}
} } }
