////////////////////////////////////////////////////////////////////////////
//	Created		: 10.05.2026
//	Author		: Antigravity
//	Copyright (C) GSC Game World - 2026
////////////////////////////////////////////////////////////////////////////

#ifndef OBJECT_BASE_H_INCLUDED
#define OBJECT_BASE_H_INCLUDED

namespace xray {
namespace configs {
	class lua_config_value;
} // namespace configs

namespace game {

class object_base {
public:
	virtual ~object_base() {}
	virtual void load(xray::configs::lua_config_value const& t) = 0;
	virtual void set_name(pcstr name) = 0;
	virtual void set_lib_name(pcstr name) = 0;
}; // class object_base

} // namespace game
} // namespace xray

#endif // #ifndef OBJECT_BASE_H_INCLUDED
