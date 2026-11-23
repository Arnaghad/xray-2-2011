////////////////////////////////////////////////////////////////////////////
//	Created		: 02.08.2011
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/vfs/association.h>
#include "mount_root_node_base.h"
#include "saving_info_tree.h"
#include "find.h"

namespace xray {
namespace vfs {

template <platform_pointer_enum T>
pvoid   base_node<T>::get_mount_root_user_data	() const
{
	mount_root_node_base<T> * const mount_root	=	get_mount_root();
	if ( !mount_root )
		return								nullptr;
	if ( !mount_root->mount ) 
		return								nullptr;
		
	if ( !mount_root->mount )
		return								nullptr;
	return									mount_root->mount->user_data;
}

template <platform_pointer_enum T>
void   base_node<T>::set_mount_root_user_data	(pvoid data)
{
	mount_root_node_base<T> * const mount_root	=	get_mount_root();
	R_ASSERT								(mount_root);
	R_ASSERT								(mount_root->mount);
	R_ASSERT								(mount_root->mount->user_data == 0);

	mount_root->mount->user_data		=	data;
}

template <platform_pointer_enum T>
void   base_node<T>::get_full_path		(fs_new::virtual_path_string * out_string) const
{
	out_string->clear						();
	base_node_list_node<T> * root		=	nullptr;

	for ( base_node const *	it_node		=	this; 
							it_node && it_node->get_name()[0]; 
							it_node		=	it_node->get_parent() ? cast_node(it_node->get_parent()) : nullptr )
	{
		base_node_list_node<T> * const new_root	=	(base_node_list_node<T> *)ALLOCA(sizeof(base_node_list_node<T>));
		new_root->next					=	root;
		new_root->node					=	it_node;
		root							=	new_root;
	}
 
 	for ( base_node_list_node<T> *	it_list_node	=	root;
									it_list_node	!=	nullptr;
									it_list_node	=	it_list_node->next )
	{
		* out_string					+=	it_list_node->node->get_name();		
		if ( it_list_node->next )
			* out_string				+=	fs_new::virtual_path_string::separator;
	}
}

template <>
bool    base_node<platform_pointer_default>::is_expanded		() const
{
	for ( base_node<> * node	=	(base_node<>*)this;
						node;
						node	=	node->get_next_overlapped() )
	{
		if ( need_physical_mount_or_async(node, (find_enum)0, traverse_node) )
			return							false;		
	}

	return									true;
}

template <>
bool    base_node<platform_pointer_default>::is_archive_check_overlapped	() const
{
	for ( base_node<> * node	=	(base_node<>*)this;
						node;
						node	=	node->get_next_overlapped() )
	{
		if ( node->is_archive() )
			return							true;
	}

	return									false;
}

// Emit these out-of-line template members in optimized builds too. Calls on
// null pointers in an unused function can be optimized away and are not an
// instantiation contract.
template pvoid base_node<platform_pointer_32bit>::get_mount_root_user_data() const;
template pvoid base_node<platform_pointer_64bit>::get_mount_root_user_data() const;
template void base_node<platform_pointer_32bit>::set_mount_root_user_data(pvoid);
template void base_node<platform_pointer_64bit>::set_mount_root_user_data(pvoid);
template void base_node<platform_pointer_32bit>::get_full_path(fs_new::virtual_path_string *) const;
template void base_node<platform_pointer_64bit>::get_full_path(fs_new::virtual_path_string *) const;
template vfs_mount * mount_of_node<platform_pointer_32bit>(base_node<platform_pointer_32bit> *);
template vfs_mount * mount_of_node<platform_pointer_64bit>(base_node<platform_pointer_64bit> *);

} // namespace vfs
} // namespace xray
