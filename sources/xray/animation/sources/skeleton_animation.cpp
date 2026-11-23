////////////////////////////////////////////////////////////////////////////
//	Created		: 25.01.2010
//	Author		: Konstantin Slipchenko
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "skeleton_animation.h"
#include "skeleton_animation_data.h"
#include "bone_animation_data.h"
#include "skeleton.h"

using xray::animation::skeleton;
using xray::animation::bone;

namespace xray {
namespace animation {

	skeleton_animation::skeleton_animation( skeleton_animation_data const& data )
	: managed_resource(0, resources::animation_class)
	{
		build( data, data.skeleton_bone_index() );
	}

	skeleton_animation::skeleton_animation( const skeleton_animation_data &data, vector< u32 > const& idx )
	: managed_resource(0, resources::animation_class)
	{
		build( data, idx );
	}

	void skeleton_animation::build( skeleton_animation_data const& data, vector< u32 > const& idx )
	{
		//const u32 bone_count = data.num_bones();
		//ASSERT( idx.size() == bone_count );
		
		const u32 bone_count = idx.size();
		ASSERT( data.num_bones() >= bone_count );
		
		pvoid mem_ptr = (pbyte)this + sizeof(*this);
		m_bone_data.resize( bone_count );
		for( u32 bone = 0; bone < bone_count; ++bone )
			for( u32 ch = channel_translation_x; ch < channel_max; ++ch )
			{
				const bi_spline_data< float > &b_spline = data.bone( idx[bone] ).channel( enum_channel_id (ch) ).bi_spline();
				m_bone_data[ bone ].channel( enum_channel_id (ch) ).create_in_place_internals( b_spline, mem_ptr );
				mem_ptr = get_shift_ptr( mem_ptr, animation_curve_type::count_internal_memory_size( b_spline ) );
			}
	}

} // namespace animation
} // namespace xray
