////////////////////////////////////////////////////////////////////////////
//	Created		: 07.07.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#ifndef SOUND_OBJECT_WRAPPER_H_INCLUDED
#define SOUND_OBJECT_WRAPPER_H_INCLUDED

#include <xray/sound/sound_emitter.h>
#include <xray/configs.h>
#include <functional>
#include <vector>
#include <string>

namespace xray {
namespace sound_editor {

	class sound_object_instance;
	class sound_collection_item;

	class sound_object_wrapper
	{
	public:
		typedef std::vector<sound_collection_item*> items_list;
		typedef std::function<void(sound_object_wrapper*)> callback_type;

									sound_object_wrapper	(const char* name);
		virtual						~sound_object_wrapper	();

		sound_object_instance*		create_instance			();
		sound::sound_instance_proxy_ptr emit				(sound::sound_scene_ptr& scene, sound::world_user& user) {return (*m_sound)->emit(scene, user);};
		
		virtual void				apply_changes			(bool load_resources) = 0;
		virtual void				revert_changes			() = 0;
		virtual void				save					() = 0;
		virtual void				save					(xray::configs::lua_config_value& cfg) = 0;
		virtual void				load					(callback_type options_callback, callback_type sound_callback) = 0;
		virtual void				load					(xray::configs::lua_config_value const& cfg) = 0;
		virtual items_list			items					() = 0;
		virtual void				add_item				(sound_collection_item* itm) = 0;
		virtual void				remove_item				(sound_collection_item* itm) = 0;

		const char*					name					() const { return m_name.c_str(); }
		bool						is_loaded				() const { return m_is_loaded; }
		bool						is_exist				() const { return m_is_exist; }

	protected:
		sound::sound_emitter_ptr*	m_sound;
		callback_type				m_sound_loaded_callback;
		callback_type				m_options_loaded_callback;
		std::string					m_name;
		bool						m_is_loaded;
		bool						m_is_exist;
	}; // class sound_object_wrapper
} // namespace sound_editor 
} // namespace xray
#endif // #ifndef SOUND_OBJECT_WRAPPER_H_INCLUDED
