////////////////////////////////////////////////////////////////////////////
//	Created		: 01.06.2010
//	Author		: Armen Abroyan
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_CORE_RENDER_INCLUDE_H_INCLUDED
#define XRAY_RENDER_CORE_RENDER_INCLUDE_H_INCLUDED

#include <xray/os_preinclude.h>
#undef NOUSER
#undef NOMSG
#include <xray/os_include.h>

#	define VK_USE_PLATFORM_WIN32_KHR
#	include <vulkan/vulkan.h>

namespace xray {
namespace render {

struct text_metrics
{
	int	height;
	int	ascent;
	int	descent;
	int	internal_leading;
	int	external_leading;
	int	ave_char_width;
	int	max_char_width;
};

} // namespace render
} // namespace xray

#endif // #ifndef XRAY_RENDER_CORE_RENDER_INCLUDE_H_INCLUDED
