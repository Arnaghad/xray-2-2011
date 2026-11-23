////////////////////////////////////////////////////////////////////////////
//	Created		: 08.07.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "composite_sound_wrapper.h"
#include "composite_sound_item.h"
#include "sound_editor_manager.h"

using xray::sound_editor::composite_sound_wrapper;
using xray::sound_editor::sound_collection_item;

composite_sound_wrapper::composite_sound_wrapper(const char* name)
:sound_object_wrapper(name)
{
	after_playing_pause = 0;
	before_playing_pause = 0;
}

composite_sound_wrapper::~composite_sound_wrapper()
{
	for (composite_sound_item* itm : m_items)
		DELETE(itm);
}

void composite_sound_wrapper::save(xray::configs::lua_config_value& root)
{
	root["after_playing_pause"] = after_playing_pause;
	root["before_playing_pause"] = before_playing_pause;
	for(u32 i=0; i<m_items.size(); ++i)
	{
		configs::lua_config_value val = root["sound_items"][i];
		m_items[i]->save(val);
	}
}

void composite_sound_wrapper::save()
{
	std::string file_path = std::string(sound_editor_manager::sound_resources_path) + "composite/" + m_name + ".composite_sound";
	configs::lua_config_ptr const& config = configs::create_lua_config(file_path.c_str());
	configs::lua_config_value root = config->get_root()["composite_sound"];
	save(root);
	config->save(configs::target_sources);
}

void composite_sound_wrapper::load(xray::configs::lua_config_value const& cfg)
{
	if(cfg.value_exists("composite_sound"))
	{
		configs::lua_config_value root = cfg["composite_sound"];
		after_playing_pause = (u32)root["after_playing_pause"];
		before_playing_pause = (u32)root["before_playing_pause"];
		if(root.value_exists("sound_items"))
		{
			configs::lua_config_iterator it = root["sound_items"].begin();
			for(;it!=root["sound_items"].end(); ++it)
			{
				composite_sound_item* itm = NEW(composite_sound_item)();
				itm->load(*it);
				m_items.push_back(itm);
			}
		}
	}
}

void composite_sound_wrapper::load(callback_type options_callback, callback_type sound_callback)
{
	m_sound_loaded_callback = sound_callback;
	m_options_loaded_callback = options_callback;
	std::string path = std::string(sound_editor_manager::sound_resources_path) + "composite/" + m_name + ".composite_sound";
	resources::request request[] = { 
		{ path.c_str(), resources::lua_config_class }
	};
	
	resources::query_resources(
		request, 
		1, 
		std::bind(&composite_sound_wrapper::on_options_loaded, this, std::placeholders::_1), 
		g_allocator
	);
}

void composite_sound_wrapper::on_options_loaded(xray::resources::queries_result& data)
{
	if(data[0].is_successful())
	{
		configs::lua_config_ptr cfg = xray::static_cast_resource_ptr<configs::lua_config_ptr>(data[0].get_unmanaged_resource());
		load(cfg->get_root());
	}

	if(m_options_loaded_callback)
		m_options_loaded_callback(this);

	math::float4x4 matrix = math::float4x4().identity();
	math::float4x4 const* matrix_pointers[] = {&matrix};
	resources::request request = { m_name.c_str(), resources::composite_sound_class };

	resources::query_resource_params params(&request, nullptr, 1, 
		std::bind(&composite_sound_wrapper::on_sound_loaded, this, std::placeholders::_1),
		g_allocator, nullptr, matrix_pointers
	);

	resources::query_resources(params);
}

void composite_sound_wrapper::on_sound_loaded(xray::resources::queries_result& data)
{
	ASSERT(data.is_successful());
	(*m_sound) = xray::static_cast_resource_ptr<xray::sound::sound_emitter_ptr>(data[0].get_unmanaged_resource());
	m_is_loaded = true;

	if(m_sound_loaded_callback)
		m_sound_loaded_callback(this);
}

void composite_sound_wrapper::apply_changes(bool load_resources)
{
	save();
	if (load_resources)
		load(nullptr, nullptr);
}

void composite_sound_wrapper::revert_changes()
{
	load(nullptr, nullptr);
}

void composite_sound_wrapper::add_item(sound_collection_item* itm)
{
	composite_sound_item* item = dynamic_cast<composite_sound_item*>(itm);
	R_ASSERT(item!=nullptr);
	
	if(std::find(m_items.begin(), m_items.end(), item) == m_items.end())
	{
		m_items.push_back(item);
		item->parent = this;
	}
}

void composite_sound_wrapper::remove_item(sound_collection_item* itm)
{
	composite_sound_item* item = dynamic_cast<composite_sound_item*>(itm);
	R_ASSERT(item!=nullptr);
	
	auto it = std::find(m_items.begin(), m_items.end(), item);
	if(it != m_items.end())
	{
		m_items.erase(it);
		item->parent = nullptr;
	}
}

composite_sound_wrapper::items_list composite_sound_wrapper::items()
{
	items_list lst;
	for (composite_sound_item* item : m_items)
		lst.push_back(item);

	return lst;
}
