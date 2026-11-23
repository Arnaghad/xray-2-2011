////////////////////////////////////////////////////////////////////////////
//	Module 		: vec2f_script.cpp
//	Created 	: 13.08.2007
//  Modified 	: 13.08.2007
//	Author		: Dmitriy Iassenev
//	Description : 2D float vector class script export
////////////////////////////////////////////////////////////////////////////

#include "pch_script.h"

#pragma optimize("s",on)
void vec2f::script_export	(lua_State* state)
{
	sol::state_view sol_state(state);
	sol::table cs = sol_state["cs"].get_or_create<sol::table>();
	sol::table core = cs["core"].get_or_create<sol::table>();
	sol::table classes = core["classes"].get_or_create<sol::table>();

	classes.new_usertype<vec2f>("vec2f",
		sol::constructors<>(),
		"x", &vec2f::x,
		"y", &vec2f::y,
		"set", sol::overload(
			[](vec2f& self, float x, float y) -> vec2f& { return self.set(x, y); },
			[](vec2f& self, vec2f const& v) -> vec2f& { return self.set(v.x, v.y); }
		),
		"add", sol::overload(
			[](vec2f& self, vec2f const& v) -> vec2f& { return self.add(v); },
			[](vec2f& self, float v) -> vec2f& { return self.add(v); },
			[](vec2f& self, vec2f const& v1, vec2f const& v2) -> vec2f& { return self.add(v1, v2); },
			[](vec2f& self, vec2f const& v, float f) -> vec2f& { return self.add(v, f); }
		),
		"sub", sol::overload(
			[](vec2f& self, vec2f const& v) -> vec2f& { return self.sub(v); },
			[](vec2f& self, float v) -> vec2f& { return self.sub(v); },
			[](vec2f& self, vec2f const& v1, vec2f const& v2) -> vec2f& { return self.sub(v1, v2); },
			[](vec2f& self, vec2f const& v, float f) -> vec2f& { return self.sub(v, f); }
		),
		"mul", sol::overload(
			[](vec2f& self, vec2f const& v) -> vec2f& { return self.mul(v); },
			[](vec2f& self, float v) -> vec2f& { return self.mul(v); },
			[](vec2f& self, vec2f const& v1, vec2f const& v2) -> vec2f& { return self.mul(v1, v2); },
			[](vec2f& self, vec2f const& v, float f) -> vec2f& { return self.mul(v, f); }
		),
		"div", sol::overload(
			[](vec2f& self, vec2f const& v) -> vec2f& { return self.div(v); },
			[](vec2f& self, float v) -> vec2f& { return self.div(v); },
			[](vec2f& self, vec2f const& v1, vec2f const& v2) -> vec2f& { return self.div(v1, v2); },
			[](vec2f& self, vec2f const& v, float f) -> vec2f& { return self.div(v, f); }
		),
		"mad", sol::overload(
			[](vec2f& self, vec2f const& dir, float mult) -> vec2f& { return self.mad(self, dir, mult); },
			[](vec2f& self, vec2f const& point, vec2f const& dir, float mult) -> vec2f& { return self.mad(point, dir, mult); }
		),
		"similar", &vec2f::similar,
		"magnitude_sqr", &vec2f::magnitude_sqr,
		"magnitude", &vec2f::magnitude,
		"dot_product", &vec2f::dot_product,
		"cross_product", &vec2f::cross_product,
		"distance_to_sqr", &vec2f::distance_to_sqr,
		"distance_to", &vec2f::distance_to,
		"geth", &vec2f::geth
	);
}
