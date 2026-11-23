////////////////////////////////////////////////////////////////////////////
//	Created		: 17.02.2009
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef GEOMETRY_INSTANCE_H_INCLUDED
#define GEOMETRY_INSTANCE_H_INCLUDED

#include <xray/collision/geometry_instance.h>

namespace xray {
namespace render {
namespace debug {
	struct renderer;
} // namespace debug
} // namespace render

namespace collision {
namespace detail {

class geometry_instance :
	public collision::geometry_instance,
	private xray::noncopyable
{
public:
									geometry_instance	( memory::base_allocator* allocator, math::float4x4 const& matrix, non_null<geometry const>::ptr geometry );

	virtual	non_null<geometry const>::ptr	get_geometry	( ) const;


	virtual	void					set_matrix			( math::float4x4 const& matrix );
	virtual	math::float4x4 const&	get_matrix			( ) const;

	virtual	math::float3 const*		vertices			( ) const;
	virtual	u32						vertex_count		( ) const;
	virtual	u32 const*				indices				( ) const;
	virtual	u32 const*				indices				( u32 triangle_id ) const;
	virtual	u32						index_count			( ) const;

	virtual	bool					aabb_query			( object const* object, math::aabb const& aabb, triangles_type& triangles ) const;
	virtual	bool					cuboid_query		( object const* object, math::cuboid const& cuboid, triangles_type& triangles ) const;
	virtual	bool					ray_query			(
										object const* object,
										float3 const& origin,
										float3 const& direction,
										float max_distance,
										float& distance,
										ray_triangles_type& triangles,
										triangles_predicate_type const& predicate
									) const;

	virtual	bool					aabb_test			( math::aabb const& aabb ) const;
	virtual	bool					cuboid_test			( math::cuboid const& cuboid ) const;
	virtual	bool					ray_test			( float3 const& origin, float3 const& direction, float max_distance, float& distance ) const;

	virtual	void					generate_contacts	( on_contact& c, const xray::collision::geometry_instance& oi )	const;

	virtual	float					get_surface_area	( ) const;
	virtual	math::aabb				get_aabb			( ) const;
	virtual	math::aabb				get_geometry_aabb	( ) const;
	virtual	bool					is_valid			( ) const;
	virtual	memory::base_allocator&	get_allocator		( ) const { return *m_allocator; }
	virtual	void					render				( render::scene_ptr const& scene, render::debug::renderer& renderer ) const;
	virtual	void					render				( render::scene_ptr const& scene, render::debug::renderer& renderer, float4x4 const& transform ) const;

	virtual	void					enumerate_primitives( enumerate_primitives_callback& cb ) const;
	virtual	void					enumerate_primitives( float4x4 const& transform, enumerate_primitives_callback& cb ) const;

	virtual void					destroy				( memory::base_allocator* allocator );

	virtual	float3					get_random_surface_point( math::random32& randomizer ) const;
	virtual float3					get_closest_point_to( float3 const& point, float4x4 const& origin = float4x4().identity() ) const;

	XRAY_IMPLEMENT_COLLISION_GEOMETRY_VISIT_FUNCTIONS_NOT_IMPLEMENTED

protected:
	virtual	void					add_triangles		( triangles_type& triangles ) const;


private:
	float4x4						m_matrix;
	float4x4						m_inverted_matrix;
	math::aabb						m_aabb;
	non_null<geometry const>::ptr	m_geometry;
	memory::base_allocator*			m_allocator;
}; // class geometry_instance

} // namespace detail
} // namespace collision
} // namespace xray

#endif // #ifndef GEOMETRY_INSTANCE_H_INCLUDED
