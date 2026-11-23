////////////////////////////////////////////////////////////////////////////
//	Created		: 03.03.2010
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "resources_manager.h"
#include "fs_file_system.h"
#include <xray/resources_fs.h>
#include "resources_fs_task_mount.h"
#include "resources_fs_task_composite.h"
#include <xray/fs_utils.h>
#include <xray/configs_binary_config.h>

namespace xray {
namespace resources {

using namespace fs;

void   add_fs_task (fs_task * const new_task, fs_task * const parent_composite_task)
{
	if ( parent_composite_task )
		parent_composite_task->add_child	(new_task);
	else
		g_resources_manager->add_fs_task	(new_task);
}

fs_task *   create_mount_composite_helper_task_and_lock (memory::base_allocator * const allocator)
{
	fs_task_composite *	new_task	=	XRAY_NEW_IMPL(* allocator, fs_task_composite)(allocator);

	new_task->lock_children				();

	g_resources_manager->add_fs_task	(new_task);
	return								new_task;
}

void   unlock_mount_composite_helper_task (fs_task * composite_task)
{
	ASSERT_CMP							(composite_task->type(), ==, fs_task::type_mount_composite);
	static_cast<fs_task_composite *>(composite_task)->unlock_children();
}

void   query_mount_disk (pcstr const						logical_path, 
						 pcstr const						disk_dir_path, 
						 bool								watch_folder,
						 query_mount_callback const			callback, 
						 memory::base_allocator * const		allocator,
						 bool const							recursively,
						 fs_task * const					parent_composite_task);

void   query_mount_db (pcstr const						logical_path, 
					   pcstr const						fat_file_path, 
					   pcstr const						db_file_path,
					   bool const 						need_replication,
					   query_mount_callback	const		callback,
					   memory::base_allocator *	const	allocator,
					   bool	const						reverse_byte_order,
					   fs_task * const					parent_composite_task);

class mount_by_config_helper
{
public:
	enum type_enum		{	type_mount, 
							type_unmount	};

	mount_by_config_helper	(type_enum const				type,
							 pcstr const					mount_id, 
							 query_mount_callback			callback,
							 memory::base_allocator * const allocator) : 
				m_type(type), m_mount_id(mount_id), m_callback(callback), m_allocator(allocator), m_success(true), m_disk_mounts_left_count(0) 
	{
		fs::path_string		path;
		path.assignf		("mounts/%s.mount", mount_id);

		request	requests[]						=	{ {path.c_str(), binary_config_class } };
		
		g_resources_manager->change_count_of_pending_helper_query_for_mount(+1);
		
		query_resource_params	params(requests, nullptr, 1, std::bind(& mount_by_config_helper::callback, this, std::placeholders::_1),
									   allocator, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, 0, query_type_helper_for_mount);

		m_mount_composite_task					=	create_mount_composite_helper_task_and_lock(allocator);
		g_resources_manager->query_resources_impl	(params);
	}

	void	callback (queries_result & config_result)
	{
		fs_task* composite_task = m_mount_composite_task;

		if ( !config_result.is_successful() )
		{
			query_db										();
		}
		else
		{
			configs::binary_config_ptr const config_ptr	=	static_cast_checked<configs::binary_config *>
															(config_result[0].get_unmanaged_resource().c_ptr());


			ASSERT( config_ptr );

			configs::binary_config_value const & config	=	config_ptr->get_root();
			query_by_config									(config);
		}

		unlock_mount_composite_helper_task					(composite_task);
		//	unlock a lock requested by create_mount_composite_helper_task_and_lock
		//	now composite task can start executing

		g_resources_manager->change_count_of_pending_helper_query_for_mount(-1);
	}

private:
	void	query_db ()
	{
		fs::path_string					path;
		path.assignf					("mounts/%s.db", m_mount_id.c_str());
		fs_new::native_path_string native_path = fs_new::native_path_string::convert(path.c_str());

		if ( m_type == type_mount )
			query_mount_db				("", native_path.c_str(), native_path.c_str(), true, m_callback, m_allocator, false, m_mount_composite_task);
		else
			query_unmount_db			("", native_path.c_str(), m_callback, m_allocator, m_mount_composite_task);

		delete_this						();
	}

	void	disk_mount_callback (bool const success)
	{
		m_success					&=	success;
		R_ASSERT						(m_disk_mounts_left_count);
		--m_disk_mounts_left_count;
		if ( !m_disk_mounts_left_count )
		{
			if ( m_callback )
				m_callback				(mount_ptr());  // signal completion with null mount_ptr
			delete_this					();
		}
	}

