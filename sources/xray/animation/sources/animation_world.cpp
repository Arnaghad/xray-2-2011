////////////////////////////////////////////////////////////////////////////
//	Created 	: 17.12.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "animation_world.h"
#include "bi_spline_skeleton_animation.h"
#include <xray/animation/skeleton_animation.h>
#include <xray/animation/skeleton.h>
#include <xray/animation/bone_names.h>
#include <xray/render/world.h>
#include <xray/render/base/debug_renderer.h>
#include <xray/render/facade/game_renderer.h>
#include <xray/resources_queries_result.h>
#include <xray/resources_fs.h>
#include <xray/animation/engine.h>
#include <xray/fs_utils.h>

#include "skeleton_animation_cook.h"
#include "cubic_spline_skeleton_animation_cook.h"
#include "skeleton_cook.h"
#include "single_animation_cook.h"
#include "animation_collection_cook.h"

namespace xray {

namespace animation {

command_line::key	test_animation("test_animation", "", "animation", "");

animation_world::animation_world(
		xray::animation::engine& engine,
		render::scene_ptr const& test_scene,
		render::scene_renderer* scene_renderer,
		render::debug::base_renderer* debug_renderer
	) :
	m_engine					( engine ),
	m_scene_renderer			( scene_renderer ),
	m_debug_renderer			( debug_renderer ),
	m_test_scene				( test_scene )
{
	static skeleton_animation_cook s_skeleton_animation_cook( *this );
	static cubic_spline_skeleton_animation_cook s_cubic_spline_skeleton_animation_cook;
	static skeleton_cook s_skeleton_cook;
	static single_animation_cook s_single_animation_cook;
	static animation_collection_cook s_animation_collection_cook;

}

void animation_world::set_test_scene( render::scene_ptr const& scene )
{
	m_test_scene = scene;
}

animation_world::~animation_world( )
{
}

void animation_world::tick( )
{
}

bi_spline_skeleton_animation *animation_world::create_skeleton_animation_data( u32 bones_count )
{
	bi_spline_skeleton_animation * ret = NEW(bi_spline_skeleton_animation)( );
	ret->set_num_bones( bones_count );
	return ret;
}

void	animation_world::destroy( bi_spline_skeleton_animation	* &p )
{
	DELETE( p );
}

void animation_world::clear_resources	( )
{
}

} // namespace animation
} // namespace xray
