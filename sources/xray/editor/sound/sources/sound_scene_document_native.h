////////////////////////////////////////////////////////////////////////////
//	Created		: 25.06.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#ifndef SOUND_SCENE_DOCUMENT_NATIVE_H_INCLUDED
#define SOUND_SCENE_DOCUMENT_NATIVE_H_INCLUDED

#include <xray/math_extensions.h>

namespace xray {
namespace sound_editor {

	class sound_object_instance;

	class sound_scene_document_native
	{
	public:
		virtual	void	change_object_transform	(sound_object_instance* inst, math::float4x4 const& old_transform, math::float4x4 const& new_transform) = 0;
		virtual bool	is_selected				(sound_object_instance* inst) = 0;
	}; // class sound_scene_document_native

} // namespace sound_editor
} // namespace xray 

#endif // #ifndef SOUND_SCENE_DOCUMENT_NATIVE_H_INCLUDED
