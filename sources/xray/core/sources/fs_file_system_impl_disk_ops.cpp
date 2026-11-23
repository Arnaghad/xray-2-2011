//////////////////////////////////////////////////////////////////////////////
//	Created		: 06.05.2010
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2010
//////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/resources_fs_iterator.h>
#include <xray/resources_managed_resource.h>
#include <xray/resources_unmanaged_resource.h>
#include <xray/fs_utils.h>
#include "fs_file_system_impl.h"
#include "fs_helper.h"
#include "resources_manager.h"
#include "game_resman.h"

namespace xray {
namespace fs   {

void   deassociate_from_grm_and_fat_if_needed (fat_node<> * node)
{
	if ( node->is_link() || node->is_folder() )
		return;

	::xray::resources::managed_resource_ptr	const 	managed_resource	=	node->get_associated_managed_resource_ptr();
	::xray::resources::unmanaged_resource_ptr const unmanaged_resource	=	node->get_associated_unmanaged_resource_ptr();
	::xray::resources::resource_base * const		resource			=	managed_resource ? (::xray::resources::resource_base *)managed_resource.c_ptr() : unmanaged_resource.c_ptr();
	
	if ( !resource )
		return;

	if ( resource->is_pinned_by_grm() )
		::xray::resources::g_game_resources_manager->release_resource	(resource);

	node->set_associated					(nullptr);
}

void   file_system_impl::unmount_disk_node (pcstr physical_path)
{
	path_string					logical_path;
	if ( !convert_physical_to_logical_path(& logical_path, physical_path, false) )
		return;

	unmount_disk				(logical_path.c_str(), physical_path, respect_mount_history_false);
}

void   file_system_impl::mark_disk_node_as_erased (pcstr physical_path)
{
	path_string					logical_path;
	if ( !convert_physical_to_logical_path(& logical_path, physical_path, false) )
		return;

	u32 hash				=	0;
	fat_node<> * node_with_path	=	find_node(logical_path.c_str(), & hash);
	if ( !node_with_path )
		return;

	fat_node<> * node_to_erase	=	nullptr;
	fat_node<> * node_to_relink	=	nullptr;
	fat_node<> * cur_node	=	* m_hash_set.find(hash);
	for ( ;cur_node; cur_node = cur_node->get_next_overlapped() )
	{
		if ( !cur_node->same_path(node_with_path) )
			continue;

		if ( is_erased_node(cur_node) )
			continue;

		path_string					cur_node_path;
		get_disk_path				(cur_node, cur_node_path);

		if ( node_to_erase )
		{
			if ( cur_node_path == physical_path )
				continue;

			node_to_relink		=	cur_node;
			break;
		}

		if ( !cur_node->is_disk() )
			continue;

		if ( cur_node_path != physical_path )
			continue;
		
		R_ASSERT					(!node_to_erase);
		node_to_erase			=	cur_node;
	}

	if ( !node_to_erase )
		return;
	
	fat_disk_node_base * disk_node_base	=	node_to_erase->cast_disk_node_base();
	ASSERT						(disk_node_base);
	disk_node_base->set_is_erased	();

	deassociate_from_grm_and_fat_if_needed	(node_to_erase);

	if ( node_to_relink )
	{
		fat_folder_node<> * parent		=	node_to_erase->m_parent->cast_folder();
		if ( parent->find_child(node_to_erase) )
			parent->prepend_child			(node_to_relink);
	}
}

bool   file_system_impl::update_file_size_in_fat (pcstr in_logical_path, pcstr in_physical_path, bool do_resource_deassociation)
{
	XRAY_UNREFERENCED_PARAMETER(do_resource_deassociation);
	
	path_string					logical_path	=	in_logical_path;
	path_string					physical_path	=	in_physical_path;
	
	u32 hash = 0;
	fat_node<> * node = find_node(logical_path.c_str(), & hash);
	if ( !node || !node->is_disk() || node->is_folder() )
		return false;

	fat_disk_file_node<> * disk_file = node->cast_disk_file();
	if ( !disk_file )
		return false;

	path_string node_disk_path;
	get_disk_path(node, node_disk_path);
	
	if ( node_disk_path != physical_path )
		return false;

	file_size_type new_file_size = 0;
	if ( !calculate_file_size(&new_file_size, disk_file->m_disk_path) )
		return false;

	u32 const old_file_size = disk_file->m_size;
	disk_file->m_size = (threading::atomic32_type)new_file_size;

	if ( old_file_size != new_file_size )
	{
		if ( do_resource_deassociation )
			deassociate_from_grm_and_fat_if_needed(node);
	}

	return true;
}

bool   file_system_impl::mount_disk_node (pcstr in_logical_path, pcstr in_physical_path, ::xray::resources::fs_iterator * out_result_it, bool assert_on_fail)
{
	XRAY_UNREFERENCED_PARAMETER(assert_on_fail);
	
	threading::mutex_raii	raii(m_mount_mutex);
	verify_path_is_portable(in_logical_path);

	if ( out_result_it )
		*out_result_it = ::xray::resources::fs_iterator();

	u32 hash = 0;
	fat_node<> * const node = find_node(in_logical_path, & hash);
	if ( node && node->cast_disk_file() )
	{
		if ( out_result_it )
			*out_result_it = ::xray::resources::fs_iterator(node, nullptr);
		return true;
	}

	if ( in_physical_path && get_path_info(nullptr, in_physical_path) != path_info::type_file )
		return false;

	path_string physical_path = in_physical_path ? in_physical_path : in_logical_path;
	
	fat_disk_file_node<> * new_disk_file = create_disk_file_node(
		crc32(in_logical_path, strings::length(in_logical_path)),
		in_logical_path, strings::length(in_logical_path),
		physical_path.c_str(), physical_path.length(),
		0, memory::g_mt_allocator, true);

	fat_folder_node<> * parent = m_root;
	path_string current_path;
	
	pcstr path_ptr = in_logical_path;
	pcstr slash = strchr(path_ptr, '/');
	while ( slash )
	{
		current_path.append(path_ptr, slash);
		u32 folder_hash = crc32(current_path.c_str(), current_path.length());
		parent = find_or_create_folder(parent, current_path.c_str(), current_path.length(), folder_hash);
		path_ptr = slash + 1;
		slash = strchr(path_ptr, '/');
	}

	parent->prepend_child(new_disk_file->cast_node());
	new_disk_file->cast_node()->m_parent = parent->cast_node();

	if ( out_result_it )
		*out_result_it = ::xray::resources::fs_iterator(new_disk_file->cast_node(), nullptr);

	return true;
}

void   file_system_impl::rename_disk_node (pcstr in_logical_path, bool do_resource_deassociation, pcstr in_renamed_old_path, pcstr in_renamed_new_path)
{
	XRAY_UNREFERENCED_PARAMETER(in_logical_path);
	XRAY_UNREFERENCED_PARAMETER(do_resource_deassociation);
	XRAY_UNREFERENCED_PARAMETER(in_renamed_old_path);
	XRAY_UNREFERENCED_PARAMETER(in_renamed_new_path);
}

void   file_system_impl::rename_disk_node (pcstr in_logical_path, bool do_resource_deassociation, pcstr in_renamed_old_path, pcstr in_renamed_new_path, u32 in_full_name_of_parent_hash)
{
	XRAY_UNREFERENCED_PARAMETER(in_logical_path);
	XRAY_UNREFERENCED_PARAMETER(do_resource_deassociation);
	XRAY_UNREFERENCED_PARAMETER(in_renamed_old_path);
	XRAY_UNREFERENCED_PARAMETER(in_renamed_new_path);
	XRAY_UNREFERENCED_PARAMETER(in_full_name_of_parent_hash);
}

} // namespace fs 
} // namespace xray
