////////////////////////////////////////////////////////////////////////////
//	Module 		: sphere3f_script.cpp
//	Created 	: 23.07.2005
//  Modified 	: 23.07.2005
//	Author		: Dmitriy Iassenev
//	Description : 3D sphere class script export
////////////////////////////////////////////////////////////////////////////

#include "pch_script.h"

#pragma optimize("s",on)
void sphere3f::script_export(lua_State* state)
{
	sol::state_view sol_state(state);
	sol::table cs = sol_state["cs"].get_or_create<sol::table>();
	sol::table core = cs["core"].get_or_create<sol::table>();
	sol::table classes = core["classes"].get_or_create<sol::table>();

	classes.new_usertype<sphere3f>("sphere3f",
		sol::constructors<>(),
		"set", sol::overload(
			static_cast<sphere3f& (sphere3f::*)(float const, float const, float const, float const)>(&sphere3f::set),
			static_cast<sphere3f& (sphere3f::*)(vec3f const&, float const)>(&sphere3f::set)
		),
		"intersect", &sphere3f::intersect
	);
}