	void	query_by_config (configs::binary_config_value const & config)
	{
		configs::binary_config_value const & mounts	=	config["mounts"];
		m_disk_mounts_left_count					=	mounts.size();
		m_success									=	true;
	
		int start_index								=	(m_type == type_mount) ? 0 : (int)mounts.size() - 1;
		int end_index								=	(m_type == type_mount) ? (int)mounts.size() : -1;
		int step									=	(m_type == type_mount) ? +1 : -1;

		for ( int index = start_index; index != end_index; index += step )
		{
			configs::binary_config_value const & value	=	mounts[index];

			R_ASSERT								(value.value_exists("physical_path"), "physical_path must be set in mount configs");
			fs::path_string	const physical_path	=	(pcstr)value["physical_path"];

			R_ASSERT								(value.value_exists("virtual_path"), "virtual_path must be set in mount configs");
			fs::path_string	const virtual_path	=	(pcstr)value["virtual_path"];

			if ( value.value_exists("create_physical_path_if_not_exists") && 
				 (u32)value["create_physical_path_if_not_exists"] == 1 )
			{
				fs::make_dir_r						(physical_path.c_str());
			}

			bool const watcher_enabled			=	value.value_exists("watcher_enabled") ?
														(bool)value["watcher_enabled"] : true;
			bool const lazy_mount				=	value.value_exists("lazy_mount") ?
														(bool)value["lazy_mount"] : true;

			if ( m_type == type_mount )
			{
				query_mount_disk					(virtual_path.c_str(), 
													 physical_path.c_str(),
													 watcher_enabled,
													 std::bind(& mount_by_config_helper::disk_mount_callback, this, std::placeholders::_1), 
													 m_allocator,
													 !lazy_mount,
													 m_mount_composite_task);
			}
			else
			{
				query_unmount_disk					(virtual_path.c_str(), 
													 physical_path.c_str(), 
													 std::bind(& mount_by_config_helper::disk_mount_callback, this, std::placeholders::_1), 
													 m_allocator,
													 m_mount_composite_task);
			}
		}
	}

	void	delete_this ()
	{
		mount_by_config_helper * this_ptr	=	this;
		XRAY_DELETE_IMPL			(m_allocator, this_ptr);
	}

