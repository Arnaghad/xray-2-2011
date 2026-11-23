////////////////////////////////////////////////////////////////////////////
//	Module 		: matrix4f_script.cpp
//	Created 	: 12.10.2006
//  Modified 	: 12.10.2006
//	Author		: Dmitriy Iassenev
//	Description : float matrix with 4x4 components class script export
////////////////////////////////////////////////////////////////////////////

#include "pch_script.h"
#include <cs/core/matrix4f.h>

static vec4f* get_i(matrix4f* self)
{
	return	&self->i;
}

static vec4f* get_j(matrix4f* self)
{
	return	&self->j;
}

static vec4f* get_k(matrix4f* self)
{
	return	&self->k;
}

static vec4f* get_c(matrix4f* self)
{
	return	&self->c;
}

static void set_i(matrix4f* self, vec4f* object)
{
	self->i	= *object;
}

static void set_j(matrix4f* self, vec4f* object)
{
	self->j	= *object;
}

static void set_k(matrix4f* self, vec4f* object)
{
	self->k	= *object;
}

static void set_c(matrix4f* self, vec4f* object)
{
	self->c	= *object;
}

#pragma optimize("s",on)
void matrix4f::script_export	(lua_State* state)
{
	sol::state_view sol_state(state);
	sol::table cs = sol_state["cs"].get_or_create<sol::table>();
	sol::table core = cs["core"].get_or_create<sol::table>();
	sol::table classes = core["classes"].get_or_create<sol::table>();

	classes.new_usertype<matrix4f>("matrix4f",
		sol::constructors<>(),
		"i", sol::property(&get_i, &set_i),
		"j", sol::property(&get_j, &set_j),
		"k", sol::property(&get_k, &set_k),
		"c", sol::property(&get_c, &set_c)
	);
}
