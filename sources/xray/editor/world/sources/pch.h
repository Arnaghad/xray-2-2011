////////////////////////////////////////////////////////////////////////////
//	Created 	: 26.08.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#ifndef XRAY_EDITOR_BUILDING
#define XRAY_EDITOR_BUILDING
#endif
#define XRAY_LOG_MODULE_INITIATOR	"editor"

#include <xray/editor/base/editor_extensions.h>

#include "editor_memory.h"

#include <xray/editor/base/unmanaged_string.h>

namespace xray {
namespace editor {
	using xray::editor_base::unmanaged_string;
} // namespace editor
} // namespace xray

namespace xray {
	namespace render {
		namespace editor {
			class renderer;
		} // namespace editor
	} // namespace render
} // namespace xray

#endif // #ifndef PCH_H_INCLUDED
