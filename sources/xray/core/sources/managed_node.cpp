////////////////////////////////////////////////////////////////////////////
//	Created		: 01.02.2011
//	Author		: 
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "managed_node.h"

namespace xray {
namespace memory {

//----------------------------------------------------
// managed_node
//----------------------------------------------------

managed_node::managed_node (managed_node_type const type, size_t const size)
{
	m_is_unmovable						=	false;
	m_pin_count							=	0;
	m_type								=	(char)type;
	m_size								=	size;
	m_next								=	nullptr;
	m_prev								=	nullptr;
	m_next_free							=	nullptr;
	m_owner								=	nullptr;
	m_next_pinned						=	nullptr;
	m_next_wait_for_free				=	nullptr;
	m_defrag_iteration					=	0;
	m_place_pos							=	0;
	m_unpin_notify_allocator			=	nullptr;
}

void   managed_node::init (managed_node * node)
{
	m_owner								=	node->owner();
}

void   managed_node::set_is_unmovable (bool is_unmovable)
{
	threading::interlocked_exchange		(m_is_unmovable, is_unmovable);
}

managed_node *   managed_node::find_prev_free () const
{
	managed_node * cur					=	m_prev;
	while ( cur )
	{
		if ( cur->is_free() )
		{
			break;
		}

		cur								=	cur->m_prev;
	}

	return									cur;
}

} // namespace memory
} // namespace xray
