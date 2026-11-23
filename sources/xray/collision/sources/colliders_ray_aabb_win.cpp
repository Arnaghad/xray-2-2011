////////////////////////////////////////////////////////////////////////////
// Created: 11.02.2009
// Author: Dmitriy Iassenev
// Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "colliders_ray_aabb.h"

namespace xray { namespace collision { namespace colliders {
ray_aabb_collider::ray_aabb_collider(float3 const& origin,float3 const& direction) : m_direction(direction) {
    static_cast<float3&>(m_origin)=origin;m_origin.padding=0;
    m_direction.normalize();
    static_cast<float3&>(m_inverted_direction)=float3(
        m_direction.x==0 ? 0 : 1/m_direction.x,
        m_direction.y==0 ? 0 : 1/m_direction.y,
        m_direction.z==0 ? 0 : 1/m_direction.z);
    m_inverted_direction.padding=0;
}
bool ray_aabb_collider::intersects_aabb_sse(sse::aabb_a16 const& bounds,float& distance) const {
    auto box=math::create_aabb_min_max(bounds.min,bounds.max);math::float3 point;
    auto result=box.intersect(m_origin,m_direction,point);
    if (result==math::intersection_none) return false;
    distance=result==math::intersection_inside ? 0.f : (point-m_origin).length();
    return true;
}
} } }
