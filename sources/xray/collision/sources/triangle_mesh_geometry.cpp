////////////////////////////////////////////////////////////////////////////
//	Created		: 19.09.2011
//	Author		: Dmitry Kulikov
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "triangle_mesh_geometry.h"
#include <xray/render/facade/debug_renderer.h>
#if !(defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH)
#include "opcode_include.h"
#include "colliders_ray_query_geometry.h"
#include "colliders_ray_test_geometry.h"
#include "colliders_aabb_geometry.h"
#include "colliders_cuboid_geometry.h"
#include "colliders_convex_geometry.h"
#endif

using xray::math::aabb;
using xray::math::float3;
using xray::math::float4x4;
using xray::math::frustum;
using xray::math::cuboid;
using xray::collision::object;
using xray::collision::query_type;
using xray::collision::ray_triangles_type;
using xray::collision::triangles_type;
using xray::collision::triangles_predicate_type;
using xray::collision::triangle_mesh_geometry;

namespace xray {
namespace collision {

triangle_mesh_geometry::triangle_mesh_geometry	( memory::base_allocator* allocator ) :
	triangle_mesh_base		( allocator )
{
}

void triangle_mesh_geometry::initialize		( memory::base_allocator* allocator, float3 const* const vertices, u32 const vertex_count, u32 const* const indices, u32 const index_count )
{
	XRAY_UNREFERENCED_PARAMETER(allocator);
	triangle_mesh_base::initialize( vertices, vertex_count, indices, index_count );
}

void triangle_mesh_geometry::destroy	( memory::base_allocator* allocator )
{
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	XRAY_UNREFERENCED_PARAMETER(allocator);
	m_bullet_bvh.reset		( );
#else
	::g_allocator			= allocator;
	XRAY_DELETE_IMPL		( allocator, m_model );
	XRAY_DELETE_IMPL		( allocator, m_mesh );
	m_model					= 0;
	m_mesh					= 0;
#endif
}

triangle_mesh_geometry::~triangle_mesh_geometry		( )
{
}

#if !(defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH)
Opcode::AABBNoLeafNode const* triangle_mesh_geometry::root	( ) const
{
	ASSERT					( m_root );
	return					( m_root );
}
#endif

void triangle_mesh_geometry::add_triangles	( triangles_type& triangles ) const
{
	triangle_mesh_base::add_triangles	( triangles );
}

bool triangle_mesh_geometry::aabb_test		( aabb const& aabb ) const
{
	XRAY_UNREFERENCED_PARAMETER	(aabb);
	return true;
}

bool triangle_mesh_geometry::cuboid_test	( cuboid const& cuboid ) const
{
	XRAY_UNREFERENCED_PARAMETER	(cuboid);
	return true;
}

struct helper : private xray::noncopyable {
	inline	helper	( float& distance, bool& result ) :
		m_distance	( distance ),
		m_result	( result )
	{
	}

