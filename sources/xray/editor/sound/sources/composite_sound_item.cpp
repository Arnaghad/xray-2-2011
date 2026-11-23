////////////////////////////////////////////////////////////////////////////
//	Created		: 05.08.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "composite_sound_item.h"

using xray::sound_editor::composite_sound_item;

composite_sound_item::composite_sound_item()
{
	m_playing_offset_min	= 0;
	m_playing_offset_max	= 0;
	is_random				= false;
}

composite_sound_item::~composite_sound_item()
{
}

void composite_sound_item::save(xray::configs::lua_config_value& cfg)
{
	sound_collection_item::save(cfg);

	if ( m_playing_offset_min != m_playing_offset_max )
	{
		cfg["offset_type"]			= "random";
		cfg["playing_offset_min"]	= m_playing_offset_min;
		cfg["playing_offset_max"]	= m_playing_offset_max;
	}
	else
	{
		cfg["offset_type"]			= "constant";
		cfg["playing_offset"]		= m_playing_offset_min;
	}
}

void composite_sound_item::load(xray::configs::lua_config_value const& cfg)
{
	sound_collection_item::load(cfg);

	if ( cfg.value_exists( "offset_type" ) )
	{
		std::string of = (pcstr)cfg["offset_type"];
		if ( of	== "random" )
		{
			set_playing_offset_min(cfg["playing_offset_min"]);
			set_playing_offset_max(cfg["playing_offset_max"]);
		}
		else
		{
			set_playing_offset_min(cfg["playing_offset"]);
			set_playing_offset_max(cfg["playing_offset"]);
		}
	}
	else
	{
		set_playing_offset_min(0);
		set_playing_offset_max(0);
	}
}
