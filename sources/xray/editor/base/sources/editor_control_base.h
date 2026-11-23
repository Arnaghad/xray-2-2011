////////////////////////////////////////////////////////////////////////////
//	Created		: 17.02.2009
//	Author		: Armen Abroyan
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef EDITOR_CONTROL_H_INCLUDED
#define EDITOR_CONTROL_H_INCLUDED

#include <xray/render/engine/base_classes.h>
#include <functional>
#include <string>

namespace xray {
	namespace render {
		class render_output_window;
	}
}

namespace xray {

namespace collision{ class object; class geometry; }

namespace editor_base {

class collision_object_geometry;
class editor_control_base;

// Native callback types replacing CLI delegates
typedef std::function<void(editor_control_base*)>	activate_control_event;
typedef std::function<void(editor_control_base*)>	deactivate_control_event;
typedef std::function<void(editor_control_base*)>	property_changed_event;
typedef std::function<editor_control_base*()>		active_control_getter;

class editor_control_base
{
public:
						editor_control_base	( ) : sensitivity(1.0f) {}
	virtual				~editor_control_base( ) = default;

		const char*		id					( ) { return m_control_id.c_str(); }

	virtual	void		activate			( bool b_activate ) { XRAY_UNREFERENCED_PARAMETER(b_activate); }
	virtual	void		update				( ) = 0;

	virtual	void		start_input			( int mode, int button, collision::object const* coll, math::float3 const& pos ) { XRAY_UNREFERENCED_PARAMETER(mode); XRAY_UNREFERENCED_PARAMETER(button); XRAY_UNREFERENCED_PARAMETER(coll); XRAY_UNREFERENCED_PARAMETER(pos); }
	virtual	void		execute_input		( ) = 0;	
	virtual	void		end_input			( ) {}

	virtual	void		draw				( render::scene_view_pointer scene_view, render::render_output_window_pointer output_window ) = 0;

	virtual	u32			acceptable_collision_type( ) = 0;
	
	virtual	void		load_settings		( ) {}
	virtual	void		save_settings		( ) {}
				
			void		subscribe_on_activated			( activate_control_event d ) { m_on_activated = d; }
			void		subscribe_on_deactivated		( deactivate_control_event d ) { m_on_deactivated = d; }
			void		subscribe_on_property_changed	( property_changed_event d ) { m_on_property_changed = d; }

	virtual	void		change_property					( const char* prop_name, float const dx, float const dy ) { XRAY_UNREFERENCED_PARAMETER(prop_name); XRAY_UNREFERENCED_PARAMETER(dx); XRAY_UNREFERENCED_PARAMETER(dy); }
	virtual	void		pin								( bool ){};
	virtual	bool		has_properties					( ){return false;}

	// Property: sensitivity
	float				sensitivity;

	// Property: name (read-only, returns id())
	const char*			name() { return id(); }

protected:
	virtual	void		initialize			( ) = 0;	
	virtual	void		destroy				( ) = 0;	
	virtual	void		set_mouse_sensivity	( float const ) {}

	activate_control_event		m_on_activated;
	deactivate_control_event	m_on_deactivated;
	property_changed_event		m_on_property_changed;

	std::string			m_control_id;
}; // class editor_control_base

} //namespace editor_base
} //namespace xray

#endif // #ifndef EDITOR_CONTROL_H_INCLUDED
