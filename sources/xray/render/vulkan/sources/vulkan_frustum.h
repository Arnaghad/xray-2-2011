#ifndef XRAY_VULKAN_FRUSTUM_H_INCLUDED
#define XRAY_VULKAN_FRUSTUM_H_INCLUDED
#include <xray/render/vulkan/library_linkage.h>
#include <xray/math_float4x4.h>
#include <array>

namespace xray { namespace render { namespace vulkan {
class XRAY_RENDER_VULKAN_API local_frustum {
    std::array<std::array<double,4>,6> m_planes={},m_magnitudes={};
    std::array<std::array<std::array<double,4>,4>,2> m_intermediates={};
    std::array<double,6> m_flush_error={};
    bool m_valid=true;
public:
    local_frustum(math::float4x4 const& world,math::float4x4 const& view,math::float4x4 const& projection);
    bool intersects(std::array<float,3> const& minimum,std::array<float,3> const& maximum) const;
};
} } }
#endif
