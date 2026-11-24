////////////////////////////////////////////////////////////////////////////
//	Created		: 20.02.2009
//	Author		: Armen Abroyan
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "triangle_mesh_resource.h"
#if !(defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH)
#include "opcode_include.h"
#endif

using xray::collision::triangle_mesh_resource;
using xray::collision::object;
using xray::collision::ray_triangles_type;
using xray::collision::triangles_type;
using xray::collision::triangles_predicate_type;
using xray::memory::base_allocator;
using xray::math::aabb;
using xray::math::frustum;
using xray::math::cuboid;
using xray::math::float3;
using xray::math::float4x4;

namespace xray {
namespace collision {

struct header {
	u32					version;
	u32					vertex_count;
	u32					triangle_count;
	math::aabb			aabb;
}; // struct header

struct resource_guard : private xray::noncopyable {
	triangle_mesh_resource::indices_type const&	m_indices;
#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	triangle_mesh_resource const&		m_owner;
#else
	Opcode::MeshInterface*			mesh;
#endif
	resources::pinned_ptr_const<u8>		pinned_data;

#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
	inline	resource_guard	( resources::managed_resource_ptr const resource, triangle_mesh_resource const& owner, triangle_mesh_resource::indices_type const& indices ) :
		m_indices				( indices ),
		m_owner					( owner ),
		pinned_data				( resource )
	{
		pcbyte pointer			= pinned_data.c_ptr();

		header const& info		= (header const&)*pointer;
		pointer					+= sizeof( header );

		float3 const* vertices	= ( float3 const* )pointer;
		pointer					= ( pbyte )( vertices + info.vertex_count );

		const_cast<triangle_mesh_resource&>(m_owner).update_mesh_pointers( vertices, &*m_indices.begin() );
	}

	inline	~resource_guard	( )
	{
#ifdef DEBUG
		const_cast<triangle_mesh_resource&>(m_owner).update_mesh_pointers( 0, 0 );
#endif // #ifdef DEBUG
	}
#else
	inline	resource_guard	( resources::managed_resource_ptr const resource, Opcode::MeshInterface* mesh, triangle_mesh_resource::indices_type const& indices ) :
		m_indices				( indices ),
		mesh					( mesh ),
		pinned_data				( resource )
	{
		pcbyte pointer			= pinned_data.c_ptr();

		header const& info		= (header const&)*pointer;
		pointer					+= sizeof( header );

		float3 const* vertices	= ( float3 const* )pointer;
		pointer					= ( pbyte )( vertices + info.vertex_count );

		mesh->SetPointers		( ( IndexedTriangle const* )&*m_indices.begin(), ( Point const* )vertices );
	}

	inline	~resource_guard	( )
	{
#ifdef DEBUG
		mesh->SetPointers		( 0, 0 );
#endif // #ifdef DEBUG
	}
#endif
}; // struct resource_guard

} // namespace collision
} // namespace xray

using xray::collision::resource_guard;

triangle_mesh_resource::triangle_mesh_resource	( base_allocator* allocator, xray::resources::managed_resource_ptr const resource ) :
	super					( allocator ),
	m_indices				( *allocator ),
	m_resource				( resource )
{
	ASSERT					( m_resource );

	resources::pinned_ptr_const<u8> buffer	(m_resource);
	pcbyte pointer			= buffer.c_ptr();

	header const& info		= (header const&)*pointer;
	pointer					+= sizeof( header );

	float3 const* vertices	= ( float3 const* )pointer;
	pointer					= ( pbyte )( vertices + info.vertex_count );

	u32 const index_count	= info.triangle_count * 3;
	u32 const* indices		= ( u32 const* )pointer;
	R_ASSERT				( pbyte( indices + index_count ) <= buffer.c_ptr() + buffer.size() );

	// this should be temporary
	// remove material from indices
	// or change OPCODE interface to work
	// with strips of data (in a DirectX manner)
	m_indices.resize		( index_count );
	m_triangle_data.resize	( info.triangle_count );

	indices_type::iterator j     = m_indices.begin();
	vectora<u32>::iterator it_cd = m_triangle_data.begin();

	u32 const* i			= indices;
	u32 const* const e		= indices + info.triangle_count*4;
	for ( ; i != e; /*++i*/ ) {
		//for ( u32 k = 0; k < 3; ++k )
		//	*j++			= *i++;
		*j++				= *i++;	//copy index 0
		*j++				= *i++;	//copy index 1
		*j++				= *i++;	//copy index 2

		*it_cd++			= *i++;	//copy custom data
	}

	initialize				( allocator, vertices, info.vertex_count, &*m_indices.begin(), m_indices.size() );
}

