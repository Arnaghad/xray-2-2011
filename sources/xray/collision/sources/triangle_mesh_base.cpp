////////////////////////////////////////////////////////////////////////////
//	Created		: 20.02.2009
//	Author		: Armen Abroyan
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "triangle_mesh_base.h"
#if !(defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH)
#include "opcode_include.h"
#include "colliders_ray_query_geometry.h"
#include "colliders_ray_test_geometry.h"
#include "colliders_aabb_geometry.h"
#include "colliders_cuboid_geometry.h"
#endif
#include "sphere_geometry.h"
#include "box_geometry.h"
#include "cylinder_geometry.h"

#include "triangle_sphere_contact_tests.h"
#include "triangle_box_contact_tests.h"
#include "triangle_cylinder_contact_tests.h"
#include "sphere_geometry_instance.h"
#include "box_geometry_instance.h"
#include "cylinder_geometry_instance.h"

#include <xray/collision/contact_info.h>
#include <xray/render/facade/debug_renderer.h>

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
using xray::collision::triangle_mesh_base;
using xray::memory::base_allocator;

triangle_mesh_base::triangle_mesh_base	( base_allocator* allocator )	:
	m_allocator				( allocator ),
	m_triangle_data			( *allocator ),
#if !(defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH)
	m_mesh					( 0 ),
	m_model					( 0 ),
	m_root					( 0 ),
#endif
	m_bounding_aabb			( math::create_zero_aabb() )
{
	::g_allocator			= allocator;
}

void triangle_mesh_base::add_triangles		( triangles_type& triangles ) const
{
	XRAY_UNREFERENCED_PARAMETER	(triangles);
}

void triangle_mesh_base::initialize		( float3 const* const vertices, u32 const vertex_count, u32 const* const indices, u32 const index_count )
{
	ASSERT					( (index_count % 3) == 0 );

#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	m_bullet_bvh.initialize	( vertices, vertex_count, indices, index_count );
#else
	m_mesh					= XRAY_NEW_IMPL ( m_allocator, Opcode::MeshInterface )( m_allocator );
	m_mesh->SetNbTriangles	( index_count / 3 );
	m_mesh->SetNbVertices	( vertex_count );
	m_mesh->SetPointers		( ( IndexedTriangle const* )indices, ( Point const* )vertices );

	Opcode::OPCODECREATE	options;
	options.mIMesh			= m_mesh;
	options.mSettings.mLimit= 1;
	options.mSettings.mRules= Opcode::SPLIT_SPLATTER_POINTS | Opcode::SPLIT_GEOM_CENTER;
	options.mKeepOriginal	= false;
	options.mNoLeaf			= true;
	options.mQuantized		= false;
	options.mCanRemap		= false;

	m_model					= XRAY_NEW_IMPL ( m_allocator, Opcode::Model )( m_allocator );
	m_model->Build			( options );

	m_root					= dynamic_cast<Opcode::AABBNoLeafTree const*>( m_model->GetTree( ) )->GetNodes( );
#endif

	calculate_aabb			( vertices, vertex_count, indices, index_count );
}

triangle_mesh_base::~triangle_mesh_base		( )
{
	::g_allocator			= m_allocator;
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	m_bullet_bvh.reset		( );
#else
	XRAY_DELETE_IMPL		( m_allocator, m_model );
	XRAY_DELETE_IMPL		( m_allocator, m_mesh );
#endif
}

#if !(defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH)
Opcode::AABBNoLeafNode const* triangle_mesh_base::root	( ) const
{
	ASSERT					( m_root );
	return					( m_root );
}
#else
void triangle_mesh_base::update_mesh_pointers( float3 const* const vertices, u32 const* const indices )
{
	m_bullet_bvh.update_pointers( vertices, indices );
}
#endif

float3 const* triangle_mesh_base::vertices				( ) const
{
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	return					( m_bullet_bvh.vertices( ) );
#else
	return					( ( float3 const* )m_mesh->GetVerts( ) );
#endif
}

u32 triangle_mesh_base::vertex_count					( ) const
{
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	return					( m_bullet_bvh.vertex_count( ) );
#else
	return					( m_mesh->GetNbVertices( ) );
#endif
}

u32 const* triangle_mesh_base::indices					( ) const
{
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	return					( m_bullet_bvh.indices( ) );
#else
	return					&m_mesh->GetTris( )->mVRef[0];
#endif
}


u32 const* triangle_mesh_base::indices					( u32 const triangle_id ) const
{
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	return					( m_bullet_bvh.indices( triangle_id ) );
#else
	return					( m_mesh->GetTris( )[ triangle_id ].mVRef );
#endif
}

u32 triangle_mesh_base::index_count						( ) const
{
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	return					( m_bullet_bvh.index_count( ) );
#else
	return					( 3*m_mesh->GetNbTriangles( ) );
#endif
}

bool triangle_mesh_base::aabb_test		( aabb const& aabb ) const
{
	XRAY_UNREFERENCED_PARAMETER	(aabb);
	return true;
}

bool triangle_mesh_base::cuboid_test	( cuboid const& cuboid ) const
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
		//m_distance	= xray::math::min( m_distance, triangle.distance );
		if( triangle.distance > m_distance )
			return	false; 

		m_distance	= triangle.distance;
		m_result	= true;
		return		false;
	}

private:
	float&			m_distance;
	bool&			m_result;
};

bool triangle_mesh_base::ray_test		(
		float3 const& origin,
		float3 const& direction,
		float max_distance,
		float& distance
	) const
{
	bool result				= false;
	helper					distance_helper( distance, result );
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	m_bullet_bvh.ray_test	(
		origin,
		direction,
		max_distance,
		triangles_predicate_type( &distance_helper, &helper::predicate )
	);
#else
	colliders::ray_test_geometry(
		*this,
		origin,
		direction,
		max_distance,
		triangles_predicate_type( &distance_helper, &helper::predicate )
	);
#endif
	return					result;
}

