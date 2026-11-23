////////////////////////////////////////////////////////////////////////////
//	Created		: 20.01.2011
//	Author		: 
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/resources_command.h>
#include <xray/resources_query_result.h>
#include <xray/resources_fs_task.h>
#include "resources_fs_impl.h"

namespace xray {
namespace resources {

query_result *   resource_command::cast_query_result ()
{
	if ( m_type == type_query )
		return					reinterpret_cast<query_result *>(this);

	return						nullptr;
}

fs_task *   resource_command::cast_fs_task ()
{
	if ( m_type == type_fs_task )
		return					reinterpret_cast<fs_task *>(this);

	return						nullptr;
}

} // namespace resources
} // namespace xray
