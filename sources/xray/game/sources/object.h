////////////////////////////////////////////////////////////////////////////
//	Created 	: 24.12.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef OBJECT_H_INCLUDED
#define OBJECT_H_INCLUDED

#include "object_base.h"
#include <xray/fixed_string.h>

namespace stalker2 {
class game_world;

class game_object_ :	public resources::unmanaged_resource,
						public xray::game::object_base,
						private xray::noncopyable 
{
public:
					game_object_			( game_world& w );
	virtual void	load					( configs::binary_config_value const& t );

	virtual void	on_scene_start			(  ){};

	virtual void	load_contents			( ){};
	virtual void	unload_contents			( ){};

	s16				m_loaded_cnt;

	game_world&		get_game_world			( ) {return m_game_world;}

	virtual void	load					( xray::configs::lua_config_value const& t ) { XRAY_UNREFERENCED_PARAMETER(t); }
	virtual void	set_name				( pcstr name ) { m_name = name; }
	virtual void	set_lib_name			( pcstr name ) { m_lib_name = name; }

protected:
	game_world&		m_game_world;
	xray::fixed_string<128>	m_name;
	xray::fixed_string<128>	m_lib_name;
};

class game_object_static :	public game_object_
{
	typedef game_object_ super;
public:
					game_object_static		( game_world& w );
	virtual void	load					( configs::binary_config_value const& t );

protected:
	float4x4		m_transform;
};

typedef	intrusive_ptr<
	game_object_,
	resources::unmanaged_intrusive_base,
	threading::single_threading_policy
> game_object_ptr_;

typedef	intrusive_ptr<
	game_object_static,
	resources::unmanaged_intrusive_base,
	threading::single_threading_policy
> game_object_static_ptr;

typedef fastdelegate::FastDelegate< void ( game_object_ptr_ const& ) >		object_loaded_callback_type;

} // namespace stalker2

#endif // #ifndef OBJECT_H_INCLUDED