	fs_task *					m_mount_composite_task;
	u32							m_disk_mounts_left_count;
	bool						m_success;
	type_enum					m_type;
	memory::base_allocator *	m_allocator;
	query_mount_callback		m_callback;
	fs::path_string				m_mount_id;
};

void   query_mount (pcstr const						mount_id,
				    query_mount_callback const		callback,
					memory::base_allocator * const	allocator)
{
	XRAY_NEW_IMPL(allocator, mount_by_config_helper)(mount_by_config_helper::type_mount, mount_id, callback, allocator);
	// memory will be freed by the object itself
}

void   query_unmount (pcstr const						mount_id,
				      query_mount_callback const		callback,
					  memory::base_allocator * const	allocator)
{
	XRAY_NEW_IMPL(allocator, mount_by_config_helper)(mount_by_config_helper::type_unmount, mount_id, callback, allocator);
	// memory will be freed by the object itself
}

void   query_continue_disk_mount (pcstr 				logical_path, 
								  pcstr 				disk_dir_path, 
								  bool					recursively,
								  query_mount_callback	callback,
								  memory::base_allocator *	allocator)
{
	if ( threading::current_thread_id() == g_resources_manager->resources_thread_id() )
	{
		fs_iterator it				=	g_fat->continue_mount_disk	(logical_path, 
																	 recursively ? file_system::recursive_true : file_system::recursive_false);
		// Bridge: old code passed fs_iterator to callback, but callback now expects mount_ptr
		callback						(mount_ptr());
		return;
	}

	fs_task * const	new_task		=	XRAY_NEW_IMPL(* allocator, fs_task)(fs_task::type_mount_disk, allocator);

	new_task->logical_path			=	logical_path;
	new_task->disk_dir_path			=	disk_dir_path;
	// Bridge: wrap query_mount_callback into std::function<void(bool)> for fs_task::mount_callback
	new_task->mount_callback		=	[callback](bool success) { callback(mount_ptr()); };
	new_task->fs_iterator_recursive	=	recursively;
	new_task->continue_mount_disk	=	true;

	add_fs_task							(new_task, nullptr);
}

void   query_mount_disk (pcstr const						logical_path, 
						 pcstr const						disk_dir_path, 
						 bool								watch_folder,
						 query_mount_callback const			callback, 
						 memory::base_allocator * const		allocator,
						 bool const							recursively,
						 fs_task * const					parent_composite_task)
{
	fs_task * const	new_task			=	XRAY_NEW_IMPL(* allocator, fs_task)(fs_task::type_mount_disk, allocator);

	new_task->logical_path			=	logical_path;
	new_task->disk_dir_path			=	disk_dir_path;
	new_task->watch_folder			=	watch_folder;
	// Bridge: wrap query_mount_callback into std::function<void(bool)> for fs_task::mount_callback
	new_task->mount_callback		=	[callback](bool success) { callback(mount_ptr()); };
	new_task->fs_iterator_recursive	=	recursively;
	
	add_fs_task							(new_task, parent_composite_task);
}

void   query_mount_db (pcstr const						logical_path, 
					   pcstr const						fat_file_path, 
					   pcstr const						db_file_path,
					   bool const 						need_replication,
					   query_mount_callback	const		callback,
					   memory::base_allocator *	const	allocator,
					   bool	const						reverse_byte_order,
					   fs_task * const					parent_composite_task)
{
	fs_task * const	new_task			=	XRAY_NEW_IMPL(* allocator, fs_task)(fs_task::type_mount_db, allocator);

	new_task->logical_path			=	logical_path;
	new_task->fat_file_path			=	fat_file_path;
	new_task->db_file_path			=	db_file_path;
	new_task->need_replication		=	need_replication;
	// Bridge: wrap query_mount_callback into std::function<void(bool)> for fs_task::mount_callback
	new_task->mount_callback		=	[callback](bool success) { callback(mount_ptr()); };
	new_task->reverse_byte_order	=	reverse_byte_order;

	add_fs_task							(new_task, parent_composite_task);
}

void   query_unmount_disk (pcstr const 						logical_path, 
						   pcstr const 						disk_dir_path, 
						   query_mount_callback	const 		callback,
						   memory::base_allocator *	const	allocator,
						   fs_task * const					parent_composite_task)
{
	fs_task * const	new_task		=	XRAY_NEW_IMPL(* allocator, fs_task)(fs_task::type_unmount_disk, allocator);

	new_task->logical_path			=	logical_path;
	new_task->disk_dir_path			=	disk_dir_path;
	// Bridge: wrap query_mount_callback into std::function<void(bool)> for fs_task::mount_callback
	new_task->mount_callback		=	[callback](bool success) { callback(mount_ptr()); };

	add_fs_task							(new_task, parent_composite_task);
}

void   query_unmount_db (pcstr const						logical_path, 
						 pcstr const						fat_file_path, 
						 query_mount_callback const			callback,
						 memory::base_allocator * const		allocator,
						 fs_task * const					parent_composite_task)
{
	fs_task * new_task				=	XRAY_NEW_IMPL(* allocator, fs_task)(fs_task::type_unmount_db, allocator);

	new_task->logical_path			=	logical_path;
	new_task->fat_file_path			=	fat_file_path;
	// Bridge: wrap query_mount_callback into std::function<void(bool)> for fs_task::mount_callback
	new_task->mount_callback		=	[callback](bool success) { callback(mount_ptr()); };

	add_fs_task							(new_task, parent_composite_task);
}

void   query_mount_physical		(fs_new::virtual_path_string const &	virtual_path, 
								 fs_new::native_path_string const &		physical_path, 
								 pcstr									descriptor,
								 fs_new::watcher_enabled_bool const		watcher_enabled,
								 query_mount_callback const				callback, 
								 memory::base_allocator * const			allocator,
								 recursive_bool const					recursive,
								 fs_task_composite * const				composite_task)
{
	fs_task_mount * const new_task	=	XRAY_NEW_IMPL(* allocator, fs_task_mount)
											(virtual_path, physical_path, descriptor, watcher_enabled, callback, recursive, allocator);
	add_fs_task							(new_task, (fs_task*)composite_task);
}

void   query_mount_archive		(fs_new::virtual_path_string const &	virtual_path,
								 fs_new::native_path_string const & 	fat_physical_path,
								 fs_new::native_path_string const & 	archive_physical_path,
								 pcstr									descriptor,
								 query_mount_callback const &			callback,
								 memory::base_allocator * const			allocator,
								 fs_task_composite * const				composite_task)
{
	fs_task_mount * const new_task	=	XRAY_NEW_IMPL(* allocator, fs_task_mount)
											(virtual_path, fat_physical_path, archive_physical_path, descriptor, callback, allocator);
	add_fs_task							(new_task, (fs_task*)composite_task);
}

} // namespace resources
} // namespace xray


