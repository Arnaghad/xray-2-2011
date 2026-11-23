////////////////////////////////////////////////////////////////////////////
//	Module 		: vec3f_script.cpp
//	Created 	: 23.07.2005
//  Modified 	: 23.07.2005
//	Author		: Dmitriy Iassenev
//	Description : 3D float vector class script export
////////////////////////////////////////////////////////////////////////////

#include "pch_script.h"
#include <cs/gtl/random32.h>

static vec3f&	random_dir0		(vec3f& v)
{
	static gtl::random32 random;
	v.x		= random.random_f();
	v.y		= random.random_f();
	v.z		= random.random_f();
	return	v;
}

//static vec3f& random_dir1	(vec3f& v,vec3f const& cone_axis, float cone_angle)
//{
//	return	v.random_dir(cone_axis,cone_angle);
//}

//static vec3f&	random_point_box(vec3f& v,vec3f const& box_size)
//{
//	return	v.random_point_box(box_size);
//}

//static vec3f&	random_point_sphere(vec3f& v, float r)
//{
//	return	v.random_point_sphere(r);
//}

#pragma optimize("s",on)
void vec3f::script_export	(lua_State* state)
{
	sol::state_view sol_state(state);
	sol::table cs = sol_state["cs"].get_or_create<sol::table>();
	sol::table core = cs["core"].get_or_create<sol::table>();
	sol::table classes = core["classes"].get_or_create<sol::table>();

	classes.new_usertype<vec3f>("vec3f",
		sol::constructors<>(),
		"x", &vec3f::x,
		"y", &vec3f::y,
		"z", &vec3f::z,
		"set", sol::overload(
			[](vec3f& self, float x, float y, float z) -> vec3f& { return self.set(x, y, z); },
			[](vec3f& self, vec3f const& v) -> vec3f& { return self.set(v.x, v.y, v.z); }
		),
		"add", sol::overload(
			[](vec3f& self, vec3f const& v) -> vec3f& { return self.add(v); },
			[](vec3f& self, float v) -> vec3f& { return self.add(v); },
			[](vec3f& self, vec3f const& v1, vec3f const& v2) -> vec3f& { return self.add(v1, v2); },
			[](vec3f& self, vec3f const& v, float f) -> vec3f& { return self.add(v, f); }
		),
		"sub", sol::overload(
			[](vec3f& self, vec3f const& v) -> vec3f& { return self.sub(v); },
			[](vec3f& self, float v) -> vec3f& { return self.sub(v); },
			[](vec3f& self, vec3f const& v1, vec3f const& v2) -> vec3f& { return self.sub(v1, v2); },
			[](vec3f& self, vec3f const& v, float f) -> vec3f& { return self.sub(v, f); }
		),
		"mul", sol::overload(
			[](vec3f& self, vec3f const& v) -> vec3f& { return self.mul(v); },
			[](vec3f& self, float v) -> vec3f& { return self.mul(v); },
			[](vec3f& self, vec3f const& v1, vec3f const& v2) -> vec3f& { return self.mul(v1, v2); },
			[](vec3f& self, vec3f const& v, float f) -> vec3f& { return self.mul(v, f); }
		),
		"div", sol::overload(
			[](vec3f& self, vec3f const& v) -> vec3f& { return self.div(v); },
			[](vec3f& self, float v) -> vec3f& { return self.div(v); },
			[](vec3f& self, vec3f const& v1, vec3f const& v2) -> vec3f& { return self.div(v1, v2); },
			[](vec3f& self, vec3f const& v, float f) -> vec3f& { return self.div(v, f); }
		),
		"mad", sol::overload(
			[](vec3f& self, vec3f const& dir, float mult) -> vec3f& { return self.mad(self, dir, mult); },
			[](vec3f& self, vec3f const& point, vec3f const& dir, float mult) -> vec3f& { return self.mad(point, dir, mult); }
		),
		"min", &vec3f::min,
		"max", &vec3f::max,
		"abs", &vec3f::abs,
		"similar", sol::overload(
			[](vec3f const& self, vec3f const& v, float d) -> bool { return self.similar(v, d); },
			[](vec3f const& self, vec3f const& v1, vec3f const& v2) -> bool { return self.similar(v1, v2); }
		),
		"clamp", &vec3f::clamp,
		"magnitude_sqr", &vec3f::magnitude_sqr,
		"magnitude", &vec3f::magnitude,
		"normalize", sol::overload(
			[](vec3f& self) -> vec3f& { return self.normalize_safe(); },
			[](vec3f& self, vec3f const& v) -> vec3f& { 
				self.set(v.x, v.y, v.z); 
				return self.normalize_safe(); 
			}
		),
		"dot_product", &vec3f::dot_product,
		"cross_product", &vec3f::cross_product,
		"distance_to_sqr", &vec3f::distance_to_sqr,
		"distance_to", &vec3f::distance_to,
		"set_hp", &vec3f::set_angles_hp,
		"get_hp", [](vec3f& v) {
			float h, p;
			v.get_angles_hp(h, p);
			return std::make_tuple(h, p);
		},
		"geth", &vec3f::get_angles_h,
		"getp", &vec3f::get_angles_p,
		"random_dir", &random_dir0
	);
}
