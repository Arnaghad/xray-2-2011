////////////////////////////////////////////////////////////////////////////
//	Created		: 30.06.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#ifndef SOUND_OBJECT_INSTANCE_H_INCLUDED
#define SOUND_OBJECT_INSTANCE_H_INCLUDED

#include <xray/sound/sound_instance_proxy.h>
#include <xray/math_extensions.h>

namespace xray {
namespace render {
namespace debug {
	class renderer;
} // namespace debug
} // namespace render
namespace sound_editor {
	
	class sound_object_wrapper;
	class sound_scene_document_native;
	struct sound_object_collision;

	class sound_object_instance
	{
	public:
						sound_object_instance	(sound_object_wrapper* w);
						~sound_object_instance	();
		void			save					(xray::configs::lua_config_value cfg);
		void			load					(xray::configs::lua_config_value const& cfg);
		void			set_transform			(math::float4x4 const& transform);
		math::float4x4*	get_transform			() {return m_transform;};
		void			set_parent				(sound_scene_document_native* doc);
		sound_object_wrapper* get_sound_object	() {return m_sound_object;};
		void			render					(xray::render::debug::renderer& r, xray::render::scene_ptr const& s);

		bool			is_selected				();
		bool			is_playing				();
		bool			is_paused				();
		void			play					();
		void			stop					();
		void			pause					();
		void			stop_now				();

		void			set_position_revertible	(math::float3 new_val);
		void			set_rotation_revertible	(math::float3 new_val);

		math::float3	position				() const { return m_transform->c.xyz(); }
		math::float3	rotation				() const { return rad2deg(m_transform->get_angles_xyz()); }
		
		bool			play_looped;

	private:
		math::float3	rad2deg					(math::float3 const& rad) const {return rad*(180.0f/math::pi);};
		math::float3	deg2rad					(math::float3 const& deg) const {return deg*(math::pi/180.0f);};

	private:
		sound_object_wrapper*				m_sound_object;
		sound_object_collision*				m_collision;
		sound::sound_instance_proxy_ptr*	m_proxy;
		sound_scene_document_native*		m_parent;
		math::float4x4*						m_transform;
	}; // class sound_object_instance
}// namespace sound_editor
}// namespace xray
#endif // #ifndef SOUND_OBJECT_INSTANCE_H_INCLUDED
