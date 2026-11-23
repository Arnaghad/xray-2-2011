////////////////////////////////////////////////////////////////////////////
//	Created		: 19.01.2010
//	Author		: Konstantin Slipchenko
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef BI_SPLINE_SKELETON_ANIMATION_H_INCLUDED
#define BI_SPLINE_SKELETON_ANIMATION_H_INCLUDED

#include "bi_spline_bone_animation.h"
#include <xray/animation/bone_names.h>
#include <xray/animation/animation_event_channels.h>
#include <xray/fs_path_string.h>
#include <xray/animation/type_definitions.h>

namespace xray {
namespace animation {

class skeleton_animation;
class bone_names;
class skeleton;

class bi_spline_skeleton_animation : public resources::unmanaged_resource {
public:
    void set_animation_type(animation_types_enum type) { m_animation_type = type; }
    void finalize_bone_names() { m_bone_names->sort_by_crc(); }
										bi_spline_skeleton_animation	( );
										~bi_spline_skeleton_animation	( );
			void						set_num_bones					( u32 size );
			void						set_bone_names_num_bones		( u32 num );
			void						set_bone_name					( bone_index_type bone_index, pcstr name );
			void						set_bone_names					( const animation::bone_names &bn );

public:
			void						write							( stream &file ) const;
#ifndef MASTER_GOLD
			void						save							( configs::lua_config_value cfg )const;
#endif // #ifndef MASTER_GOLD
			template <typename T>
			void						load							( T const &cfg )
			{
				xray::configs::binary_config_value const cfg_data  = cfg["data"];
				const u32 size = cfg_data.size();
				m_bone_data.resize( size );
				for ( u32 i=0; i<size; ++i )
					m_bone_data[i].load( cfg_data[i] );
				////////////////////////////////////////////////
				set_bone_names_num_bones( size );
				m_bone_names->read( cfg["bone_names"] );
				
				pcstr const is_additive_string = "is_additive";
				m_animation_type	= (cfg.value_exists(is_additive_string) ? cfg[is_additive_string] : false) ? animation_type_additive : animation_type_full;
			}
			template <typename T>
			void						create_event_channels			( T const &cfg )
			{
				ASSERT( !m_event_channels );
				size_t internal_mem_size  = animation_event_channels::count_internal_memory_size( cfg );
				m_event_channels_memory = MALLOC( internal_mem_size + sizeof(animation_event_channels), "bi_spline_skeleton_animation::event_channels" );
				m_event_channels = new( m_event_channels_memory ) animation_event_channels;
				m_event_channels->create_in_place_internals( cfg, pbyte(m_event_channels_memory) + sizeof(animation_event_channels) );
			}
			bool						compare							( bi_spline_skeleton_animation const & skel )const;
			void						skeleton_bone_index				( skeleton const& skeleton, vector< u32 >& indices ) const;

public:
	inline	bi_spline_bone_animation&	bone							( u32 id )			{ return m_bone_data[ id ]; }
	inline	bi_spline_bone_animation const&	bone						( u32 id ) const	{ return m_bone_data[ id ]; } 
	inline	u32							bones_count						( ) const			{ return m_bone_data.size(); }
	inline	animation::bone_names const& bone_names						( ) const			{ return *m_bone_names; }
	inline	animation_event_channels const*	event_channels				( ) const			{ return m_event_channels; }
	inline	animation_types_enum		animation_type					( ) const			{ return m_animation_type; }

private:
	vector< bi_spline_bone_animation >	m_bone_data;
	animation_event_channels*			m_event_channels;
	pvoid								m_event_channels_memory;
	animation::bone_names*				m_bone_names;
	pvoid								m_bone_names_memory;
	animation_types_enum				m_animation_type;
}; // class bi_spline_skeleton_animation

} // namespace animation
} // namespace xray

#endif // #ifndef BI_SPLINE_SKELETON_ANIMATION_H_INCLUDED
