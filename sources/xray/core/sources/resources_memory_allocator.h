#ifndef MANAGED_MEMORY_ALLOCATOR
#define MANAGED_MEMORY_ALLOCATOR

#include "resources_helper.h"

#include <xray/intrusive_ptr.h>
#include <xray/vfs/iterator.h>
#include <xray/debug/macros.h>

namespace xray		{
namespace core		{
namespace resources {

class managed_memory_allocator;
class managed_node;

// Legacy resource type for the core managed memory allocator.
// This is NOT xray::resources::managed_resource — it is a simple local type
// that provides the interface required by the legacy managed_memory_allocator.
class resource
{
public:
	resource	(u32 data_size, u32 offs_to_data)
		: m_data_size(data_size)
		, m_offs_to_data(offs_to_data)
		, m_node(nullptr)
	{}

	void	get_full_path	(fs_new::virtual_path_string& path) const;
	u32		get_raw_size	() const { return m_data_size; }
	u32		get_size		() const { return m_data_size; }
	u32		get_buffer_size	() const { return m_data_size + m_offs_to_data; }

	managed_node*	m_node;

private:
	u32		m_data_size;
	u32		m_offs_to_data;
};

class managed_node
{
public:
	enum				type_t			{ is_allocated, is_free, is_kept };

private:
	vfs::vfs_iterator	m_fat_it;
	char				m_type;
	u16					m_pin_count;
	managed_node*		m_next;
	managed_node*		m_prev;
	u32					m_size;
	resource*			m_res;
	union
	{
		managed_node*	m_next_free;
		managed_node*	m_prev_kept;
	};

public:
	void				init			(type_t type, u32 size);
	managed_node*		find_prev_free	() const;
	size_t				get_size		() const { return m_size; }

	friend class		resource;
	friend class		managed_memory_allocator;
};

class managed_memory_allocator
{
public:
					managed_memory_allocator	(u32 size);
					~managed_memory_allocator	();

	resource*			allocate 			(u32		buffer_size,
											 u32		data_size, // data_size <= buffer_size
											 vfs::vfs_iterator*	fat_it,
											 u32		offs_to_data = 0);

	void				deallocate			(resource* ptr);
	u32					get_free_size		() const { return m_free_size; }

	void				log_nodes			() const;

	void				test_defragment		();

	void				check_consistency	();

private:
	managed_node*		unlink_kept_node	(vfs::vfs_iterator fat_it);
	managed_node*		allocate_node		(managed_node* start_free_node, u32 size);
	managed_node*		deallocate_impl		(managed_node* ptr);
	managed_node*		find_prev_free_node (managed_node* free_node);
	void				defragment			();
	bool				is_first_node		(managed_node* node) const;
	managed_node*		get_first_node		() const;
	u32					get_real_node_size	(managed_node* node) const;

	static u32 const	s_max_ignored_remainder	=	16;

private:
	managed_node*		m_first_kept;
	managed_node*		m_last_kept;
	managed_node*		m_first_free;

	u32					m_free_size;
	pbyte				m_arena;
	u32					m_arena_size;

	pbyte				m_temp_arena;
	u32					m_temp_arena_size;
};

fixed_string<512>		get_resource_log_string	(resource* resource);

} // namespace resources
} // namespace core
} // namespace xray

#endif // MANAGED_MEMORY_ALLOCATOR
