////////////////////////////////////////////////////////////////////////////
//	Module 		: engine.cpp
//	Created 	: 12.04.2008
//  Modified 	: 12.04.2008
//	Author		: Dmitriy Iassenev
//	Description : engine class
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "engine.h"

int	engine::backend_luaL_loadstring						(lua_State* L, char const* s)
{
	return	::luaL_loadstring(L, s);
}

int	engine::backend_luaL_newmetatable					(lua_State* L, char const* tname)
{
	return	::luaL_newmetatable(L, tname);
}

void engine::backend_lua_createtable					(lua_State* L, int narray, int nrec)
{
	::lua_createtable(L, narray, nrec);
}

int	engine::backend_lua_sethook							(lua_State* L, lua_Hook func, lua_mask_type mask, int count)
{
	lua_sethook(L, func, mask, count);
	return 1;
}

engine::lua_Hook engine::backend_lua_gethook			(lua_State* L)
{
	return	::lua_gethook(L);
}

int	engine::backend_lua_getinfo							(lua_State* L, char const* what, lua_Debug* ar)
{
	return	::lua_getinfo(L, what, ar);
}

void engine::backend_lua_getfenv						(lua_State* L, int idx)
{
	lua_getglobal(L, "_ENV");
}

void engine::backend_lua_getfield						(lua_State* L, int idx, char const* k)
{
	::lua_getfield(L, idx, k);
}

char const*	engine::backend_lua_getlocal				(lua_State* L, lua_Debug const* ar, int n)
{
	return	::lua_getlocal(L, ar, n);
}

void engine::backend_lua_gettable						(lua_State* L, int idx)
{
	::lua_gettable(L, idx);
}

int	engine::backend_lua_getstack						(lua_State* L, int level, lua_Debug* ar)
{
	return	::lua_getstack(L, level, ar);
}

int	engine::backend_lua_gettop							(lua_State* L)
{
	return	::lua_gettop(L);
}

char const*	engine::backend_lua_getupvalue				(lua_State* L, int funcindex, int n)
{
	return	::lua_getupvalue(L, funcindex, n);
}

int	engine::backend_lua_iscfunction						(lua_State* L, int idx)
{
	return	::lua_iscfunction(L, idx);
}

int	engine::backend_lua_next							(lua_State* L, int idx)
{
	return	::lua_next(L, idx);
}

int	engine::backend_lua_pcall							(lua_State* L, int nargs, int nresults, int errfunc)
{
	return	::lua_pcall(L, nargs, nresults, errfunc);
}

void engine::backend_lua_pushcclosure					(lua_State* L, lua_CFunction fn, int n)
{
	::lua_pushcclosure(L, fn, n);
}

void engine::backend_lua_pushnil						(lua_State* L)
{
	::lua_pushnil(L);
}

void engine::backend_lua_pushstring						(lua_State* L, char const* s)
{
	::lua_pushstring(L, s);
}

void engine::backend_lua_pushvalue						(lua_State* L, int idx)
{
	::lua_pushvalue(L, idx);
}

void engine::backend_lua_pushnumber						(lua_State* L, lua_Number idx)
{
	::lua_pushnumber(L, idx);
}

void engine::backend_lua_remove							(lua_State* L, int idx)
{
	lua_remove(L, idx);
}

void engine::backend_lua_replace						(lua_State* L, int idx)
{
	lua_replace(L, idx);
}

int	engine::backend_lua_setfenv							(lua_State* L, int idx)
{
	// In Lua 5.2+, fenvs are replaced by _ENV.
	// Simple shim: set the global _ENV.
	lua_pushglobaltable(L);
	lua_setglobal(L, "_ENV");
	return 0;
}

int engine::backend_lua_setmetatable					(lua_State* L, int objindex)
{
	return	::lua_setmetatable(L, objindex);
}

void engine::backend_lua_settable						(lua_State* L, int idx)
{
	::lua_settable(L, idx);
}

void engine::backend_lua_settop							(lua_State* L, int idx)
{
	::lua_settop(L, idx);
}

int	engine::backend_lua_toboolean						(lua_State* L, int idx)
{
	return	::lua_toboolean(L, idx);
}

engine::lua_Integer	engine::backend_lua_tointeger		(lua_State* L, int idx)
{
	return	::lua_tointeger(L, idx);
}

char const* engine::backend_lua_tolstring				(lua_State* L, int idx, size_t* len)
{
	return	::lua_tolstring(L, idx, len);
}

lua_Number engine::backend_lua_tonumber					(lua_State* L, int idx)
{
	return	::lua_tonumber(L, idx);
}

void const* engine::backend_lua_topointer				(lua_State* L, int idx)
{
	return	::lua_topointer(L, idx);
}

bool engine::backend_lua_isnumber						(lua_State* L, int idx)
{
	return	!!::lua_isnumber(L, idx);
}

