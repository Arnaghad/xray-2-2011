////////////////////////////////////////////////////////////////////////////
//	Created		: 08.07.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#ifndef SOUND_COLLECTION_WRAPPER_H_INCLUDED
#define SOUND_COLLECTION_WRAPPER_H_INCLUDED

#include "sound_object_wrapper.h"

namespace xray {
namespace sound_editor {

	enum class collection_play_type
	{
		random = 1,
		sequential = 2,
	};

	class sound_collection_wrapper: public sound_object_wrapper
	{
	public:
							sound_collection_wrapper	(const char* name);
		virtual				~sound_collection_wrapper	();
		
		virtual void		apply_changes				(bool load_resources) override;
		virtual void		revert_changes				() override;
		virtual void		save						() override;
		virtual void		save					(xray::configs::lua_config_value& cfg) override;
		virtual void		load					(callback_type options_callback, callback_type sound_callback) override;
		virtual void		load					(xray::configs::lua_config_value const& cfg) override;
		
		virtual items_list	items						() override {return m_items;};
		virtual void		add_item				(sound_collection_item* itm) override;
		virtual void		remove_item				(sound_collection_item* itm) override;

		collection_play_type play_type;
		bool			can_repeat_last;
		u32				cycle_from;
		float			min_delay;
		float			max_delay;

	private:
		void				on_sound_loaded				(xray::resources::queries_result& data);
		void				on_options_loaded			(xray::resources::queries_result& data);

	private:
		items_list		m_items;
	}; // class sound_collection_wrapper
} // namespace sound_editor 
} // namespace xray
#endif // #ifndef SOUND_COLLECTION_WRAPPER_H_INCLUDED
