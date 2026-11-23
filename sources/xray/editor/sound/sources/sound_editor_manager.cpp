////////////////////////////////////////////////////////////////////////////
//	Created		: 08.12.2009
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "sound_editor_manager.h"
#include "sound_object_wrapper.h"
#include "single_sound_wrapper.h"
#include "composite_sound_wrapper.h"
#include "sound_collection_wrapper.h"
#include <xray/sound/world.h>
#include <xray/render/world.h>

using xray::sound_editor::sound_editor_manager;
using xray::sound_editor::sound_object_wrapper;
using xray::sound_editor::single_sound_wrapper;
using xray::sound_editor::composite_sound_wrapper;
using xray::sound_editor::sound_collection_wrapper;
using xray::sound_editor::sound_object_type;

const char* sound_editor_manager::sound_resources_path = "resources/sounds/";
const char* sound_editor_manager::absolute_sound_resources_path = "";

sound_editor_manager::sound_editor_manager(xray::sound::world& sound_world, xray::render::world& render_world)
:m_sound_world(sound_world),
m_render_world(render_world)
{
}

sound_editor_manager::~sound_editor_manager()
{
	clear_resources();
}

void sound_editor_manager::clear_resources()
{
	for (auto& p : m_single_sounds)
		DELETE(p.second);
	m_single_sounds.clear();

	for (auto& p : m_composite_sounds)
		DELETE(p.second);
	m_composite_sounds.clear();

	for (auto& p : m_sound_collections)
		DELETE(p.second);
	m_sound_collections.clear();

	m_modified_sounds.clear();
}

sound_object_wrapper* sound_editor_manager::get_sound(const char* sound_name, sound_object_type t)
{
	if(t==sound_object_type::single)
		return get_single_sound(sound_name);
	else if(t==sound_object_type::composite)
		return get_composite_sound(sound_name);
	else if(t==sound_object_type::collection)
		return get_sound_collection(sound_name);
	
	return nullptr;
}

single_sound_wrapper* sound_editor_manager::get_single_sound(const char* sound_name)
{
	auto it = m_single_sounds.find(sound_name);
	if(it != m_single_sounds.end())
		return it->second;

	single_sound_wrapper* w = NEW(single_sound_wrapper)(sound_name);
	w->load(nullptr, nullptr); // Callback logic needs to be handled
	m_single_sounds[sound_name] = w;
	return w;
}

composite_sound_wrapper* sound_editor_manager::get_composite_sound(const char* sound_name)
{
	auto it = m_composite_sounds.find(sound_name);
	if(it != m_composite_sounds.end())
		return it->second;

	composite_sound_wrapper* w = NEW(composite_sound_wrapper)(sound_name);
	w->load(nullptr, nullptr);
	m_composite_sounds[sound_name] = w;
	return w;
}

sound_collection_wrapper* sound_editor_manager::get_sound_collection(const char* sound_name)
{
	auto it = m_sound_collections.find(sound_name);
	if(it != m_sound_collections.end())
		return it->second;

	sound_collection_wrapper* w = NEW(sound_collection_wrapper)(sound_name);
	w->load(nullptr, nullptr);
	m_sound_collections[sound_name] = w;
	return w;
}

void sound_editor_manager::remove_sound(const char* sound_name, sound_object_type t)
{
	if(t==sound_object_type::single)
	{
		auto it = m_single_sounds.find(sound_name);
		if (it != m_single_sounds.end()) {
			DELETE(it->second);
			m_single_sounds.erase(it);
		}
	}
	else if(t==sound_object_type::composite)
	{
		auto it = m_composite_sounds.find(sound_name);
		if (it != m_composite_sounds.end()) {
			DELETE(it->second);
			m_composite_sounds.erase(it);
		}
	}
	else if(t==sound_object_type::collection)
	{
		auto it = m_sound_collections.find(sound_name);
		if (it != m_sound_collections.end()) {
			DELETE(it->second);
			m_sound_collections.erase(it);
		}
	}
}
