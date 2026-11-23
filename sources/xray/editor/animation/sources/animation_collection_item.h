////////////////////////////////////////////////////////////////////////////
//	Created		: 10.10.2011
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef ANIMATION_COLLECTION_ITEM_H_INCLUDED
#define ANIMATION_COLLECTION_ITEM_H_INCLUDED

#include "animation_item.h"
#include <vector>
#include <random>

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

		enum class animation_collection_type
		{
			random,
			sequential
		};

		class animation_collection_item: public animation_item
		{
		private:
			typedef animation::animation_expression_emitter_ptr animation_expression_emitter_ptr;
			typedef animation::mixing::expression expression;

		public:
								animation_collection_item	( animation_collections_editor_native* parent_editor, const char* collection_name );
			virtual				~animation_collection_item	();
			
			static const char*	extension;

			virtual animation_item_type type			() const override { return animation_item_type::animation_collection_item; }
			virtual bool		can_emit				() const override { return is_loaded() && !m_items.empty(); }

			std::vector<animation_item*>& items			() { return m_items; }

			bool				is_cyclic_repeat;
			bool				is_dont_repeat_previous;
			bool				is_internal_collection;
			animation_collection_type collection_type;
			
			bool				is_correct				() const;

		public:
			bool				can_save				();
			void				save					();
			void				save					( configs::lua_config_value& config_value );
			void				load					();
			void				load					( configs::lua_config_value const& config_value );
			void				add_item				( animation_item* item );
			void				insert_item				( u32 index, animation_item* item );
			void				remove_item				( animation_item* item );
			void				remove_item_at			( u32 index );
			virtual		expression emit					( mutable_buffer& buffer, bool& is_last_animation ) override;

		private:
			void				load_anim_item			( configs::lua_config_value const& config_value, animation_collection_item* item );
			void				save_anim_item			( configs::lua_config_value& config_value, animation_collection_item* item );
			void				item_loaded_callback	( xray::resources::queries_result& data );
			void				sub_item_loaded_callback( animation_item* item );

		private:
			std::vector<animation_item*>		m_items;
			u32									m_loading_sub_items;
			u32									m_current_animation_index;
			bool								m_is_child_last_animation;
			std::mt19937						m_random;

		}; // class animation_collection_item

	} // namespace animation_editor
} // namespace xray

#endif // #ifndef ANIMATION_COLLECTION_ITEM_H_INCLUDED