int	engine::backend_lua_type							(lua_State* L, int idx)
{
	return	::lua_type(L, idx);
}

char const* engine::backend_lua_typename				(lua_State* L, int t)
{
	return	::lua_typename(L, t);
}

lua_Debug* engine::backend_lua_debug_create				()
{
	ASSERT	(m_instance_count < sizeof(m_instances)/sizeof(m_instances[0]));
	return	&m_instances[m_instance_count++];
}

void engine::backend_lua_debug_destroy					(lua_Debug*& instance)
{
	instance	= 0;
	--m_instance_count;
}

char const*	engine::backend_lua_debug_get_name			(lua_Debug& instance)
{
	return	instance.name;
}

char const*	engine::backend_lua_debug_get_source		(lua_Debug& instance)
{
	return	instance.source;
}

char const*	engine::backend_lua_debug_get_short_source	(lua_Debug& instance)
{
	return	instance.short_src;
}

int	engine::backend_lua_debug_get_current_line			(lua_Debug& instance)
{
	return	instance.currentline;
}

void engine::log								(log_message_types const message_type, char const* const message)
{
	CS_UNREFERENCED_PARAMETER	( message_type );
	CS_UNREFERENCED_PARAMETER	( message );
}

char* engine::class_name						(char* const buffer, unsigned int const size, void* class_rep)
{
	return			sz_cpy(buffer, size, "unknown");
}

void engine::type_convert_class						(char* const buffer, unsigned int const size, lua_State* state, int index)
{
	sz_cpy					(buffer, size, "unknown");
}

static bool is_script_class						(lua_State* state, int const index)
{
	return				false;
}

bool engine::type_convert_instance					(char* buffer, unsigned int const size, lua_State* state, int index)
{
	return				false;
}

void engine::type_convert_userdata					(char* buffer, unsigned int const size, lua_State* state, int index)
{
	sz_cpy					(buffer, size, "unknown user data");
}

bool engine::type_to_string						(char* const buffer, unsigned int const size, lua_State* const state, int const index, bool& use_in_description)
{
	CS_UNREFERENCED_PARAMETER	( use_in_description );

	switch (lua_type(state, index)) {
		case engine::lua_type_string	:
		case engine::lua_type_table		:
		case engine::lua_type_nil		:
		case engine::lua_type_boolean	:
		case engine::lua_type_number	:
		case engine::lua_type_function	:
		case engine::lua_type_coroutine	:
			return			false;
		case engine::lua_type_light_user_data :
		case engine::lua_type_user_data	: {
			type_convert_userdata(buffer, size, state, index);
			return			true;
		}
		default							: NODEFAULT();
	} // switch (lua_type(state, index))

#if CS_DEBUG_LIBRARIES || defined(__SNC__)
	return					false;
#endif // #if CS_DEBUG_LIBRARIES
}

void engine::fill_class_info	(cs::lua_studio::backend& backend, char* const buffer, unsigned int const size, void* object, void* class_rep, lua_State* state)
{
	sz_cpy					(buffer, size, "{}");
}

void engine::value_convert_class	(cs::lua_studio::backend& backend, char* buffer, unsigned int size, void* class_rep, lua_State* state, int index, cs::lua_studio::icon_type& icon_type, bool const full_description)
{
	sz_cpy					(buffer, size, "{...}");
}

bool engine::value_convert_instance	(cs::lua_studio::backend& backend, char* buffer, unsigned int size, void* object, lua_State* state)
{
	return				false;
}

bool engine::value_convert_instance				(cs::lua_studio::backend& backend, char* buffer, unsigned int size, lua_State* state, int index, cs::lua_studio::icon_type& icon_type, bool full_description)
{
	if (lua_type(state, index) != LUA_TUSERDATA && lua_type(state, index) != LUA_TTABLE)
		return				false;

	char*	stream = buffer;
	int		i = 0;

	lua_pushvalue(state, index);
	while (lua_next(state, -2)) {
		i++;
		pcstr					name = lua_tostring(state, -2);

		string4096				type;
		bool					use_in_description;
		backend.type_to_string	(type, sizeof(type), state, -1, use_in_description);

		string4096				value;
		cs::lua_studio::icon_type	v_icon_type;
		backend.value_to_string	(value, sizeof(value), state, -1, v_icon_type, false);

		if (i == 1)
			stream += cs::sprintf(stream, size - u32(stream - buffer), "{");

		if (use_in_description)
			stream += cs::sprintf(stream, size - u32(stream - buffer), "%s[%s]=%s ", name, type, value);
		else
			stream += cs::sprintf(stream, size - u32(stream - buffer), "%s=%s ", name, value);

		lua_pop			(state, 1);
	}
	lua_pop			(state, 1);

	if (!i)
		return				false;

	stream += cs::sprintf(stream, size - u32(stream - buffer), "}%c", 0);
	icon_type = cs::lua_studio::icon_type_class_instance;
	return				true;
}

