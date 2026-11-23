////////////////////////////////////////////////////////////////////////////
//	Module 		: world_help.cpp
//	Created 	: 03.07.2005
//  Modified 	: 03.07.2005
//	Author		: Dmitriy Iassenev
//	Description : script engine class help 
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "world.h"
#include <cs/core/string_stream.h>

using cs::script::detail::world;

// Simplified listing using sol2 - script_binding class introspection is no longer available
// The detailed class_rep/construct_rep/overload_rep listing has been removed since
// sol2 doesn't expose internal class representation the same way.

static void list_free_functions(lua_State* state, sol::object const& object, pcstr header, std::string const& indent)
{
	CS_UNREFERENCED_PARAMETER( header );

	if (object.get_type() != sol::type::table)
		return;

	sol::table tbl = object.as<sol::table>();

	u32 count = 0;
	for (auto& [key, value] : tbl) {
		if (value.get_type() == sol::type::function) {
			++count;
		}
	}

	// Recurse into sub-tables
	{
		std::string _indent = indent;
		_indent.append("    ");

		for (auto& [key, value] : tbl) {
			if (value.get_type() == sol::type::table) {
				std::string key_str;
				if (key.get_type() == sol::type::string) {
					key_str = key.as<std::string>();
				} else {
					continue;
				}

				if (key_str != "_G" && key_str != "package" && key_str != "_M") {
					list_free_functions(state, value, key_str.c_str(), _indent);
				}
			}
		}
	}

	if (count) {
		msg(cs::message_initiator_script, cs::core::message_type_information, "%s};", indent.c_str());
	}
}

void world::list_exported_stuff			()
{
	msg					(cs::core::message_type_information," ");
	msg					(cs::core::message_type_information,"list of the classes exported to script");
	msg					(cs::core::message_type_information," ");

	// Note: detailed class listing is not available with sol2
	// (script_binding::detail::class_registry introspection has been removed)
	msg					(cs::core::message_type_information,"(detailed class listing not available with sol2 bindings)");

	msg					(cs::core::message_type_information,"end of list of the classes exported to script");
	msg					(cs::core::message_type_information," ");
	msg					(cs::core::message_type_information," ");
	msg					(cs::core::message_type_information,"list of the namespaces exported to script");

	list_free_functions	(state(), sol::state_view(state()).globals(), "", "");

	msg					(cs::core::message_type_information," ");
	msg					(cs::core::message_type_information,"end of list of the namespaces exported to script");
	msg					(cs::core::message_type_information," ");
}
