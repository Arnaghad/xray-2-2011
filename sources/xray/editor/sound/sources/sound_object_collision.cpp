////////////////////////////////////////////////////////////////////////////
//	Created		: 01.07.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "sound_object_collision.h"
#include "sound_object_collision_dynamic.h"
#include <xray/collision/api.h>
#include <xray/collision/geometry_instance.h>
#include <xray/collision/space_partitioning_tree.h>

using xray::sound_editor::sound_object_collision;
using xray::sound_editor::sound_object_collision_dynamic;
using xray::sound_editor::sound_object_instance;
using xray::collision::space_partitioning_tree;
using xray::collision::geometry_instance;
using xray::collision::object;
using xray::collision::object_type;

sound_object_collision::sound_object_collision(space_partitioning_tree* tree)
:m_collision_obj(nullptr),
m_collision_tree(tree),
m_b_active(false),
m_user_geometry(nullptr)
{
}

object* sound_object_collision::get_collision_object()
{ 
	return m_collision_obj; 
}

void sound_object_collision::destroy(xray::memory::base_allocator* allocator)
{
	if(initialized())
	{
		remove();
		m_collision_obj->get_geometry_instance()->destroy(g_allocator);
		DELETE(m_collision_obj);
		m_collision_obj = nullptr;
		if(m_user_geometry)
			collision::delete_geometry_instance(allocator, m_user_geometry);
	}
}

void sound_object_collision::remove()
{
	if(m_b_active)
		m_collision_tree->erase(m_collision_obj);

	m_b_active = false;
}

void sound_object_collision::insert()
{
	m_collision_tree->insert(m_collision_obj, math::float4x4().identity() );
	m_b_active = true;
}

void sound_object_collision::set_matrix(math::float4x4 const* m)
{
	ASSERT(initialized());
	if(!m_b_active)	
		return;

	m_collision_tree->move(m_collision_obj, *m);
}

xray::math::aabb sound_object_collision::get_aabb()
{
	return m_collision_obj->get_aabb();
}

bool sound_object_collision::initialized()
{
	return (m_collision_obj!=nullptr);
}

void sound_object_collision::create_from_geometry(bool user_geom, sound_object_instance* o, geometry_instance* geom, object_type t)
{
	if(user_geom)
		m_user_geometry	= geom;

	m_collision_obj = NEW(sound_object_collision_dynamic)(geom, o, t);
}
