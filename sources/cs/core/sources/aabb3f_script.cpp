////////////////////////////////////////////////////////////////////////////
//	Module 		: aabb3f_script.cpp
//	Created 	: 14.04.2007
//  Modified 	: 14.04.2007
//	Author		: Dmitriy Iassenev
//	Description : 3D float axes aligned bounding box (AABB) class script export
////////////////////////////////////////////////////////////////////////////

#include "pch_script.h"

#pragma optimize("s",on)
void aabb3f::script_export	(lua_State* state)
{
	sol::state_view sol_state(state);
	sol::table cs = sol_state["cs"].get_or_create<sol::table>();
	sol::table core = cs["core"].get_or_create<sol::table>();
	sol::table classes = core["classes"].get_or_create<sol::table>();

	classes.new_usertype<aabb3f>("aabb3f",
		sol::constructors<>(),
		"min", &aabb3f::min,
		"max", &aabb3f::max,
		"invalidate", &aabb3f::invalidate,
		"modify", &aabb3f::modify,
		"grow", sol::overload(
			static_cast<aabb3f& (aabb3f::*)(vec3f const&)>(&aabb3f::grow),
			static_cast<aabb3f& (aabb3f::*)(float const)>(&aabb3f::grow)
		),
		"merge", &aabb3f::merge,
		"inside", sol::overload(
			static_cast<bool (aabb3f::*)(aabb3f const&) const>(&aabb3f::inside),
			static_cast<bool (aabb3f::*)(vec3f const&, vec3f const&) const>(&aabb3f::inside)
		),
		"over", sol::overload(
			static_cast<bool (aabb3f::*)(aabb3f const&) const>(&aabb3f::over),
			static_cast<bool (aabb3f::*)(vec3f const&, vec3f const&) const>(&aabb3f::over)
		),
		"test", sol::overload(
			static_cast<bool (aabb3f::*)(aabb3f const&) const>(&aabb3f::test),
			static_cast<bool (aabb3f::*)(vec3f const&, vec3f const&) const>(&aabb3f::test)
		)
	);
}
