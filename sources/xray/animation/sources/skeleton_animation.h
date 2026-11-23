#pragma once
////////////////////////////////////////////////////////////////////////////
//	Created		: 20.07.2011
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_ANIMATION_SKELETON_ANIMATION_H_INCLUDED
#define XRAY_ANIMATION_SKELETON_ANIMATION_H_INCLUDED

#include <xray/animation/bone_animation.h>
#include <xray/animation/type_definitions.h>

namespace xray {
namespace animation {

class skeleton_animation_data;

class skeleton_animation : public resources::managed_resource {
public:
	skeleton_animation( skeleton_animation_data const& data );
	skeleton_animation( const skeleton_animation_data &data, vector< u32 > const& idx );

	inline float max_time() const { return m_bone_data.empty() ? 0.f : m_bone_data[0].max_time(); }
	inline float min_time() const { return m_bone_data.empty() ? 0.f : m_bone_data[0].min_time(); }
	inline bone_animation& bone( u32 id ) { return m_bone_data[id]; }
	inline bone_animation const& bone( u32 id ) const { return m_bone_data[id]; }
	inline float blend_factor( float time, float transition_time ) const { XRAY_UNREFERENCED_PARAMETERS(time, transition_time); return 1.f; }

private:
	void build( skeleton_animation_data const& data, vector< u32 > const& idx );
	vector< bone_animation > m_bone_data;
};

} // namespace animation
} // namespace xray

#endif // #ifndef XRAY_ANIMATION_SKELETON_ANIMATION_H_INCLUDED
