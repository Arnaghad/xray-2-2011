////////////////////////////////////////////////////////////////////////////
//	Created		: 10.05.2026
//	Author		: Antigravity
//	Copyright (C) GSC Game World - 2026
////////////////////////////////////////////////////////////////////////////

#ifndef TOOL_BASE_H_INCLUDED
#define TOOL_BASE_H_INCLUDED

#include "object_base.h"

namespace stalker2 {
	class game_world;
} // namespace stalker2

namespace xray {

namespace configs {
	class lua_config_value;
} // namespace configs

namespace game {

typedef stalker2::game_world game_world;

class tool_base {
public:
	tool_base(game_world& w) : m_game_world(w) {}
	virtual ~tool_base() {}
	virtual object_base* load_object(xray::configs::lua_config_value const& t) = 0;
	virtual object_base* create_object(pcstr name) = 0;
	virtual object_base* create_raw_object(pcstr id) = 0;
	virtual void destroy_object(object_base* o) = 0;
	virtual pcstr type_name() const = 0;

protected:
	game_world& m_game_world;
}; // class tool_base

} // namespace game
} // namespace xray

#endif // #ifndef TOOL_BASE_H_INCLUDED
