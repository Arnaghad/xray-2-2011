////////////////////////////////////////////////////////////////////////////
//	Created		: 10.06.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "editor_creator.h"

namespace xray {
namespace animation_editor {

void editor_creator::set_memory_allocator(xray::animation_editor::allocator_type* allocator)
{
	g_allocator = allocator;
}

} // namespace animation_editor
} // namespace xray
