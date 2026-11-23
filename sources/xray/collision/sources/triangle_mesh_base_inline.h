////////////////////////////////////////////////////////////////////////////
//	Created		: 10.03.2009
//	Author		: Konstantin Slipchenko
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef TRIANGLE_MESH_BASE_INLINE_H_INCLUDED
#define TRIANGLE_MESH_BASE_INLINE_H_INCLUDED

#include <xray/collision/contact_info.h>

namespace xray {
namespace collision {

template<class ContactTests>
void	triangle_mesh_base:: t_generate_contact_primitive( on_contact& c, const float4x4 &mesh_transform, const float4x4 &transform, const typename ContactTests::geometry_type& og )const
{
	XRAY_UNREFERENCED_PARAMETER	( mesh_transform );
	
	math::aabb const& query_aabb = ContactTests::get_query_aabb( transform, og );

	triangles_type	results(*c.alocator());
	float4x4 const mesh_transform_inverted ( invert4x3 ( mesh_transform ) );
	math::aabb query_aabb_local = query_aabb; query_aabb_local.modify( mesh_transform_inverted );

	colliders::aabb_geometry( *this, 0, query_aabb_local, results ); 
	
	buffer_vector<triangle_result>	al_res	( ALLOCA( sizeof(triangle_result) * results.size() ), results.size() );
	al_res.assign( results.begin(), results.end() );
	results.clear();
	
	buffer_vector<u32>	backs( ALLOCA( sizeof( u32 ) * al_res.size() ), al_res.size() );
	buffer_vector<typename ContactTests::result>	al_tri_res	( ALLOCA( sizeof( typename ContactTests::result ) * al_res.size() ), al_res.size() );

	ContactTests tests( og, transform, al_tri_res, c );

	buffer_vector<triangle_result>::const_iterator 	ie	= al_res.end();
	buffer_vector<triangle_result>::const_iterator	i	= al_res.begin();
	for( ;i!=ie; ++i )
	{
		const u32 *	ind	= indices( (*i).triangle_id );
		tests.test( backs, ind, vertices(), mesh_transform );
	}

	buffer_vector<u32>::const_iterator 	je	= backs.end();
	buffer_vector<u32>::const_iterator	j	= backs.begin();
	for( ;j!=je; ++j )
	{
		typename ContactTests::result &back_tri = al_tri_res[*j];
		if( !tests.trash_back( back_tri, vertices(), mesh_transform ) )
			tests.back_test( back_tri, vertices(), mesh_transform );
	}
}
}//xray {
}//collision {


#endif // #ifndef TRIANGLE_MESH_BASE_INLINE_H_INCLUDED
