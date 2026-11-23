////////////////////////////////////////////////////////////////////////////
//	Created		: 09.06.2010
//	Author		: Konstantin Slipchenko
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef FIXED_BONE_H_INCLUDED
#define FIXED_BONE_H_INCLUDED

#include <xray/animation/skeleton.h>

namespace xray {

namespace render {
namespace debug {
	class renderer;
} // namespace debug
} // namespace render

namespace configs
{
	class lua_config_value;
	class binary_config_value;
} // namespace configs

namespace animation {

class skeleton;

struct fixed_bone {

	fixed_bone(): m_anim_skeleton_idx( u32(-1) ), m_fixed_vertex( float3(0,0,0 ) ){}


	u32		m_anim_skeleton_idx;	
	float3	m_fixed_vertex;
	
	template < typename ConfigValue >
	void read( ConfigValue const& config, const animation::skeleton &s )
	{
		pcstr bone_name = config["name"];
		
		m_anim_skeleton_idx = skeleton_bone_index( s, bone_name );
		ASSERT( m_anim_skeleton_idx != u32(-1), "fixed_bone :%s not found", bone_name );
		m_fixed_vertex = config["vertex"];
	}

}; // struct fixed_bone

struct animation_foot_steps
{
	
	static const u32 max_intervals = 11;
	fixed_vector< float, max_intervals > intervals;

}; // struct animation_foot_steps

struct foot_steps_bones
{
	
	fixed_vector< u32, animation_foot_steps::max_intervals - 1 > bones;

}; // struct foot_steps_bones

struct foot_steps
{
	
	foot_steps_bones				m_bones;
	vector< animation_foot_steps >	m_steps;
	
	inline	u32		intervals_size	( )const { return m_steps[ 0 ].intervals.size(); }
			void	get_intervals	( buffer_vector< float > &intervals, float start_time,  const buffer_vector< float > &weights )const;

			u32		bone			( float time, float start_time, const buffer_vector< float > &weights, float& interval_start )const;
			u32		bone			( float time, float start_time, const buffer_vector< float > &weights )const;

			void	render			( render::debug::base_renderer	&renderer, const float4x4 &matrix, u32 color, const buffer_vector< float > &weights, const buffer_vector< float > &time_scales, const float render_time_scale );

}; // struct foot_steps


class fixed_bones:
	private xray::noncopyable
{

public:
	fixed_bones(  const animation::skeleton &s, memory::base_allocator& allocator );

	template < typename ConfigValue >
	void				read			( ConfigValue const& config  )
	{
		const u32 sz = config.size();
		m_bones.resize( sz );
		for ( u32 i = 0 ; i < sz; ++i )
			m_bones[i].read( config[ i ], m_skeleton );
	}

	template < typename ConfigValue >
	void				read_steps		( foot_steps &steps, float play_time, ConfigValue const& config )const
	{
		const u32 sz_bones		= config["bones"].size();
		
		if ( sz_bones  == 0 )
			return;

		for ( u32 i = 0; i < sz_bones; ++i )
			steps.m_bones.bones.push_back( fixed_bone_idx( config["bones"][ i ] ) );

		const u32 sz_anims = config["intervals"].size();

		steps.m_steps.resize( sz_anims );

		for ( u32 j = 0; j < sz_anims; ++j )
		{
			const u32 sz_intervals	= config["intervals"][j].size();
			R_ASSERT( sz_intervals >= 2 );
			R_ASSERT( sz_bones + 1 == sz_intervals );

			for ( u32 i = 0; i < sz_intervals; ++i )
			{
				steps.m_steps[j].intervals.push_back( float ( config["intervals"][j][i] ) );
			}
			R_ASSERT_CMP( steps.m_steps[j].intervals[ sz_intervals - 2 ], <=, play_time  );

			steps.m_steps[j].intervals[ sz_intervals - 1 ] = play_time ;

		}
	}

	inline const fixed_bone	&bone		( u32 idx_fixed )const	{ return m_bones[ idx_fixed ]; }

private:
	u32					skeleton_bone_idx	( pcstr name )const;
	u32					fixed_bone_idx		( pcstr name )const;

private:
	const animation::skeleton		&m_skeleton;
	vectora< fixed_bone >			m_bones;

}; //struct fixed_bones

} // namespace animation
} // namespace xray

#endif // #ifndef FIXED_BONE_H_INCLUDED