bool engine::value_to_string					(cs::lua_studio::backend& backend, char* const buffer, unsigned int const size, lua_State* const state, int const index, cs::lua_studio::icon_type& icon_type, bool const full_description)
{
	switch (lua_type(state, index)) {
		case engine::lua_type_string	:
		case engine::lua_type_table		:
		case engine::lua_type_nil		:
		case engine::lua_type_boolean	:
		case engine::lua_type_number	:
		case engine::lua_type_function	:
		case engine::lua_type_coroutine	:
			return			false;
		case engine::lua_type_light_user_data :
		case engine::lua_type_user_data	: {
			// updated a litte bit for sol2
			// check if it is a table using sol2
			if (sol::type_of(state, index) != sol::type::table) {
				if (!is_script_class(state, index)) {
					icon_type		= cs::lua_studio::icon_type_unknown;
					pcvoid			user_data = lua_topointer(state, index);
					cs::sprintf		(buffer, size, "0x%08x", static_cast<u32>(reinterpret_cast<std::uintptr_t>(user_data)));
					return			true;
				}

				sol::object class_rep(state, index);
				ASSERT				(class_rep.valid());
				value_convert_class	(backend, buffer, size, const_cast<void*>(class_rep.pointer()), state, index, icon_type, full_description);
				return				true;
			}

			// value_convert_class call was updated to use sol::object
			// call updated to use sol::object
			if (value_convert_instance(backend, buffer, size, state, index, icon_type, full_description))
				return				true;


				icon_type				= cs::lua_studio::icon_type_unknown;
				pcvoid					user_data = lua_topointer(state, index);
				cs::sprintf				(buffer, size, "0x%08x", static_cast<u32>(reinterpret_cast<std::uintptr_t>(user_data)));
				return					true;
		}
		default							: NODEFAULT();
	} // switch (lua_type(state, index))

#if CS_DEBUG_LIBRARIES || defined(__SNC__)
	return					false;
#endif // #if CS_DEBUG_LIBRARIES
}

void engine::push_class							(lua_State* const state, char const* const id)
{
}

void engine::push_class_base					(lua_State* const state, char const* const id)
{
}

void engine::push_class_instance				(lua_State* const state, char const* const id)
{
}

void engine::push_user_data						(lua_State* const state, char const* const id, cs::lua_studio::icon_type const icon_type)
{
}

bool engine::push_value							(lua_State* const state, char const* const id, cs::lua_studio::icon_type const icon_type)
{
	switch ( lua_type( state, -1 ) ) {
		case engine::lua_type_table :
			return			false;
		case engine::lua_type_light_user_data :
		case engine::lua_type_user_data : {
			push_user_data	(state, id, icon_type);
			return			true;
		}
		default						: NODEFAULT();
	}
#if CS_DEBUG_LIBRARIES || defined(__SNC__)
	return					false;
#endif // #if CS_DEBUG_LIBRARIES
}

void engine::fill_class_data					(
		cs::lua_studio::backend& backend,
		cs::lua_studio::value_to_expand& value_to_expand,
		lua_State* const state
	)
{
}


void engine::expand_class						(
		cs::lua_studio::backend& backend,
		cs::lua_studio::value_to_expand& value,
		lua_State* const state
	)
{
}

namespace cs {
namespace detail {

struct value_info {
	pcstr						m_name;
	pcstr						m_type;
	pcstr						m_value;
	cs::lua_studio::icon_type	m_icon_type;
}; // struct value_info

struct value {
	static inline bool predicate ( value_info const& left, value_info const& right )
	{
		return				sz_cmp( left.m_name, right.m_name ) < 0 ;
	}
}; // struct value

} // namespace detail
} // namespace cs

void engine::expand_class_instance				(
		cs::lua_studio::backend& backend,
		cs::lua_studio::value_to_expand& value_to_expand,
		lua_State* const state
	)
{
}
void engine::expand_user_data			(
		cs::lua_studio::backend& backend,
		cs::lua_studio::value_to_expand& value,
		lua_State* const state
	)
{
}


bool engine::expand_value						(
		cs::lua_studio::backend& backend,
		cs::lua_studio::value_to_expand& value,
		lua_State* const state
	)
{
	switch (lua_type(state, -1)) {
		case engine::lua_type_nil :
		case engine::lua_type_table :
			return			false;
		case engine::lua_type_light_user_data :
		case engine::lua_type_user_data : {
			expand_user_data( backend, value, state );
			return			true;
		}
		default : NODEFAULT();
	}

#if CS_DEBUG_LIBRARIES || defined(__SNC__)
	return					false;
#endif // #if CS_DEBUG_LIBRARIES
}

engine::engine									() :
	m_instance_count(0)
{
}
