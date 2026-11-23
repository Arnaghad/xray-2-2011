////////////////////////////////////////////////////////////////////////////
//	Created		: 30.06.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "sound_object_instance.h"
#include "sound_object_wrapper.h"
#include "single_sound_wrapper.h"
#include "composite_sound_wrapper.h"
#include "sound_scene_document_native.h"
#include "sound_object_collision.h"
#include "wav_file_options.h"
#include <xray/render/facade/debug_renderer.h>
#include <xray/editor/base/collision_object_types.h>
#include <xray/sound/sound_instance_proxy.h>
#include <xray/collision/api.h>

using xray::sound_editor::sound_object_instance;
using xray::sound_editor::sound_object_wrapper;
using xray::sound_editor::sound_scene_document_native;

sound_object_instance::sound_object_instance(sound_object_wrapper* w)
:m_sound_object(w)
{
	play_looped = false;
	m_collision = nullptr;
	m_transform = NEW(math::float4x4)(math::float4x4().identity());
	m_proxy = NEW(sound::sound_instance_proxy_ptr)();
	m_parent = nullptr;
}

sound_object_instance::~sound_object_instance()
{
	if(m_collision && m_collision->initialized())
		m_collision->destroy(g_allocator);

	(*m_proxy) = nullptr;
	DELETE(m_proxy);
	DELETE(m_transform);
	if (m_collision)
		DELETE(m_collision);
}

void sound_object_instance::save(xray::configs::lua_config_value cfg)
{
	cfg["filename"] = m_sound_object->name();
	// cfg["id"] = ...; // Handle ID conversion if needed
	cfg["position"] = (*m_transform).c.xyz();
	cfg["rotation"] = (*m_transform).get_angles_xyz();
	cfg["looped"] = play_looped;
	
	if(dynamic_cast<single_sound_wrapper*>(m_sound_object))
		cfg["resource_type"] = (u32)256; // single
	else if(dynamic_cast<composite_sound_wrapper*>(m_sound_object))
		cfg["resource_type"] = (u32)257; // composite
	else
		cfg["resource_type"] = (u32)258; // collection
}

void sound_object_instance::load(xray::configs::lua_config_value const& cfg)
{
	play_looped = (bool)cfg["looped"];
	float3 pos = (float3)cfg["position"];
	float3 angles = (float3)cfg["rotation"];
	float4x4 m = create_translation(pos) * create_rotation(angles);
	set_transform(m);
}

void sound_object_instance::set_transform(float4x4 const& transform)
{
	float3 s = transform.get_scale();
	if(s.x<0.05f || s.y<0.05f || s.z<0.05f)
	{
		xray::math::float4x4 m = transform;
		xray::math::clamp(s.x, 0.05f, 1000.0f);
		xray::math::clamp(s.y, 0.05f, 1000.0f);
		xray::math::clamp(s.z, 0.05f, 1000.0f);
		m.set_scale(s);
		*m_transform = m;
	}
	else
		*m_transform = transform;
	
	if(m_collision && m_collision->initialized())
		m_collision->set_matrix(m_transform);

	if(m_sound_object->is_loaded() && m_proxy->c_ptr())
		(*m_proxy)->set_position((*m_transform).c.xyz());
}

void sound_object_instance::set_parent(sound_scene_document_native* doc)
{
	m_parent = doc;
	// Logic that depends on managed doc needs to be moved to bridge or doc made native
	// m_collision = NEW(sound_object_collision)(m_parent->viewport->m_collision_tree);
	// ...
}

bool sound_object_instance::is_selected()
{
	if (m_parent)
		return m_parent->is_selected(this);
	return false;
}

void sound_object_instance::render(xray::render::debug::renderer& r, xray::render::scene_ptr const& s)
{
	r.draw_origin(s, *m_transform, 2.0f);
	if(is_selected())
		r.draw_line_ellipsoid(s, *m_transform, math::color(255, 0, 0));
	else
		r.draw_line_ellipsoid(s, *m_transform, math::color(0, 255, 0));
}

bool sound_object_instance::is_playing()
{
	return m_proxy->c_ptr() && (*m_proxy)->is_playing();
}

bool sound_object_instance::is_paused()
{
	return m_proxy->c_ptr() && (*m_proxy)->is_producing_paused( );
}

void sound_object_instance::play()
{
	if (!m_parent) return;
	
	// (*m_proxy) = m_sound_object->emit(m_parent->get_sound_scene(), m_parent->get_editor()->get_sound_world()->get_editor_world_user());

	if((*m_proxy)->is_producing_paused())
		return (*m_proxy)->resume();

	if((*m_proxy)->is_playing())
		return;

	(*m_proxy)->play((play_looped) ? sound::looped : sound::once);
}

void sound_object_instance::stop()
{
	if(!m_proxy->c_ptr())
		return;

	(*m_proxy)->stop();
}

void sound_object_instance::pause()
{
	if(!m_proxy->c_ptr())
		return;

	if((*m_proxy)->is_playing())
		(*m_proxy)->pause();
}

void sound_object_instance::stop_now()
{
	if(!m_proxy->c_ptr())
		return;
	
	(*m_proxy)->stop();
}

void sound_object_instance::set_position_revertible(float3 new_val)
{
	float4x4 transform = *m_transform;
	transform.c.xyz() = new_val;
	if (m_parent)
		m_parent->change_object_transform(this, *m_transform, transform);
}

void sound_object_instance::set_rotation_revertible(float3 new_val)
{
	float4x4 transform = *m_transform;
	transform = create_scale(transform.get_scale()) * create_rotation(new_val) * create_translation(transform.c.xyz());
	if (m_parent)
		m_parent->change_object_transform(this, *m_transform, transform);
}
