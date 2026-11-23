////////////////////////////////////////////////////////////////////////////
//	Created		: 08.07.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#ifndef COMPOSITE_SOUND_WRAPPER_H_INCLUDED
#define COMPOSITE_SOUND_WRAPPER_H_INCLUDED

#include "sound_object_wrapper.h"

namespace xray {
namespace sound_editor {

	class composite_sound_item;

	class composite_sound_wrapper: public sound_object_wrapper
	{
	public:
							composite_sound_wrapper		(const char* name);
		virtual				~composite_sound_wrapper	();
		
		virtual void		apply_changes				(bool load_resources) override;
		virtual void		revert_changes				() override;
		virtual void		save						() override;
		virtual void		save					(xray::configs::lua_config_value& cfg) override;
		virtual void		load					(callback_type options_callback, callback_type sound_callback) override;
		virtual void		load					(xray::configs::lua_config_value const& cfg) override;
		
		virtual items_list	items						() override;
		virtual void		add_item				(sound_collection_item* itm) override;
		virtual void		remove_item				(sound_collection_item* itm) override;

		u32 after_playing_pause;
		u32 before_playing_pause;

	private:
		void				on_sound_loaded				(xray::resources::queries_result& data);
		void				on_options_loaded			(xray::resources::queries_result& data);

	private:
		std::vector<composite_sound_item*>	m_items;
	}; // class composite_sound_wrapper
} // namespace sound_editor 
} // namespace xray
#endif // #ifndef COMPOSITE_SOUND_WRAPPER_H_INCLUDED
