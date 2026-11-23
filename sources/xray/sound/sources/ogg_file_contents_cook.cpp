////////////////////////////////////////////////////////////////////////////
//	Created 	: 24.02.2011
//	Author		: Dmitriy Kulikov
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "ogg_file_contents_cook.h"
#include "ogg_file_contents.h"
#include "sound_world.h"

namespace xray {
namespace sound {

ogg_file_contents_cook::ogg_file_contents_cook( ) 
	:super(resources::ogg_file_contents_class, reuse_false, use_resource_manager_thread_id)
{
}

ogg_file_contents_cook::~ogg_file_contents_cook( )
{
}

void ogg_file_contents_cook::translate_query( resources::query_result_for_cook& parent )
{
	resources::request request_array[] = {	
		{ parent.get_requested_path(), resources::ogg_raw_file }
//		{ "request_options", resources::lua_config_class },
	};
	query_resources	(
						request_array, 
						array_size(request_array), 
						[this](auto&& arg1) { return on_sub_resources_loaded(std::forward<decltype(arg1)>(arg1)); }, 
						parent.get_user_allocator(), 
						nullptr,
						&parent );
}

void ogg_file_contents_cook::on_sub_resources_loaded( resources::queries_result& data )
{
	R_ASSERT(data.is_successful());
	resources::query_result_for_cook* const parent	= data.get_parent_query();

	resources::managed_resource_ptr ogg_raw_file	= data[0].get_managed_resource();

	ogg_file_contents* file_contents;
	file_contents = unmanaged_new<ogg_file_contents>( ogg_raw_file );

	if ( !file_contents )
	{
		parent->set_out_of_memory	( resources::unmanaged_memory, sizeof( file_contents ) );
		parent->finish_query		( result_out_of_memory );
		return;
	}

	parent->set_unmanaged_resource	( file_contents, resources::nocache_memory, sizeof( file_contents ) );
	parent->finish_query			( result_success );
}

void ogg_file_contents_cook::delete_resource( resources::resource_base* res )
{
	UNMANAGED_DELETE				( res );
}

} // namespace sound
} // namespace xray
