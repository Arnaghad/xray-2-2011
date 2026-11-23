////////////////////////////////////////////////////////////////////////////
//	Created		: 10.10.2011
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "animation_item.h"

namespace xray{
namespace animation_editor{

animation_item::animation_item ( animation_collections_editor_native* parent_editor )
{
	m_parent_editor		= parent_editor;
	m_is_loaded			= true;
}

} // namespace animation_editor
} // namespace xray
