////////////////////////////////////////////////////////////////////////////
//	Created		: 10.06.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////
#ifndef EDITOR_CREATOR_H_INCLUDED
#define EDITOR_CREATOR_H_INCLUDED

namespace xray {
namespace render { class world; }
namespace ui { struct world; }

namespace animation_editor {

class editor_creator
{
public:
	static void set_memory_allocator( allocator_type* allocator );
}; // class editor_creator

} // namespace animation_editor
} // namespace xray
#endif // #ifndef EDITOR_CREATOR_H_INCLUDED
