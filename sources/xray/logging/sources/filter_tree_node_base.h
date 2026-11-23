////////////////////////////////////////////////////////////////////////////
//	Created		: 30.08.2011
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef RULE_TREE_NODE_BASE_H_INCLUDED
#define RULE_TREE_NODE_BASE_H_INCLUDED

#include <xray/intrusive_double_linked_list.h>

namespace xray {
namespace logging {
namespace filter_tree {

enum	{ max_name_length = 32 };
typedef fixed_string<max_name_length>	node_name_type;
class	node_base;

struct compare_nodes	
{
	bool	operator () (node_base const & left, node_base const & right) const;
	bool	operator () (pcstr const left, node_base const & right) const;
	bool	operator () (node_base const & left, pcstr const right) const;
};


class node_base
{
public:
	node_base								(pcstr name) : name(name) {}
	node_base * m_tree_next;
	node_base * m_tree_prev;
	node_name_type							name;
};

typedef xray::intrusive_double_linked_list< node_base, node_base*, &node_base::m_tree_next, &node_base::m_tree_prev > nodes_tree_type;

} // namespace filter_tree
} // namespace logging
} // namespace xray

#endif // #ifndef RULE_TREE_NODE_BASE_H_INCLUDED
