////////////////////////////////////////////////////////////////////////////
//	Created		: 06.05.2010
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef RESOURCES_FS_TASK_H_INCLUDED
#define RESOURCES_FS_TASK_H_INCLUDED

#include <xray/resources_fs_iterator.h>
#include <xray/fs_watcher.h>
#include <xray/intrusive_list.h>
#include <xray/fs/virtual_path_string.h>
#include <xray/fs/native_path_string.h>
#include <xray/resources_query_result.h>
#include <xray/fs_sub_fat_resource.h>

namespace xray {
namespace resources {

class fs_task
{
public:
	enum	type_enum				{	type_undefined, 
										type_fs_iterator,
										type_fs_iterator_task,
										type_mount_operations_start,
											type_mount_physical, 
											type_mount_archive, 
											type_mount_disk,
											type_mount_db,
											type_unmount_disk,
											type_unmount_db,
											type_mount_disk_node,
											type_unmount_disk_node,
											type_update_file_size_in_fat,
											type_update_disk_node_name,
											type_unmount,
											type_erase_file,
											type_mount_composite, // list of mount/unmount operations
										type_mount_operations_end,
										// take care to insert all mount operations between guards
									};

public:
					fs_task								(type_enum type);
					fs_task								(type_enum type,  
														 memory::base_allocator * allocator,
														 query_result_for_cook * parent_query = nullptr);
	virtual			~fs_task							() {}

	virtual bool	execute_may_destroy_this			();
	virtual void	call_user_callback					();

	u32				thread_id							() const { return m_thread_id; }
	type_enum		type								() const { return m_type; }
	virtual bool	is_helper_query_for_mount			() const { return false; }
	bool			is_mount_task						() const;
	bool			is_sub_request						() const { return m_parent_query != nullptr; }
	memory::base_allocator *	allocator				() const { return m_allocator; }
	void						set_allocator			(memory::base_allocator * a) { m_allocator = a; }

	void								add_child(fs_task* child) {}
	void								on_fs_iterator_ready();

	struct children_type {
		void lock() {}
		void unlock() {}
	} children;

	fs_new::virtual_path_string			fs_iterator_path;
	fs_new::native_path_string			fat_file_path;
	fs_new::native_path_string			physical_path;
	fs_new::native_path_string			old_physical_path;
	fs_new::native_path_string			new_physical_path;
	fs_new::virtual_path_string			logical_path;
	fs_new::native_path_string			disk_dir_path;
	fs_new::native_path_string			db_file_path;

	bool								need_replication;
	bool								watch_folder;
	bool								reverse_byte_order;
	std::function<void(bool)>			mount_callback;
	std::function<void(vfs::vfs_locked_iterator const&)> fs_iterator_callback;
	bool								continue_mount_disk;
	bool								fs_iterator_recursive;
	bool								fs_iterator_ready;
	fs_task *							parent;
	fs::find_results_struct				m_find_results;
	bool								call_from_get_path_info;
	
	// Use dummy padding or forward declared pointer if the struct isn't fully defined.
	// But fs_iterator_path is already there, meaning we can just include the types.
	fs::file_change_info				file_change_info;

protected:
	void			on_task_ready_may_destroy_this		();

protected:
	fs_task *							m_next;
	bool								m_result;

private:
	type_enum							m_type;
	memory::base_allocator *			m_allocator;
	u32									m_thread_id;
	query_result_for_cook *				m_parent_query;

	friend class						resources_manager;
	friend class						thread_local_data;
	friend class						fs_task_composite;
};

} // namespace resources
} // namespace xray

#endif // #ifndef RESOURCES_FS_TASK_H_INCLUDED
