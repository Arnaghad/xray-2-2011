////////////////////////////////////////////////////////////////////////////
//	Created		: 10.06.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////
#ifndef EDITOR_CREATOR_H_INCLUDED
#define EDITOR_CREATOR_H_INCLUDED

namespace xray {
namespace sound { struct world; }
namespace render { class world; }
namespace sound_editor {

	class sound_editor_manager;

	class editor_creator
	{
	public:
		static sound_editor_manager* create_manager(sound::world& sound_world, render::world& render_world);
		static void set_memory_allocator(allocator_type* allocator);
	}; // class editor_creator

} // namespace sound_editor
} // namespace xray
#endif // #ifndef EDITOR_CREATOR_H_INCLUDED
