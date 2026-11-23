////////////////////////////////////////////////////////////////////////////
//	Created		: 10.10.2011
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "animation_collection_item.h"
#include "animation_single_item.h"
#include "animation_collections_editor_native.h"
#include <xray/animation/api.h>
#include <xray/animation/mixing_expression.h>
#include <xray/animation/animation_expression_emitter.h>

namespace xray{
namespace animation_editor{

using xray::animation::animation_expression_emitter_ptr;

const char* animation_collection_item::extension = ".anim_collection";

animation_collection_item::animation_collection_item( animation_collections_editor_native* parent_editor, const char* collection_name ): animation_item( parent_editor )
{
	m_name = collection_name;
	collection_type = animation_collection_type::random;
	is_cyclic_repeat			= false;
	is_dont_repeat_previous	= false;
	is_internal_collection = false;
	m_loading_sub_items			= 0;
	m_is_child_last_animation	= true;
}

animation_collection_item::~animation_collection_item()
{
	for(auto it : m_items)
		DELETE(it); // Note: this depends on whether the items are owned by the collection or manager
}

static bool is_correct_collection( animation_collection_item* item )
{
	for( auto sub_item : item->items() )
	{
		if( sub_item->type() != animation_item_type::animation_collection_item )
			continue;

		animation_collection_item* collection = static_cast<animation_collection_item*>( sub_item );

		if( collection->items().empty() )
			return false;

		return is_correct_collection( collection );
	}
	return true;
}

bool animation_collection_item::is_correct() const
{
	if( m_items.empty() )
		return false;

	return is_correct_collection( const_cast<animation_collection_item*>(this) );
}

bool animation_collection_item::can_save ( )
{
	if( m_items.empty() )
		return false;

	for( auto item : m_items )
	{
		if( item->type() == animation_item_type::animation_collection_item )
		{
			animation_collection_item* collection_item = static_cast<animation_collection_item*>( item );
			if( !collection_item->can_save( ) )
				return false;
		}
	}

	return true;
}

void animation_collection_item::save ( )
{
	// Path logic needs to be moved to manager
}

void animation_collection_item::save ( configs::lua_config_value& config_value )
{
	save_anim_item( config_value, this );
}

void animation_collection_item::load ( )
{
	// Resource query logic
}

void animation_collection_item::load ( configs::lua_config_value const& config_value )
{
	load_anim_item ( config_value, this );

	if( m_loading_sub_items != 0 )
		return;

	m_is_loaded = true;
	if (on_loaded)
		on_loaded( this );
}

void animation_collection_item::add_item ( animation_item* item )
{
	insert_item( (u32)m_items.size(), item );
}

void animation_collection_item::insert_item	( u32 index, animation_item* item )
{
	m_items.insert( m_items.begin() + index, item );

	if( !item->is_loaded() )
	{
		m_is_loaded		= false;
		++m_loading_sub_items;
		item->on_loaded = std::bind(&animation_collection_item::sub_item_loaded_callback, this, std::placeholders::_1);
	}
}

void animation_collection_item::remove_item ( animation_item* item )
{
	auto it = std::find(m_items.begin(), m_items.end(), item);
	if(it != m_items.end())
		m_items.erase(it);
}

void animation_collection_item::remove_item_at ( u32 index )
{
	if(index < m_items.size())
		m_items.erase(m_items.begin() + index);
}

xray::animation::mixing::expression animation_collection_item::emit ( mutable_buffer& buffer, bool& is_last_animation )
{
	R_ASSERT( !m_items.empty() );
	
	bool is_child_last_animation;

	switch ( collection_type )
	{
	case animation_collection_type::random:
	{
		is_last_animation				= !is_cyclic_repeat;
		u32 const animations_count		= (u32)m_items.size();

		if( !is_dont_repeat_previous )
		{
			std::uniform_int_distribution<u32> dist(0, animations_count - 1);
			m_current_animation_index	= dist(m_random);
			break;
		}

		u32 previous_animation_index	= m_current_animation_index;
		do
		{
			std::uniform_int_distribution<u32> dist(0, animations_count - 1);
			m_current_animation_index	= dist(m_random);
		} while ( previous_animation_index == m_current_animation_index );
	
		break;
	}

	case animation_collection_type::sequential:
	{
		if( !m_is_child_last_animation )
			break;

		u32 const animations_count			= (u32)m_items.size();
		m_current_animation_index			= ( m_current_animation_index + 1 ) % animations_count;
		is_last_animation					= !is_cyclic_repeat && ( m_current_animation_index == animations_count - 1 );
		break;
	}

	default:
		NODEFAULT							( m_current_animation_index = u32(-1) );
	}	

	expression ret_expression	= m_items[ m_current_animation_index ]->emit( buffer, is_child_last_animation );
	m_is_child_last_animation	= is_child_last_animation;
	is_last_animation			= is_last_animation && m_is_child_last_animation;

	return ret_expression;
}

void animation_collection_item::load_anim_item ( configs::lua_config_value const& config_value, animation_collection_item* item )
{
	item->m_is_loaded						= false;
	item->is_cyclic_repeat				= config_value["is_cyclic_repeat"];
	item->is_dont_repeat_previous			= config_value["is_dont_repeat_previous"];
	
	const char* type_str = config_value["collection_type"];
	item->collection_type = (strcmp(type_str, "sequential") == 0) ? animation_collection_type::sequential : animation_collection_type::random;

	item->m_items.clear();

	if( !config_value.value_exists( "animation_items" ) )
		return;

	configs::lua_config_value items_value = config_value["animation_items"];
	int count = items_value.size( );
	for ( int i = 0; i < count; ++i )
	{
		animation_item_type type		= (animation_item_type)(int)items_value[i]["type"];
	
		switch( type )
		{
		case animation_item_type::animation_single_item:
		{
			const char* name = items_value[i]["path"];
			animation_single_item* sub_item	= m_parent_editor->get_single( name );
			m_items.push_back( sub_item );

			if( sub_item->is_loaded() )
				continue;

			++m_loading_sub_items;
			sub_item->on_loaded = std::bind(&animation_collection_item::sub_item_loaded_callback, this, std::placeholders::_1);
			break;
		}

		case animation_item_type::animation_composite_item:
		case animation_item_type::animation_collection_item:
		{
			animation_collection_item* sub_item;

			if( items_value[i].value_exists( "name" ) )
			{
				const char* name = items_value[i]["name"];
				sub_item							= NEW(animation_collection_item)( m_parent_editor, name );
				sub_item->is_internal_collection	= true;
				sub_item->load						( items_value[i] );
				m_items.push_back( sub_item );
			}
			else
			{
				const char* name = items_value[i]["path"];
				sub_item							= m_parent_editor->get_collection( name );
				m_items.push_back( sub_item );
			}

			if( sub_item->is_loaded() )
				continue;

			++m_loading_sub_items;
			sub_item->on_loaded = std::bind(&animation_collection_item::sub_item_loaded_callback, this, std::placeholders::_1);
			break;
		}

		default:
			NODEFAULT( );
		}
	}
}

void animation_collection_item::save_anim_item ( configs::lua_config_value& config_value, animation_collection_item* item )
{
	config_value["is_cyclic_repeat"]			= item->is_cyclic_repeat;
	config_value["is_dont_repeat_previous"]		= item->is_dont_repeat_previous;
	config_value["collection_type"]				= (item->collection_type == animation_collection_type::sequential) ? "sequential" : "random";

	u32 count = (u32)item->items().size();
	if( count == 0 )
		return;

	configs::lua_config_value items_value = config_value["animation_items"];
	for ( u32 i = 0; i < count; ++i )
	{
		items_value[i]["type"] = (int)m_items[i]->type();

		animation_item* sub_item = m_items[i];
		if( sub_item->type() == animation_item_type::animation_collection_item )
		{
			animation_collection_item* collection_item = static_cast<animation_collection_item*>( sub_item );
			if( collection_item->is_internal_collection )
			{	
				items_value[i]["name"] = collection_item->name();
				configs::lua_config_value item_value = items_value[i];
				collection_item->save( item_value );
				continue;
			}
		}

		items_value[i]["path"] = sub_item->name();
	}
}

void animation_collection_item::item_loaded_callback(xray::resources::queries_result& data)
{
	R_ASSERT( data.is_successful( ) );
	
	configs::lua_config_ptr cfg = static_cast_resource_ptr<configs::lua_config_ptr>( data[0].get_unmanaged_resource( ) );
	load( cfg->get_root( )["collection"] );

	if( m_is_loaded )
		if (on_loaded)
			on_loaded( this );
}

void animation_collection_item::sub_item_loaded_callback ( animation_item* )
{
	--m_loading_sub_items;
	if( m_loading_sub_items == 0 )
	{
		m_is_loaded		= true;
		if (on_loaded)
			on_loaded( this );
	}
}

} // namespace animation_editor
} // namespace xray
