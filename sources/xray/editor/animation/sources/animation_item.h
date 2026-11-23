////////////////////////////////////////////////////////////////////////////
//	Created		: 10.10.2011
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef ANIMATION_ITEM_H_INCLUDED
#define ANIMATION_ITEM_H_INCLUDED

#include "animation_item_type.h"
#include <string>
#include <functional>

namespace xray {
	namespace animation { namespace mixing { class expression; } }
	namespace animation_editor {

		class animation_collections_editor_native;

		class animation_item
		{
		protected:
			typedef xray::animation::mixing::expression expression;

		public:
								animation_item		( animation_collections_editor_native* parent_editor );
			virtual				~animation_item		() {}

			const char*			name				() const { return m_name.c_str(); }
			void				set_name			( const char* value ) { m_name = value; }

			virtual animation_item_type type		() const = 0;

			bool				is_loaded			() const { return m_is_loaded; }
			
			animation_collections_editor_native* parent_editor () const { return m_parent_editor; }

			virtual bool		can_emit			() const = 0;

			std::function<void(animation_item*)> on_loaded;
			
		public:
			virtual	expression	emit				( mutable_buffer& buffer, bool& is_last_animation ) = 0;

		protected:
			std::string			m_name;
			bool				m_is_loaded;
			animation_collections_editor_native* m_parent_editor;

		}; // class animation_item

	} // namespace animation_editor
} // namespace xray

#endif // #ifndef ANIMATION_ITEM_H_INCLUDED
