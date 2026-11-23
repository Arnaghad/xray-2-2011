////////////////////////////////////////////////////////////////////////////
//	Created		: 02.09.2010
//	Author		: Armen Abroyan
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_BASE_TERRAIN_BASE_H_INCLUDED
#define XRAY_RENDER_BASE_TERRAIN_BASE_H_INCLUDED

#include <xray/render/engine/base_classes.h>
#include <xray/render/facade/terrain_memory_profile.h>

namespace xray {
namespace render {

struct terrain_cook_user_data {
	scene_ptr	scene;
	pcstr		current_project_resource_path;
    terrain_memory_profile memory_profile=terrain_memory_profile::standard_256_mib;
};


} // namespace render
} // namespace xray

#endif // #ifndef XRAY_RENDER_BASE_TERRAIN_BASE_H_INCLUDED
