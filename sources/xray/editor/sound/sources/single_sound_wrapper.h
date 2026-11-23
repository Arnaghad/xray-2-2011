////////////////////////////////////////////////////////////////////////////
//	Created		: 25.03.2011
//	Author		: Dmitry Kulikov
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#ifndef SINGLE_SOUND_WRAPPER_H_INCLUDED
#define SINGLE_SOUND_WRAPPER_H_INCLUDED

#include "sound_object_wrapper.h"
#include <xray/sound/single_sound.h>

namespace xray {
namespace sound_editor {

	class wav_file_options;

	class single_sound_wrapper: public sound_object_wrapper
	{
	public:
							single_sound_wrapper	(const char* name);
		virtual				~single_sound_wrapper	();
		
		virtual void		apply_changes			(bool load_resources) override;
		virtual void		revert_changes			() override;
		virtual void		save					() override;
		virtual void		save					(xray::configs::lua_config_value& cfg) override;
		virtual void		load					(callback_type options_callback, callback_type sound_callback) override;
		virtual void		load					(xray::configs::lua_config_value const& cfg) override;
		
		virtual items_list	items					() override {return items_list();};
		virtual void		add_item				(sound_collection_item* itm) override {XRAY_UNREFERENCED_PARAMETER(itm);};
		virtual void		remove_item				(sound_collection_item* itm) override {XRAY_UNREFERENCED_PARAMETER(itm);};
		
		sound::sound_rms_ptr get_sound_rms			();
		sound::sound_spl_ptr get_sound_spl			();

		wav_file_options*	get_wav_options			() { return m_wav_options; }

	private:
		void				on_single_sound_loaded	(xray::resources::queries_result& data);
		void				on_wav_options_loaded	(xray::resources::queries_result& data);
		void				read_spl_curve			(xray::configs::lua_config_value const& cfg);

	private:
		// float_curve is WPF, skipping for native logic
		wav_file_options*				m_wav_options;
	}; // class single_sound_wrapper
} // namespace sound_editor 
} // namespace xray
#endif // #ifndef SINGLE_SOUND_WRAPPER_H_INCLUDED
