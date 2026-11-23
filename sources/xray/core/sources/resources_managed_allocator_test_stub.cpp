//////////////////////////////////////////////////////////////////////////////
//	Created		: 06.05.2010
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2010
//////////////////////////////////////////////////////////////////////////////

#include "pch.h"

#include <xray/managed_allocator.h>
#include "resources_managed_allocator.h"

#if 1

namespace xray {
namespace resources {

void   managed_resource_allocator::test_unmovables_list_valid () const
{
}

void   managed_resource_allocator::test_unmovable_init (u32 const /*start_ms*/)
{
}

void   managed_resource_allocator::test_unmovable_unlock_expired (u32 const /*cur_ms*/)
{
}

} // namespace resources

namespace memory {

void   managed_allocator::test_unmovables_list_valid () const
{
}

void   managed_allocator::test_unmovable_init (u32 const /*start_ms*/)
{
}

void   managed_allocator::test_unmovable_unlock_expired (u32 const /*cur_ms*/)
{
}

} // namespace memory
} // namespace xray

#endif // #if 1
