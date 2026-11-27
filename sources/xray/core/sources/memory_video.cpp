////////////////////////////////////////////////////////////////////////////
//	Created		: 25.06.2010
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2010
//	Description	: Video memory queries (cross-platform default for core; actual VRAM detection lives in Vulkan RHI)
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/platform_extensions.h>

u64 xray::platform::get_local_video_memory_size()
{
	return get_minimal_local_video_memory_size();
}

u64 xray::platform::get_minimal_local_video_memory_size()
{
	return 256 * Mb;
}
