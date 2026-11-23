////////////////////////////////////////////////////////////////////////////
//	Created		: 04.06.2010
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef RESOURCES_RESOURCE_LINK_H_INCLUDED
#define RESOURCES_RESOURCE_LINK_H_INCLUDED

#include <xray/intrusive_list.h>
#include <xray/threading_simple_lock.h>

namespace xray {
namespace resources {

class resource_base;
class child_resource_ptr_base;

class XRAY_CORE_API resource_link 
{
public:
	bool				is_quality_link	() const { return quality_value != not_a_quality_link; }

	resource_base *				resource;
	resource_link *				next_link;
	u32							quality_value;
	enum						{ not_a_quality_link = u32(-1) };
	resource_link	() : resource(nullptr), next_link(nullptr), quality_value((u32)not_a_quality_link) { }

}; // class resource_link

typedef		intrusive_list< resource_link, resource_link *, & resource_link::next_link, 
							threading::simple_lock, size_policy >
			resource_link_list;

resource_link *	  resource_link_list_next_no_dying	(resource_link const * object);
resource_link *	  resource_link_list_front_no_dying (resource_link_list const & list);



} // namespace resources
} // namespace xray

template class XRAY_CORE_API	xray::intrusive_list< xray::resources::resource_link, xray::resources::resource_link *, & xray::resources::resource_link::next_link, 
								xray::threading::simple_lock, xray::size_policy >;

#endif // #ifndef RESOURCES_RESOURCE_LINK_H_INCLUDED
