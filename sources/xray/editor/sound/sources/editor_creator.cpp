////////////////////////////////////////////////////////////////////////////
//	Created		: 10.06.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "editor_creator.h"
#include "sound_editor_manager.h"

namespace xray {
namespace sound_editor {

sound_editor_manager* editor_creator::create_manager(sound::world& sound_world, render::world& render_world)
{
	return NEW(sound_editor_manager)(sound_world, render_world);
}

void editor_creator::set_memory_allocator(xray::sound_editor::allocator_type* allocator)
{
	g_allocator = allocator;
}

}// namespace sound_editor
}// namespace xray
