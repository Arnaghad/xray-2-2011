////////////////////////////////////////////////////////////////////////////
//	Stage 05 Modernization: Bullet btOptimizedBvh mesh query backend
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_COLLISION_BULLET_MESH_BVH_H_INCLUDED
#define XRAY_COLLISION_BULLET_MESH_BVH_H_INCLUDED

#include <xray/collision/common_types.h>
#include <xray/collision/object.h>

#include <memory>

class btOptimizedBvh;
class btTriangleIndexVertexArray;

int triBoxOverlap( xray::math::float3 const& boxcenter, xray::math::float3 const& boxhalfsize, xray::math::float3 const (&triverts)[3] );

namespace xray {
namespace math {
	class convex;
} // namespace math

namespace collision {

class XRAY_COLLISION_API bullet_mesh_bvh : private xray::noncopyable {
public:
						bullet_mesh_bvh		( );
						~bullet_mesh_bvh	( );

	void				initialize			(
												float3 const* vertices,
												u32 vertex_count,
												u32 const* indices,
												u32 index_count
											);
	void				reset				( );
	void				update_pointers		( float3 const* vertices, u32 const* indices );

	float3 const*		vertices			( ) const { return m_vertices; }
	u32					vertex_count		( ) const { return m_vertex_count; }
	u32 const*			indices				( ) const { return m_indices; }
	u32 const*			indices				( u32 triangle_id ) const { return m_indices + triangle_id * 3; }
	u32					index_count			( ) const { return m_index_count; }
	u32					triangle_count		( ) const { return m_index_count / 3; }
	math::aabb const&	aabb				( ) const { return m_aabb; }

	bool				aabb_query			(
												object const* obj,
												math::aabb const& query_aabb,
												triangles_type& triangles
											) const;
	bool				cuboid_query		(
												object const* obj,
												math::cuboid const& query_cuboid,
												triangles_type& triangles
											) const;
	bool				ray_query			(
												object const* obj,
												float3 const& origin,
												float3 const& direction,
												float max_distance,
												ray_triangles_type& triangles,
												triangles_predicate_type const& predicate
											) const;
	bool				ray_test			(
												float3 const& origin,
												float3 const& direction,
												float max_distance,
												triangles_predicate_type const& predicate
											) const;
	u32					convex_triangle_count( math::convex const& bounding_convex ) const;

private:
	float3 const*								m_vertices;
	u32											m_vertex_count;
	u32 const*									m_indices;
	u32											m_index_count;
	math::aabb									m_aabb;
	std::unique_ptr<btTriangleIndexVertexArray>	m_mesh_interface;
	std::unique_ptr<btOptimizedBvh>				m_bvh;
}; // class bullet_mesh_bvh

} // namespace collision
} // namespace xray

#endif // #ifndef XRAY_COLLISION_BULLET_MESH_BVH_H_INCLUDED