	bool predicate	( xray::collision::ray_triangle_result const& triangle ) const
	{
		if ( triangle.distance > m_distance )
			return	false; 

		m_distance	= triangle.distance;
		m_result	= true;
		return		false;
	}

private:
	float&			m_distance;
	bool&			m_result;
};

/*
bool triangle_mesh_geometry::ray_test		(
...
	return					result;
}
*/

void triangle_mesh_geometry::render			( xray::render::scene_ptr const& scene, xray::render::debug::renderer& renderer, float4x4 const& matrix ) const
{
	//u32 const vertex_buffer_size	= (m_mesh->GetNbVertices())*sizeof(vertex_colored);
	//u32 const index_buffer_size		= (m_mesh->GetNbIndices())*sizeof(u16);
	//bool allocated_in_stack			= false;
	//vertex_colored* vertices		= 0;
	//u16* indices					= 0;
	//if ( vertex_buffer_size + index_buffer_size < 900*1024 ) {
	//	allocated_in_stack			= true;
	//	vertex_colored* vertices	= pointer_cast<vertex_colored*>( ALLOCA(vertex_buffer_size) );
	//	u16* indices				= pointer_cast<u16*>( ALLOCA(index_buffer_size) );
	//}
	//else {
	//	UNREACHABLE_CODE();
	//}

	float3 const* const	verts		= vertices();
	u32 const* const	tris		= indices();
	u32 const			tri_count	= index_count() / 3;
	for ( u32 ti = 0; ti < tri_count; ++ti ) {
		u32 const* const tri_idx	= tris + ti * 3;
		float3 const positions[]	= {
			matrix.transform_position( verts[ tri_idx[0] ] ),
			matrix.transform_position( verts[ tri_idx[1] ] ),
			matrix.transform_position( verts[ tri_idx[2] ] ),
		};

		math::color clr(0, 127, 0, 127);
		using xray::render::vertex_colored;
		vertex_colored const draw_vertices[]=
		{
			vertex_colored(
				positions[0],
				clr.m_value
			),
			vertex_colored(
				positions[1],
				clr.m_value
			),
			vertex_colored(
				positions[2],
				clr.m_value
			)
		};
		renderer.draw_triangle( scene, draw_vertices[0], draw_vertices[1], draw_vertices[2] );
	}
}

bool triangle_mesh_geometry::aabb_query		( object const* object, aabb const& aabb, triangles_type& triangles ) const
{
	return					triangle_mesh_base::aabb_query( object, aabb, triangles );
}

bool triangle_mesh_geometry::cuboid_query	( object const* object, cuboid const& cuboid, triangles_type& triangles ) const
{
	return					triangle_mesh_base::cuboid_query( object, cuboid, triangles );
}

u32 triangle_mesh_geometry::get_triangles_count		( math::convex const& bounding_convex ) const
{
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	return					m_bullet_bvh.convex_triangle_count( bounding_convex );
#else
	return					colliders::convex_geometry( *this, bounding_convex ).result();
#endif
}


aabb triangle_mesh_geometry::get_aabb( ) const
{
	return					m_bounding_aabb;
}

float3 triangle_mesh_geometry::get_random_surface_point ( math::random32& randomizer ) const
{
	XRAY_UNREFERENCED_PARAMETER	( randomizer );
	UNREACHABLE_CODE			( return float3() );
}

float triangle_mesh_geometry::get_surface_area	( ) const
{
	// TODO: calculate real area as sum of triangles
	NOT_IMPLEMENTED				( return 0.f );
}





void triangle_mesh_geometry::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const collision::geometry& og ) const
{
	og.generate_contacts( c, transform, self_transform, *this );
}

void triangle_mesh_geometry::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const collision::sphere_geometry& og ) const
{
	XRAY_UNREFERENCED_PARAMETERS( &c, &self_transform, &transform, &og );
	NOT_IMPLEMENTED();
}

void triangle_mesh_geometry::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const box_geometry& og ) const
{
	XRAY_UNREFERENCED_PARAMETERS( &c, &self_transform, &transform, &og );
	NOT_IMPLEMENTED();
}

void triangle_mesh_geometry::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const cylinder_geometry& og ) const
{
	XRAY_UNREFERENCED_PARAMETERS( &c, &self_transform, &transform, &og );
	NOT_IMPLEMENTED();
}

void triangle_mesh_geometry::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const collision::triangle_mesh_base& og ) const
{
	XRAY_UNREFERENCED_PARAMETERS( &c, &self_transform, &transform, &og );
	NOT_IMPLEMENTED();
}

void triangle_mesh_geometry::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const composite_geometry& og ) const
{
	XRAY_UNREFERENCED_PARAMETERS( &c, &self_transform, &transform, &og );
	NOT_IMPLEMENTED();
}

//void triangle_mesh_geometry::accept	( geometry_double_dispatcher& dispatcher, geometry const& node ) const
//{
//	XRAY_UNREFERENCED_PARAMETERS		( &dispatcher, &node );
//	NOT_IMPLEMENTED( );
//}
//

//void triangle_mesh_geometry::visit	( geometry_double_dispatcher& dispatcher, box_geometry const& node ) const
//{
//	XRAY_UNREFERENCED_PARAMETERS		( &dispatcher, &node );
//	NOT_IMPLEMENTED( );
//}
//
//void triangle_mesh_geometry::visit	( geometry_double_dispatcher& dispatcher, sphere_geometry const& node ) const
//{
//	XRAY_UNREFERENCED_PARAMETERS		( &dispatcher, &node );
//	NOT_IMPLEMENTED( );
//}
//
//void triangle_mesh_geometry::visit	( geometry_double_dispatcher& dispatcher, cylinder_geometry const& node ) const
//{
//	XRAY_UNREFERENCED_PARAMETERS		( &dispatcher, &node );
//	NOT_IMPLEMENTED( );
//}

} // namespace collision
} // namespace xray
