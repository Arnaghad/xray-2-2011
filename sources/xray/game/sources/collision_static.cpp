////////////////////////////////////////////////////////////////////////////
//	Created		: 23.09.2009
//	Author		: Konstantin Slipchenko
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "collision_static.h"
#include <xray/collision/api.h>
#include <xray/physics/world.h>
#include <xray/collision/object.h>
#include <xray/collision/geometry_instance.h>
#include <xray/resources_pinned_ptr.h>
#include <xray/math_aabb.h>

namespace stalker2{

enum {
	object_type_geometry	= u32(1) << 0,
	object_type_object		= u32(1) << 1,
}; // enum

struct collision_mesh_header {
	u32					version;
	u32					vertex_count;
	u32					triangle_count;
	math::aabb			aabb;
};

collision_static::collision_static():
	m_collision			( 0 ),
	m_collision_object	( 0 ),
	m_instance			( 0 )
{
}

void collision_static::create( xray::resources::managed_resource_ptr const mesh_resource, physics::world &w  )
{
	ASSERT							( !m_collision );
	ASSERT							( !m_collision_object );

	resources::pinned_ptr_const<u8>	buffer	( mesh_resource );
	u8 const*						pointer	= buffer.c_ptr( );

	collision_mesh_header const&	info	= *( collision_mesh_header const* )pointer;
	pointer									+= sizeof( collision_mesh_header );

	float3 const*						vertices	= ( float3 const* )pointer;
	pointer											= ( u8 const* )( vertices + info.vertex_count );

	u32 const	index_count		= info.triangle_count * 3;
	u32 const*	raw_indices		= ( u32 const* )pointer;

	vectora<u32>	stripped_indices	( g_allocator );
	stripped_indices.resize				( index_count );
	vectora<u32>::iterator j			= stripped_indices.begin( );
	u32 const* i						= raw_indices;
	u32 const* const e					= raw_indices + info.triangle_count * 4;
	for ( ; i != e; )
	{
		*j++	= *i++;
		*j++	= *i++;
		*j++	= *i++;
		++i;
	}

	m_collision						= &*collision::new_triangle_mesh_geometry( g_allocator, vertices, info.vertex_count, &*stripped_indices.begin( ), index_count );
	m_instance						= &*collision::new_triangle_mesh_geometry_instance( g_allocator, float4x4().identity(), m_collision );
	m_collision_object				= &*collision::new_collision_object( g_allocator, object_type_geometry, m_instance );

	w.set_ready						( true );
}

void	collision_static::destroy( physics::world &w )
{
	if ( m_collision_object )
		collision::delete_object	( g_allocator, m_collision_object );

	if ( m_instance )
		collision::delete_geometry_instance	( g_allocator, m_instance );

	if ( m_collision )
		collision::delete_geometry	( g_allocator, m_collision );

	XRAY_UNREFERENCED_PARAMETER( w );
}

}
