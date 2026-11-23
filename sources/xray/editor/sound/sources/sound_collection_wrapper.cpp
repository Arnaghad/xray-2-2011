////////////////////////////////////////////////////////////////////////////
//	Created		: 08.07.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "sound_collection_wrapper.h"
#include "sound_collection_item.h"
#include "sound_editor_manager.h"

using xray::sound_editor::sound_collection_wrapper;
using xray::sound_editor::collection_play_type;
using xray::sound_editor::sound_collection_item;

sound_collection_wrapper::sound_collection_wrapper(const char* name)
:sound_object_wrapper(name)
{
	play_type = collection_play_type::random;
	can_repeat_last = false;
	cycle_from = 0;
	min_delay = 0.0f;
	max_delay = 0.0f;
}

sound_collection_wrapper::~sound_collection_wrapper()
{
	for (sound_collection_item* itm : m_items)
		DELETE(itm);
}

void sound_collection_wrapper::save(xray::configs::lua_config_value& root)
{
	root["type"] = (play_type == collection_play_type::random) ? "random" : "sequential";
	root["dont_repeat_sound_successively"] = can_repeat_last;
	root["cyclic_repeat_from_sound"] = cycle_from;
	root["min_delay"] = min_delay;
	root["max_delay"] = max_delay;
	
	for(u32 i=0; i<m_items.size(); ++i)
	{
		configs::lua_config_value val = root["sound_items"][i];
		m_items[i]->save(val);
	}
}

void sound_collection_wrapper::save()
{
	std::string file_path = std::string(sound_editor_manager::sound_resources_path) + "collections/" + m_name + ".sound_collection";
	configs::lua_config_ptr const& config = configs::create_lua_config(file_path.c_str());
	configs::lua_config_value root = config->get_root()["collection"];
	save(root);
	config->save(configs::target_sources);
}

void sound_collection_wrapper::load(xray::configs::lua_config_value const& cfg)
{
	if(cfg.value_exists("collection"))
	{
		configs::lua_config_value root = cfg["collection"];
		std::string t = (pcstr)root["type"];
		play_type = (t == "random") ? collection_play_type::random : collection_play_type::sequential;
		
		can_repeat_last = (bool)root["dont_repeat_sound_successively"];
		cycle_from = (u32)root["cyclic_repeat_from_sound"];
		
		if(root.value_exists("min_delay"))
			min_delay = (float)root["min_delay"];

		if(root.value_exists("max_delay"))
			max_delay = (float)root["max_delay"];

		if(root.value_exists("sound_items"))
		{
			configs::lua_config_iterator it = root["sound_items"].begin();
			for(;it!=root["sound_items"].end(); ++it)
			{
				sound_collection_item* itm = NEW(sound_collection_item)();
				itm->load(*it);
				m_items.push_back(itm);
			}
		}
	}
}

void sound_collection_wrapper::load(callback_type options_callback, callback_type sound_callback)
{
	m_sound_loaded_callback = sound_callback;
	m_options_loaded_callback = options_callback;
	std::string path = std::string(sound_editor_manager::sound_resources_path) + "collections/" + m_name + ".sound_collection";
	resources::request request[] = { 
		{ path.c_str(), resources::lua_config_class }
	};
	
	resources::query_resources(
		request, 
		1, 
		std::bind(&sound_collection_wrapper::on_options_loaded, this, std::placeholders::_1), 
		g_allocator
	);
}

void sound_collection_wrapper::on_options_loaded(xray::resources::queries_result& data)
{
	if(data[0].is_successful())
	{
		configs::lua_config_ptr cfg = xray::static_cast_resource_ptr<configs::lua_config_ptr>(data[0].get_unmanaged_resource());
		load(cfg->get_root());
	}

	if(m_options_loaded_callback)
		m_options_loaded_callback(this);

	bool all_items_exist = true;
	for ( sound_collection_item* item : m_items )
	{
		if ( !item->get_sound_object()->is_exist() )
		{
			all_items_exist		= false;
			break;
		}
	}

	if ( !all_items_exist )
	{
		m_is_loaded = true;
		if(m_sound_loaded_callback)
			m_sound_loaded_callback(this);
		return;
	}

	math::float4x4 matrix = math::float4x4().identity();
	math::float4x4 const* matrix_pointers[] = {&matrix};
	resources::request request = { m_name.c_str(), resources::sound_collection_class };

	resources::query_resource_params params(&request, nullptr, 1, 
		std::bind(&sound_collection_wrapper::on_sound_loaded, this, std::placeholders::_1),
		g_allocator, nullptr, matrix_pointers
	);

	resources::query_resources(params);
}

void sound_collection_wrapper::on_sound_loaded(xray::resources::queries_result& data)
{
	ASSERT(data.is_successful());
	(*m_sound) = xray::static_cast_resource_ptr<xray::sound::sound_emitter_ptr>(data[0].get_unmanaged_resource());
	m_is_loaded = true;

	if(m_sound_loaded_callback)
		m_sound_loaded_callback(this);
}

void sound_collection_wrapper::apply_changes(bool load_resources)
{
	save();
	if (load_resources)
		load(nullptr, nullptr);
}

void sound_collection_wrapper::revert_changes()
{
	load(nullptr, nullptr);
}

void sound_collection_wrapper::add_item(sound_collection_item* itm)
{
	if(std::find(m_items.begin(), m_items.end(), itm) == m_items.end())
	{
		m_items.push_back(itm);
		itm->parent = this;
	}
}

void sound_collection_wrapper::remove_item(sound_collection_item* itm)
{
	auto it = std::find(m_items.begin(), m_items.end(), itm);
	if(it != m_items.end())
	{
		m_items.erase(it);
		itm->parent = nullptr;
	}
}