#if defined(XRAY_COLLISION_USE_BULLET_BVH) && XRAY_COLLISION_USE_BULLET_BVH
#define XRAY_COLLISION_GUARD_ARG (*this)
#else
#define XRAY_COLLISION_GUARD_ARG mesh()
#endif

bool triangle_mesh_resource::aabb_query		( object const* const object, aabb const& aabb, triangles_type& triangles ) const
{
	resource_guard			guard( m_resource, XRAY_COLLISION_GUARD_ARG, m_indices );
	return					super::aabb_query( object, aabb, triangles );
}

bool triangle_mesh_resource::cuboid_query	( object const* object, cuboid const& cuboid, triangles_type& triangles ) const
{
	resource_guard			guard( m_resource, XRAY_COLLISION_GUARD_ARG, m_indices );
	return					super::cuboid_query( object, cuboid, triangles );
}

bool triangle_mesh_resource::ray_query		(
		object const* object,
		float3 const& origin,
		float3 const& direction,
		float max_distance,
		float& distance,
		ray_triangles_type& triangles,
		triangles_predicate_type const& predicate
	) const
{
	resource_guard			guard( m_resource, XRAY_COLLISION_GUARD_ARG, m_indices );
	return					super::ray_query( object, origin, direction, max_distance, distance, triangles, predicate );
}

void triangle_mesh_resource::render			( xray::render::scene_ptr const& scene, xray::render::debug::renderer& renderer, float4x4 const& matrix ) const
{
	resource_guard			guard( m_resource, XRAY_COLLISION_GUARD_ARG, m_indices );
	super::render			( scene, renderer, matrix );
}

void triangle_mesh_resource::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const collision::geometry& og ) const
{
	resource_guard			guard( m_resource, XRAY_COLLISION_GUARD_ARG, m_indices );
	super::generate_contacts( c, self_transform, transform, og );
}

void triangle_mesh_resource::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const collision::sphere_geometry& og ) const
{
	resource_guard			guard( m_resource, XRAY_COLLISION_GUARD_ARG, m_indices );
	super::generate_contacts( c, self_transform, transform, og );
}

void triangle_mesh_resource::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const box_geometry& og ) const
{
	resource_guard			guard( m_resource, XRAY_COLLISION_GUARD_ARG, m_indices );
	super::generate_contacts( c, self_transform, transform, og );
}

void triangle_mesh_resource::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const cylinder_geometry& og ) const
{
	resource_guard			guard( m_resource, XRAY_COLLISION_GUARD_ARG, m_indices );
	super::generate_contacts( c, self_transform, transform, og );
}

void triangle_mesh_resource::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const collision::triangle_mesh_base& og ) const
{
	resource_guard			guard( m_resource, XRAY_COLLISION_GUARD_ARG, m_indices );
	super::generate_contacts( c, self_transform, transform, og );
}

void triangle_mesh_resource::generate_contacts	( on_contact& c, const float4x4 &self_transform, const float4x4 &transform, const composite_geometry& og ) const
{
	resource_guard			guard( m_resource, XRAY_COLLISION_GUARD_ARG, m_indices );
	super::generate_contacts( c, self_transform, transform, og );
}
