////////////////////////////////////////////////////////////////////////////
//	Module 		: world_error.cpp
//	Created 	: 15.06.2005
//  Modified 	: 15.06.2005
//	Author		: Dmitriy Iassenev
//	Description : script engine class error handler
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "world.h"

using cs::script::detail::world;

static int s_pcall_handler(lua_State* state)
{
	// This replaces the old script_binding pcall callback
	// Simply forward the error message
	return 1;
}

void world::setup_callbacks			()
{
	lua_atpanic						(state(),world::on_panic);
}

int	world::on_panic					(lua_State* state)
{
	process_error					(state,"on_panic",LUA_ERRRUN);
	return							0;
}

int world::on_pcall_failed			(lua_State* state)
{
	process_error					(state,"on_pcall_failed",LUA_ERRRUN);

	if (lua_typestring(state,-1))
		lua_pop						(state,1);

	return							LUA_ERRRUN;
}

pcstr world::get_error_message		(int error_code)
{
	switch (error_code) {
		case LUA_ERRRUN :				return	"SCRIPT RUNTIME ERROR";
		case LUA_ERRMEM :				return	"SCRIPT ERROR (memory allocation)";
		case LUA_ERRERR :				return	"SCRIPT ERROR (while running the error handler function)";
		case LUA_ERRFILE :				return	"SCRIPT ERROR (while running file)";
		case LUA_ERRSYNTAX :			return	"SCRIPT SYNTAX ERROR";
		default :						NODEFAULT();
	}

#if CS_DEBUG_LIBRARIES || defined(__SNC__)
	return								0;
#endif
}

void world::error_message			(lua_State* state, int error_code)
{
	lua_world(state).msg			(cs::core::message_type_error,get_error_message(error_code));
}

bool world::process_script_output	(lua_State* state, pcstr file_name, int error_code)
{
	if (error_code)
		error_message				(state, error_code);

	if (!lua_typestring(state,-1))
		return						false;

	pcstr							S = lua_tostring(state,-1);
	if (!sz_cmp(S,"cannot resume dead coroutine"))
		lua_world(state).msg				(cs::core::message_type_error,"Please do not return any values from main thread function",file_name);
	else {
		if (!error_code)
			lua_world(state).msg			(cs::core::message_type_information,"Output from %s",file_name);
		lua_world(state).msg				(error_code ? cs::core::message_type_error : cs::core::message_type_output,"%s",S);
	}

	return							true;
}

void world::print_stack				(cs::core::message_type const& message_type)
{
	if (m_printing_stack)
		return;

	m_printing_stack				= true;

	lua_Debug						debug_info;
	for (int i=0; lua_getstack(state(),i,&debug_info);++i ) {
		lua_getinfo					(state(), "nSlu", &debug_info);
		if (!debug_info.name)
			msg						(
				message_type,
				"%2d : [%s] %s(%d) : %s",
				i,
				debug_info.what,
				debug_info.source ? debug_info.source + 1 : debug_info.short_src,
				debug_info.currentline,
				""
			);
		else
			if (!sz_cmp(debug_info.what,"C"))
				msg					(message_type, "%2d : [C  ] %s", i, debug_info.name);
			else
				msg					(
					message_type,
					"%2d : [%s] %s(%d) : %s",
					i,
					debug_info.what,
					debug_info.source ? debug_info.source + 1 : debug_info.short_src,
					debug_info.currentline,
					debug_info.name
				);
	}

	m_printing_stack				= false;
}
