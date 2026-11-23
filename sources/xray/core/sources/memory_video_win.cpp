////////////////////////////////////////////////////////////////////////////
//	Created		: 25.06.2010
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2010
//	Description	: simplified version for Vulkan
////////////////////////////////////////////////////////////////////////////

#undef WIN32_LEAN_AND_MEAN
#undef NOGDI
#include <windows.h>
#include "pch.h"
#include <xray/platform_extensions_win.h>
#include <xray/os_preinclude.h>
#undef NOUSER
#undef NOGDI
#undef NOMSG
#undef NONLS
#include <xray/os_include.h>

u64 xray::platform::get_local_video_memory_size	( )
{
	// Actual video memory detection is moved to xray_render_vulkan.
	// Return a safe default value for core compatibility.
	return	get_minimal_local_video_memory_size();
}

u64 xray::platform::get_minimal_local_video_memory_size	 ( )
{
	return				256*Mb;
}
