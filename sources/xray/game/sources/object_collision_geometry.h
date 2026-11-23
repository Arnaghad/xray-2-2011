////////////////////////////////////////////////////////////////////////////
//	Created		: 16.08.2011
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef OBJECT_COLLISION_GEOMETRY_H_INCLUDED
#define OBJECT_COLLISION_GEOMETRY_H_INCLUDED

#include "object.h"

namespace xray
{ 
	namespace collision	
	{ 
		class	collision_object; 
		class	geometry_instance; 
		struct	primitive; 
		class	geometry; 
	} 
}

namespace stalker2
{
	enum object_collision_geometry_mode
	{
		object_collision_geometry_mode_mesh_containment,
		object_collision_geometry_mode_mesh_intersection
	};

	class object_collision_geometry: public game_object_static
	{
		typedef game_object_static		super;
	public:
		typedef buffer_vector< collision::geometry_instance* >	instances_container;
	public:
						object_collision_geometry	( game_world& w );
		virtual			~object_collision_geometry	( );

	public:
		virtual void	load						( configs::binary_config_value const& t );
		virtual void	load_contents				( );
		virtual void	unload_contents				( );
				bool	check_object_inside			( collision::geometry_instance const & testee );

				bool	is_obb_inside_obb			( float4x4 const& obb_transform, float3 const& anti_mesh, float4x4 const& anti_mesh_transform );
				bool	is_obb_inside_cylinder		( float4x4 const& obb_transform, float const cylinder_half_height, float const cylinder_radius, float4x4 const& cylinder_transform );
				bool	is_obb_inside_sphere		( float4x4 const& obb_transform, float3 const& sphere_center, float const& sphere_radius );
				bool	is_sphere_inside_obb		( float3 const& test_sphere_center, float const test_sphere_radius, float3 const& obb_half_sides, float4x4 const& obb_transform );
				bool	is_sphere_inside_cylinder	( float3 const& test_sphere_center, float const test_sphere_radius, float const cylinder_half_height, float const cylinder_radius, float4x4 const& cylinder_transform );
				bool	is_sphere_inside_sphere		( float3 const& test_sphere_center, float const test_sphere_radius, float3 const& sphere_center, float const& sphere_radius );
				bool	is_cylinder_inside_obb		( float4x4 const& test_cylinder_transform, float const test_cylinder_radius, float const test_cylinder_half_height, float3 const& obb_half_sides, float4x4 const& obb_transform );
				bool	is_cylinder_inside_cylinder	( float4x4 const& test_cylinder_transform, float const test_cylinder_radius, float const test_cylinder_half_height, float const cylinder_half_height, float const cylinder_radius, float4x4 const& cylinder_transform );
				bool	is_cylinder_inside_sphere	( float4x4 const& test_cylinder_transform, float const test_cylinder_radius, float const test_cylinder_half_height, float3 const& sphere_center, float const& sphere_radius );

	instances_container const& get_instances		( ) const { return m_instances; }
	private:
				bool	check_object_inside_containment_mode	( collision::geometry_instance const & testee );
				bool	check_object_inside_intersection_mode	( collision::geometry_instance const & testee );
				void	set_transform				( float4x4 const& transform );

	private:
		instances_container								m_instances;
		instances_container								m_anti_instances;
		instances_container								m_geometries;

		collision::collision_object*					m_anti_collision;
		collision::geometry*							m_anti_composite_geometry;
		object_collision_geometry_mode					m_mode;

#ifndef MASTER_GOLD
	private:
		void			draw_collision				( );

	private:
		bool											m_is_draw_collisions;
#endif

	}; // class object_collision_geometry

} // namespace stalker2

#endif // #ifndef OBJECT_COLLISION_GEOMETRY_H_INCLUDED
