////////////////////////////////////////////////////////////////////////////
//	Created		: 06.10.2011
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef ANIMATION_COLLECTIONS_EDITOR_NATIVE_H_INCLUDED
#define ANIMATION_COLLECTIONS_EDITOR_NATIVE_H_INCLUDED

#include <xray/resources_classes.h>

namespace xray {
namespace animation_editor {

	class animation_single_item;
	class animation_collection_item;

	class animation_collections_editor_native
	{
	public:
		virtual animation_single_item*		get_single		( const char* single_name ) = 0;
		virtual animation_collection_item*	get_collection	( const char* collection_name ) = 0;
	}; // class animation_collections_editor_native

} // namespace animation_editor
} // namespace xray

#endif // #ifndef ANIMATION_COLLECTIONS_EDITOR_NATIVE_H_INCLUDED
