////////////////////////////////////////////////////////////////////////////
// Created: 11.02.2009
// Author: Dmitriy Iassenev
// Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "colliders_ray_base.h"

namespace xray { namespace collision { namespace colliders {
ray_base::ray_base(float3 const& origin,float3 const& direction,float max_distance)
    : m_direction(direction),m_max_distance(max_distance) {
    static_cast<float3&>(m_origin)=origin;m_origin.padding=0;
    m_direction.normalize();
    static_cast<float3&>(m_inverted_direction)=float3(
        m_direction.x==0 ? 0 : 1/m_direction.x,
        m_direction.y==0 ? 0 : 1/m_direction.y,
        m_direction.z==0 ? 0 : 1/m_direction.z);
    m_inverted_direction.padding=0;
}
bool ray_base::intersects_aabb_sse(sse::aabb_a16 const& bounds,float& distance) const {
    auto box=math::create_aabb_min_max(bounds.min,bounds.max);math::float3 point;
    auto result=box.intersect(m_origin,m_direction,point);
    if (result==math::intersection_none) return false;
    distance=result==math::intersection_inside ? 0.f : (point-m_origin).length();
    return distance<=m_max_distance;
}
} } }
