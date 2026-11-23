////////////////////////////////////////////////////////////////////////////
//	Created		: 05.08.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "editor_creator.h"

#include "transform_control_object.h"
#include "transform_control_base.h"

using xray::editor_base::editor_creator;
using xray::editor_base::transform_control_object;
using xray::editor_base::transform_control_base;

transform_control_object::mode transform_control_object::m_edit_mode_ = transform_control_object::mode::edit_transform;
float transform_control_base::gizmo_size = 1.0f;

void editor_creator::set_memory_allocator(xray::editor_base::allocator_type* allocator)
{
	ASSERT(!xray::editor_base::g_allocator);
	xray::editor_base::g_allocator = allocator;
}
