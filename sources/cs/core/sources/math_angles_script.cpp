////////////////////////////////////////////////////////////////////////////
//	Module 		: math_angles_script.cpp
//	Created 	: 13.08.2007
//  Modified 	: 13.08.2007
//	Author		: Dmitriy Iassenev
//	Description : angles functions script export
////////////////////////////////////////////////////////////////////////////

#include "pch_script.h"

#pragma optimize("s",on)

template <>
void gtl__class_script_exporter_dummy_::script_export	(lua_State* state)
{
	sol::state_view sol_state(state);
	sol::table cs = sol_state["cs"].get_or_create<sol::table>();
	sol::table core = cs["core"].get_or_create<sol::table>();

	core["angle_difference"] = &angle_difference;
	core["angle_normalize_signed"] = &angle_normalize_signed;
}
