////////////////////////////////////////////////////////////////////////////
//	Created		: 22.04.2010
//	Author		: Andrew Kolomiets
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "sound_cooker.h"
#include "sound_world.h"
#include "sound_stream.h"
#include "sound_object.h"

namespace xray {
namespace sound {

sound_cooker::sound_cooker() 
: super(resources::sound_class, reuse_false, use_cook_thread_id)
{
}

void sound_cooker::translate_query( resources::query_result& parent )
{
	pcstr req_path = parent.get_requested_path();

	query_resource	(
						req_path, 
						resources::sound_stream_class,
						[this](auto&& arg1) { return on_sub_resources_loaded(std::forward<decltype(arg1)>(arg1)); }, 
						parent.get_user_allocator( ), 
						nullptr,
						&parent 
					);
}

void sound_cooker::on_sub_resources_loaded( resources::queries_result& data )
{
	R_ASSERT									( data.is_successful() );
	
	sound_stream_ptr stream						= static_cast_resource_ptr<sound_stream_ptr>(data[0].get_unmanaged_resource());

	sound_object_impl* created_resource			= MT_NEW(sound_object_impl)( stream );
	created_resource->set_thread_allocator		( data.get_user_allocator( ) );

	resources::query_result_for_cook* parent	= data.get_parent_query();
	parent->set_unmanaged_resource				( resources::unmanaged_resource_ptr(created_resource), resources::nocache_memory, sizeof(sound_object_impl) );
	parent->finish_query				( result_success );
}

void sound_cooker::delete_resource( resources::unmanaged_resource* res)
{
	MT_DELETE		( res );
}

} // namespace sound
} // namespace xray
