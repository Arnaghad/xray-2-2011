////////////////////////////////////////////////////////////////////////////
//	Created		: 12.10.2011
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef ANIMATION_SINGLE_ITEM_H_INCLUDED
#define ANIMATION_SINGLE_ITEM_H_INCLUDED

#include "animation_item.h"
#include <xray/animation/api.h>

namespace xray {
	namespace animation
	{ 
		class animation_expression_emitter;
		typedef resources::resource_ptr < animation_expression_emitter, resources::unmanaged_intrusive_base > animation_expression_emitter_ptr;

		namespace mixing { class expression; }
	}
	namespace animation_editor
	{
		class animation_collections_editor_native;

		class animation_single_item: public animation_item
		{
		public:
								animation_single_item	( animation_collections_editor_native* parent_editor, const char* animation_name );
			virtual				~animation_single_item	();
			
			static const char*	extension;

			virtual animation_item_type type			() const override { return animation_item_type::animation_single_item; }
			virtual bool		can_emit				() const override { return is_loaded(); }

			void				load					();
			virtual	expression	emit					( mutable_buffer& buffer, bool& is_last_animation ) override;

		private:
			void				item_loaded_callback	( xray::resources::queries_result& data );

		private:
			animation::animation_expression_emitter_ptr* m_animation_ptr;		

		}; // class animation_single_item
	} // namespace animation_editor
} // namespace xray

#endif // #ifndef ANIMATION_SINGLE_ITEM_H_INCLUDED
