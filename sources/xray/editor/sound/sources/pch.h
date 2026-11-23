////////////////////////////////////////////////////////////////////////////
//	Created		: 08.07.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#define XRAY_SOUND_EDITOR_BUILDING

#define XRAY_LOG_MODULE_INITIATOR	"sound_editor"
#include <xray/editor/base/editor_extensions.h>

#include <xray/render/facade/editor_renderer.h>
#include "sound_editor_memory.h"

#include <xray/editor/base/unmanaged_string.h>

namespace xray {
namespace sound_editor {
	using xray::editor_base::unmanaged_string;
} // namespace sound_editor
} // namespace xray

#include <xray/editor/base/images/images16x16.h>
#include <xray/sound/world.h>
#include <xray/render/world.h>

#endif // #ifndef PCH_H_INCLUDED
