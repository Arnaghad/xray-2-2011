////////////////////////////////////////////////////////////////////////////
//	Created		: 08.12.2009
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////
#ifndef SOUND_EDITOR_MANAGER_H_INCLUDED
#define SOUND_EDITOR_MANAGER_H_INCLUDED

#include <string>
#include <vector>
#include <map>

namespace xray {
namespace sound { struct world; }
namespace render { class world; }
namespace sound_editor {

	class sound_object_wrapper;
	class single_sound_wrapper;
	class composite_sound_wrapper;
	class sound_collection_wrapper;

	enum class sound_object_type
	{
		single = 256,
		composite = 257,
		collection = 258
	};

	class sound_editor_manager
	{
	public:
		typedef std::map<std::string, single_sound_wrapper*> single_sounds_list;
		typedef std::map<std::string, composite_sound_wrapper*> composite_sounds_list;
		typedef std::map<std::string, sound_collection_wrapper*> sound_collections_list;
		typedef std::vector<std::string> modified_list;

		static const char*		sound_resources_path;
		static const char*		absolute_sound_resources_path;

	public:
								sound_editor_manager		(sound::world& sound_world, render::world& render_world);
								~sound_editor_manager		();

				void			clear_resources				();
				
		sound_object_wrapper*	get_sound					(const char* sound_name, sound_object_type t);
		single_sound_wrapper*	get_single_sound			(const char* sound_name);
		composite_sound_wrapper* get_composite_sound		(const char* sound_name);
		sound_collection_wrapper* get_sound_collection		(const char* sound_name);
		
				void			remove_sound				(const char* sound_name, sound_object_type t);

		sound::world&			get_sound_world				() { return m_sound_world; }
		render::world&			get_render_world			() { return m_render_world; }

	private:
		sound::world&				m_sound_world;
		render::world&				m_render_world;

		single_sounds_list			m_single_sounds;
		composite_sounds_list		m_composite_sounds;
		sound_collections_list		m_sound_collections;
		modified_list				m_modified_sounds;
	}; // class sound_editor_manager

} // namespace sound_editor
} // namespace xray

#endif // #ifndef SOUND_EDITOR_MANAGER_H_INCLUDED
