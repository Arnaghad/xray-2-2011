////////////////////////////////////////////////////////////////////////////
//	Module 		: world.cpp
//	Created 	: 15.06.2005
//  Modified 	: 15.06.2005
//	Author		: Dmitriy Iassenev
//	Description : script engine class
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "world.h"
#include "thread.h"
#include "property_holder.h"
#include <cs/lua_studio_backend/interfaces.h>

using cs::script::detail::world;
using cs::script::detail::thread;
using cs::script::detail::property_holder;


lua_State* world::virtual_machine							() const
{
	return						state();
}

bool world::script_object									(
		pcstr object_description,
		script_binding::object& result,
		int object_type
	)
{
	return						object(object_description,result,object_type);
}

void world::execute_script									(pcstr file_name, bool force_to_reload)
{
	process_file				(file_name,force_to_reload);
}

cs::script::thread* world::create_thread		(char const* const& string_to_execute)
{
	return						cs_new< ::thread >(this, string_to_execute);
}

void CS_SCRIPT_CALL world::destroy_thread	(cs::script::thread*& thread)
{
	cs_delete					((::thread*&)thread);
}

cs::script::property_holder* CS_SCRIPT_CALL world::create_property_holder	(char const* file_name, u8 const flags)
{
	return						cs_new< ::property_holder >(this, file_name, flags);
}

void CS_SCRIPT_CALL world::destroy_property_holder							(cs::script::property_holder* &instance)
{
	cs_delete					(instance);
}
