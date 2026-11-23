////////////////////////////////////////////////////////////////////////////
//	Module 		: vec4f_script.cpp
//	Created 	: 23.07.2005
//  Modified 	: 23.07.2005
//	Author		: Dmitriy Iassenev
//	Description : 4D float vector class script export
////////////////////////////////////////////////////////////////////////////

#include "pch_script.h"

static vec4f& set	(vec4f* self, float x, float y, float z, float w)
{
	return	self->set(x,y,z,w);
}

#pragma optimize("s",on)
void vec4f::script_export	(lua_State* state)
{
	sol::state_view sol_state(state);
	sol::table cs = sol_state["cs"].get_or_create<sol::table>();
	sol::table core = cs["core"].get_or_create<sol::table>();
	sol::table classes = core["classes"].get_or_create<sol::table>();

	classes.new_usertype<vec4f>("vec4f",
		sol::constructors<>(),
		"x", &vec4f::x,
		"y", &vec4f::y,
		"z", &vec4f::z,
		"w", &vec4f::w,
		"set", &::set
	);
}
