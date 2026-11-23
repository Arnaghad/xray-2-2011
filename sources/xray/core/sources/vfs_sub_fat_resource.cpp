////////////////////////////////////////////////////////////////////////////
//	Created		: 11.10.2010
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/vfs_sub_fat_resource.h>
#include <xray/resources_resource_ptr.h>

namespace xray {
namespace resources {


vfs_sub_fat_resource_ptr	get_sub_fat_resource (vfs::vfs_mount_ptr mount_ptr)
{
	return									(vfs_sub_fat_resource *)mount_ptr->user_data;
}

} // namespace resources
} // namespace xray
