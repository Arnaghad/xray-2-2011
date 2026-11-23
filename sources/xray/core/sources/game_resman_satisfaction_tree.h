////////////////////////////////////////////////////////////////////////////
//	Created		: 03.08.2010
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef GAME_RESMAN_SATISFACTION_TREES_H_INCLUDED
#define GAME_RESMAN_SATISFACTION_TREES_H_INCLUDED

#include <xray/resources.h>
#include <xray/intrusive_double_linked_list.h>

namespace xray {
namespace resources {

class compare_by_target_satisfaction	{
public: bool	operator () (resource_base const & left, resource_base const & right) const;
};



typedef xray::intrusive_double_linked_list< resource_base, resource_base*, &resource_base::m_grm_satisfaction_tree_next, &resource_base::m_grm_satisfaction_tree_prev > target_satisfaction_tree_type;

} // namespace resources
} // namespace xray

#endif // #ifndef GAME_RESMAN_SATISFACTION_TREES_H_INCLUDED
