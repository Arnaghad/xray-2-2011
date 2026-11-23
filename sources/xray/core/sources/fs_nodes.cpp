//////////////////////////////////////////////////////////////////////////////
//	Created		: 06.05.2010
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2010
//////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "fs_nodes.h"
#include "fs_impl.h"

namespace xray {
namespace fs   {

template <platform_pointer_enum pointer_for_fat_size>
fat_node<pointer_for_fat_size> *   fat_node<pointer_for_fat_size>::find_referenced_link_node () const
{
	if ( !is_link() )
		return								nullptr;

	if ( is_hard_link() )
	{
		fat_node<pointer_for_fat_size> * const out_referenced_node	=	cast_db_hard_link_node()->reference_node();
		return								out_referenced_node;
	}

	u32 const thread_id					=	threading::current_thread_id();
	R_ASSERT_U								(thread_id == resources::g_resources_manager->resources_thread_id() || 
											 thread_id == resources::g_resources_manager->mount_and_fs_iterator_dispatch_mutex_hold_thread_id() || 
											 current_thread_locked_fat_for_read());

	path_string								full_path;
	cast_db_soft_link_node()->absolute_path_to_referenced	(& full_path);

	fat_node<pointer_for_fat_size> * const out_referenced_node	=	
		(fat_node<pointer_for_fat_size> *)g_fat->impl()->find_node_no_mount(full_path.c_str());
	
	R_ASSERT								(out_referenced_node);

	return									out_referenced_node;
}

template <>
bool   fat_node<platform_pointer_default>::is_root_of_sub_fat () const
{
	sub_fat_resource * const sub_fat	=	g_fat->impl()->get_db(this);
	if ( sub_fat && sub_fat->root_node == this )
		return								true;

	return									false;
}

void   fat_disk_node_base::set_node_is_locked (bool lock)	
{ 
	if ( lock )	
		threading::interlocked_increment	(m_node_lock_count);
	else			
	{
		R_ASSERT							(m_node_lock_count);
		threading::interlocked_decrement	(m_node_lock_count);
	}
}

}

void   set_fat_node_is_locked (fat_node<> * node, bool lock)
{
	if ( !node->is_disk() )
		return;

	if ( fat_disk_file_node<> * disk_node = node->cast_disk_file() )
		disk_node->set_node_is_locked		(lock);
	else if ( fat_disk_folder_node<> * disk_folder = node->cast_disk_folder() )
		disk_folder->set_node_is_locked		(lock);
}

template class fat_node						<platform_pointer_32bit>;
template class fat_node						<platform_pointer_64bit>;
template class fat_folder_node				<platform_pointer_32bit>;
template class fat_folder_node				<platform_pointer_64bit>;
template class fat_disk_folder_node			<platform_pointer_32bit>;
template class fat_disk_folder_node			<platform_pointer_64bit>;
template class fat_disk_file_node			<platform_pointer_32bit>;
template class fat_disk_file_node			<platform_pointer_64bit>;
template class fat_db_file_node				<platform_pointer_32bit>;
template class fat_db_file_node				<platform_pointer_64bit>;
template class fat_db_compressed_file_node	<platform_pointer_32bit>;
template class fat_db_compressed_file_node	<platform_pointer_64bit>;

} // namespace fs 
} // namespace xray 