void triangle_mesh_base::render			( xray::render::scene_ptr const& scene, xray::render::debug::renderer& renderer, float4x4 const& matrix ) const
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

		float const step			= 10.f;
		float const inverted_step	= 1.f / step;

		using xray::render::vertex_colored;
		vertex_colored v0, v1, v2;
		v0.position = positions[0];
		v0.color = math::color(
					0*( math::clamp_r( ( positions[0].y - step*.5f) * inverted_step * 127.f + 127, 0.f, 255.f) ),
					1*( math::clamp_r( ( positions[0].y - step*.5f) * inverted_step * 127.f + 127, 0.f, 255.f) ),
					0*( math::clamp_r( ( positions[0].y - step*.5f) * inverted_step * 127.f + 127, 0.f, 255.f) ),
					255.0f
				).m_value;

		v1.position = positions[1];
		v1.color = math::color(
					0*( math::clamp_r( ( positions[1].y - step*.5f) * inverted_step * 127.f + 127, 0.f, 255.f) ),
					1*( math::clamp_r( ( positions[1].y - step*.5f) * inverted_step * 127.f + 127, 0.f, 255.f) ),
					0*( math::clamp_r( ( positions[1].y - step*.5f) * inverted_step * 127.f + 127, 0.f, 255.f) ),
					255.0f
				).m_value;

		v2.position = positions[2];
		v2.color = math::color(
					0*( math::clamp_r( ( positions[2].y - step*.5f) * inverted_step * 127.f + 127, 0.f, 255.f) ),
					1*( math::clamp_r( ( positions[2].y - step*.5f) * inverted_step * 127.f + 127, 0.f, 255.f) ),
					0*( math::clamp_r( ( positions[2].y - step*.5f) * inverted_step * 127.f + 127, 0.f, 255.f) ),
					255.0f
				).m_value;

		renderer.draw_triangle( scene, v0, v1, v2 );
	}
}

bool triangle_mesh_base::aabb_query		( object const* object, aabb const& aabb, triangles_type& triangles ) const
{
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	return					m_bullet_bvh.aabb_query( object, aabb, triangles );
#else
	return					colliders::aabb_geometry( *this, object, aabb, triangles ).result( );
#endif
}

bool triangle_mesh_base::cuboid_query	( object const* object, cuboid const& cuboid, triangles_type& triangles ) const
{
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	return					m_bullet_bvh.cuboid_query( object, cuboid, triangles );
#else
	return					colliders::cuboid_geometry	( *this, object, cuboid, triangles ).result( );
#endif
}

bool triangle_mesh_base::ray_query		(
		object const* object,
		float3 const& origin,
		float3 const& direction,
		float max_distance,
		float& distance,
		ray_triangles_type& triangles,
		triangles_predicate_type const& predicate
	) const
{
	XRAY_UNREFERENCED_PARAMETER	(distance);

#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	bool const result		= m_bullet_bvh.ray_query( object, origin, direction, max_distance, triangles, predicate );
#else
	bool const result		= colliders::ray_query_geometry( *this, object, origin, direction, max_distance, triangles, predicate ).result( );
#endif
	if ( !result )
		return				( false );

	return					( true );
}

void triangle_mesh_base::calculate_aabb	( float3 const* const vertices, u32 const vertex_count, u32 const* const indices, u32 const index_count )
{
	XRAY_UNREFERENCED_PARAMETERS	(indices, index_count);

	ASSERT					( vertex_count );
	if ( !vertex_count )
		return;

	float3 const* i			= vertices;
	float3 const* const e	= vertices + vertex_count;

	float3 min				= *i;
	float3 max				= *i;
	for ( ++i; i != e; ++i ) {
		min					= math::min( min, *i );
		max					= math::max( max, *i );
	}

	m_bounding_aabb.min		= min;
	m_bounding_aabb.max		= max;
}

aabb&	triangle_mesh_base::get_aabb( aabb& result ) const
{
	result					= m_bounding_aabb;
	return					result;
}
void		triangle_mesh_base::generate_contacts( xray::collision::on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const xray::collision::geometry& og )const
{
	og.generate_contacts( c, transform, self_transform, *this );
}
void	triangle_mesh_base::generate_contacts( xray::collision::on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const xray::collision::sphere_geometry& og ) const 
{
	sphere_geometry_instance instance( transform, og.radius() );
	t_generate_contact_primitive<detail::triangle_sphere_contact_tests>( c, self_transform,  transform, instance );
}

void triangle_mesh_base::generate_contacts( xray::collision::on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const xray::collision::box_geometry& og )const
{
	box_geometry_instance instance( transform );
	t_generate_contact_primitive<detail::triangle_box_contact_tests>( c, self_transform,  transform, instance );
}

void triangle_mesh_base::generate_contacts( xray::collision::on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const xray::collision::cylinder_geometry& og )const
{
	cylinder_geometry_instance instance( transform, og.radius(), og.half_length() );
	t_generate_contact_primitive<detail::triangle_cylinder_contact_tests>( c, self_transform,  transform, instance );
}

void triangle_mesh_base::generate_contacts ( xray::collision::on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const xray::collision::triangle_mesh_base& og ) const
{
	XRAY_UNREFERENCED_PARAMETERS	(&c, &self_transform, &transform, &og);
}

void triangle_mesh_base::generate_contacts ( xray::collision::on_contact& c, const float4x4 &self_transform, const float4x4 &transform,  const xray::collision::composite_geometry& og ) const	
{
	XRAY_UNREFERENCED_PARAMETERS	(&c, &self_transform, &transform, &og);
}
