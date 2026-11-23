#include "pch.h"
#include "vulkan_frustum.h"
#include <cmath>
#include <limits>

namespace xray { namespace render { namespace vulkan {
local_frustum::local_frustum(math::float4x4 const& world,math::float4x4 const& view,math::float4x4 const& projection) {
    using matrix=std::array<std::array<double,4>,4>;
    matrix clip={},magnitude={};
    for (unsigned row=0;row<4;++row) for (unsigned col=0;col<4;++col) {
        double value=world.elements[row][col];
        if (!std::isfinite(value) || (value && std::abs(value)<std::numeric_limits<float>::min())) { m_valid=false; return; }
        clip[row][col]=value; magnitude[row][col]=std::abs(value);
    }
    m_intermediates[0]=magnitude;
    std::array<double,4> flush; flush.fill(8*std::numeric_limits<float>::min());
    // Row-vector W*V*P matches the transposed constants used by static_vs.
    // Track absolute products as well, so cancellation cannot erase the FP32 error margin.
    for (auto* transform : {&view,&projection}) {
        matrix next={},next_magnitude={};
        std::array<double,4> next_flush; next_flush.fill(8*std::numeric_limits<float>::min());
        for (unsigned row=0;row<4;++row) for (unsigned col=0;col<4;++col) for (unsigned k=0;k<4;++k) {
            double value=transform->elements[k][col];
            if (!std::isfinite(value) || (value && std::abs(value)<std::numeric_limits<float>::min())) { m_valid=false; return; }
            next[row][col]+=clip[row][k]*value;
            next_magnitude[row][col]+=magnitude[row][k]*std::abs(value);
        }
        for (unsigned col=0;col<4;++col) for (unsigned k=0;k<4;++k) next_flush[col]+=flush[k]*std::abs(double(transform->elements[k][col]));
        flush=next_flush;
        clip=next; magnitude=next_magnitude;
        if (transform==&view) m_intermediates[1]=magnitude;
    }
    // No perspective divide or plane normalization: reversed Z, infinite far,
    // mirrored/sheared worlds and finite singular projections remain conservative.
    for (unsigned row=0;row<4;++row) {
        for (unsigned axis=0;axis<2;++axis) for (unsigned side=0;side<2;++side) {
            unsigned plane=axis*2+side;
            m_planes[plane][row]=clip[row][3]+(side ? -clip[row][axis] : clip[row][axis]);
            m_magnitudes[plane][row]=magnitude[row][3]+magnitude[row][axis];
        }
        m_planes[4][row]=clip[row][2]; m_magnitudes[4][row]=magnitude[row][2];
        m_planes[5][row]=clip[row][3]-clip[row][2]; m_magnitudes[5][row]=magnitude[row][3]+magnitude[row][2];
    }
    m_flush_error={flush[3]+flush[0],flush[3]+flush[0],flush[3]+flush[1],flush[3]+flush[1],flush[2],flush[3]+flush[2]};
}
bool local_frustum::intersects(std::array<float,3> const& minimum,std::array<float,3> const& maximum) const {
    if (!m_valid) return true;
    for (unsigned axis=0;axis<3;++axis)
        if (!std::isfinite(minimum[axis]) || !std::isfinite(maximum[axis]) || minimum[axis]>maximum[axis]
            || (minimum[axis] && std::abs(minimum[axis])<std::numeric_limits<float>::min())
            || (maximum[axis] && std::abs(maximum[axis])<std::numeric_limits<float>::min())) return true;
    for (auto const& stage : m_intermediates) for (unsigned col=0;col<4;++col) {
        double magnitude=stage[3][col];
        for (unsigned axis=0;axis<3;++axis) magnitude+=stage[axis][col]*std::max(std::abs(double(minimum[axis])),std::abs(double(maximum[axis])));
        if (magnitude>std::numeric_limits<float>::max()/8) return true;
    }
    bool outside=false;
    for (unsigned i=0;i<6;++i) {
        auto const& plane=m_planes[i]; double support=plane[3],magnitude=m_magnitudes[i][3];
        for (unsigned axis=0;axis<3;++axis) {
            support+=plane[axis]*(plane[axis]>=0 ? maximum[axis] : minimum[axis]);
            magnitude+=m_magnitudes[i][axis]*std::max(std::abs(double(minimum[axis])),std::abs(double(maximum[axis])));
        }
        if (!std::isfinite(support) || magnitude>std::numeric_limits<float>::max()/8) return true;
        double error=64*(std::numeric_limits<float>::epsilon()*magnitude+m_flush_error[i]);
        outside=outside || support < -error;
    }
    return !outside;
}
} } }
