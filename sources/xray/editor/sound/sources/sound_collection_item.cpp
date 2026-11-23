////////////////////////////////////////////////////////////////////////////
//	Created		: 08.07.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "sound_collection_item.h"
#include "sound_object_wrapper.h"
#include "single_sound_wrapper.h"
#include "composite_sound_wrapper.h"
#include "sound_collection_wrapper.h"

using xray::sound_editor::sound_collection_item;
using xray::sound_editor::sound_object_type;
using xray::sound_editor::sound_object_wrapper;

sound_collection_item::sound_collection_item()
{
	sound_type = sound_object_type::single;
	m_sound_object = nullptr;
	parent = nullptr;
	is_link = true;
}

sound_collection_item::~sound_collection_item()
{
	if (m_sound_object)
		DELETE(m_sound_object);
}

void sound_collection_item::save(xray::configs::lua_config_value& cfg)
{
	R_ASSERT(m_sound_object!=nullptr);
	cfg["filename"] = m_sound_object->name();
	
	if(sound_type==sound_object_type::single)
		cfg["resource_type"] = (u32)resources::single_sound_class;
	else
	{
		if(!is_link)
		{
			xray::configs::lua_config_value val = cfg["collection"];
			m_sound_object->save(val);
		}

		if(sound_type==sound_object_type::composite)
			cfg["resource_type"] = (u32)resources::composite_sound_class;
		else
			cfg["resource_type"] = (u32)resources::sound_collection_class;
	}

	if(is_link)
		m_sound_object->save();
}

void sound_collection_item::load(xray::configs::lua_config_value const& cfg)
{
	const char* n = (pcstr)cfg["filename"];
	u32 class_id = (u32)cfg["resource_type"];
	
	if(class_id == (u32)resources::single_sound_class)
	{
		m_sound_object = NEW(single_sound_wrapper)(n);
		sound_type = sound_object_type::single;
	}
	else if(class_id == (u32)resources::composite_sound_class)
	{
		m_sound_object = NEW(composite_sound_wrapper)(n);
		sound_type = sound_object_type::composite;
	}
	else
	{
		m_sound_object = NEW(sound_collection_wrapper)(n);
		sound_type = sound_object_type::collection;
	}

	if(cfg.value_exists("collection"))
	{
		m_sound_object->load(cfg);
		is_link = false;
	}
	else
	{
		auto cb = std::bind(&sound_collection_item::on_sound_loaded, this, std::placeholders::_1);
		m_sound_object->load(cb, cb);
	}
}

void sound_collection_item::on_sound_loaded(sound_object_wrapper* obj)
{
	XRAY_UNREFERENCED_PARAMETER(obj);
}

std::string sound_collection_item::to_string() const
{
	if(m_sound_object!=nullptr)
		return m_sound_object->name();

	return "";
}
