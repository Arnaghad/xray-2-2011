////////////////////////////////////////////////////////////////////////////
//	Created		: 08.07.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#ifndef SOUND_COLLECTION_ITEM_H_INCLUDED
#define SOUND_COLLECTION_ITEM_H_INCLUDED

#include <xray/configs.h>
#include <string>

#include "sound_editor_manager.h"

namespace xray {
namespace sound_editor {

	class sound_object_wrapper;

	class sound_collection_item
	{
	public:
						sound_collection_item		();
		virtual			~sound_collection_item		();
		virtual void	save						(xray::configs::lua_config_value& cfg);
		virtual void	load						(xray::configs::lua_config_value const& cfg);
		virtual std::string to_string				() const;

		bool			is_link;
		sound_object_type sound_type;
		
		sound_object_wrapper* get_sound_object		() const { return m_sound_object; }
		void			set_sound_object			(sound_object_wrapper* val) { m_sound_object = val; }

		sound_object_wrapper* parent;

	private:
		void			on_sound_loaded				(sound_object_wrapper* obj);

	private:
		sound_object_wrapper* m_sound_object;
	}; // class sound_collection_item
} // namespace sound_editor 
} // namespace xray
#endif // #ifndef SOUND_COLLECTION_ITEM_H_INCLUDED
