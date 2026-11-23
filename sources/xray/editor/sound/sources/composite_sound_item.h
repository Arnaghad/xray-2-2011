////////////////////////////////////////////////////////////////////////////
//	Created		: 05.08.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#ifndef COMPOSITE_SOUND_ITEM_H_INCLUDED
#define COMPOSITE_SOUND_ITEM_H_INCLUDED

#include "sound_collection_item.h"

namespace xray {
namespace sound_editor {
	
	class composite_sound_item: public sound_collection_item
	{
	public:
						composite_sound_item		();
		virtual			~composite_sound_item		();
		virtual void	save						(xray::configs::lua_config_value& cfg) override;
		virtual void	load						(xray::configs::lua_config_value const& cfg) override;

		u32				playing_offset_min			() const { return m_playing_offset_min; }
		void			set_playing_offset_min		(u32 val) 
		{ 
			m_playing_offset_min = val; 
			if (m_playing_offset_min > m_playing_offset_max) m_playing_offset_max = val; 
		}

		u32				playing_offset_max			() const { return m_playing_offset_max; }
		void			set_playing_offset_max		(u32 val) 
		{ 
			m_playing_offset_max = val; 
			if (m_playing_offset_max < m_playing_offset_min) m_playing_offset_min = val; 
		}

		bool			is_random; 
	private:
		u32				m_playing_offset_min;
		u32				m_playing_offset_max;
	}; // class composite_sound_item
} // namespace sound_editor 
} // namespace xray
#endif // #ifndef COMPOSITE_SOUND_ITEM_H_INCLUDED
