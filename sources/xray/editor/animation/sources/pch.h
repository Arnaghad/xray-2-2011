////////////////////////////////////////////////////////////////////////////
//	Created		: 26.05.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#define XRAY_ANIMATION_EDITOR_BUILDING

#define XRAY_LOG_MODULE_INITIATOR	"animation_editor"
#include <xray/editor/base/editor_extensions.h>
#include <xray/render/facade/editor_renderer.h>

#include "animation_editor_memory.h"
#include <xray/render/world.h>

#include <xray/editor/base/images/images16x16.h>

#include <xray/editor/base/unmanaged_string.h>

namespace xray {
namespace animation_editor {
	using xray::editor_base::unmanaged_string;
} // namespace animation_editor
} // namespace xray


#endif // #ifndef PCH_H_INCLUDED
