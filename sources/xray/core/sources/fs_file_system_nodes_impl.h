////////////////////////////////////////////////////////////////////////////
//	Created		: 06.05.2010
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef FS_FILE_SYSTEM_NODES_IMPL_H_INCLUDED
#define FS_FILE_SYSTEM_NODES_IMPL_H_INCLUDED

#include "fs_file_system.h"
#include <xray/hash_multiset.h>

namespace xray {

namespace fs   {

// Legacy aliases used throughout the fat node implementation
typedef platform_pointer_enum                       pointer_for_fat_size_enum;
static const platform_pointer_enum pointer_for_fat_64bit = platform_pointer_64bit;
static const platform_pointer_enum pointer_for_fat_32bit = platform_pointer_32bit;
static const platform_pointer_enum pointer_for_fat_size_platform_default = platform_pointer_default;

//-----------------------------------------------------------------------------------
// detail::node_hash and detail::node_cmp
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size>
class fat_node;

template <class T, pointer_for_fat_size_enum  pointer_for_fat_size >
struct pointer_for_fat_select_helper
{
	typedef T *		type;
};

template <class T>
struct pointer_for_fat_select_helper<T,pointer_for_fat_64bit>
{
	struct helper
	{
		union
		{
			T *		pointer;
			u64		max_storage;
		};

					helper				() { max_storage = 0; }
					helper				(T * const in_pointer)	{ max_storage = 0; pointer = in_pointer; }
		void		operator =			(T * const in_pointer)	{ pointer = in_pointer; }
		T *			operator ->			()						{ return pointer; }
		T const *	operator ->			() const				{ return pointer; }
		T &			operator *			()						{ return * pointer; }
		T const &	operator *			() const				{ return * pointer; }

					operator T * &		()						{ return pointer; }
					operator T *		() const				{ return pointer; }
	};

	typedef helper	type;
};

template <class T, pointer_for_fat_size_enum pointer_for_fat_size>
struct pointer_for_fat
{
	typedef typename pointer_for_fat_select_helper<T,pointer_for_fat_size>::type	type;
};

//-----------------------------------------------------------------------------------
// fat_node
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size>
class fat_folder_node;

template <pointer_for_fat_size_enum pointer_for_fat_size>
class fat_disk_file_node;

template <pointer_for_fat_size_enum pointer_for_fat_size>
class fat_disk_folder_node;

template <pointer_for_fat_size_enum pointer_for_fat_size>
class fat_db_file_node;

template <pointer_for_fat_size_enum pointer_for_fat_size>
class fat_db_compressed_file_node;

template <pointer_for_fat_size_enum pointer_for_fat_size>
class fat_db_inline_file_node;

template <pointer_for_fat_size_enum pointer_for_fat_size>
class fat_db_inline_compressed_file_node;

template <pointer_for_fat_size_enum pointer_for_fat_size>
class fat_node_intrusive_base
{
public:
	typename pointer_for_fat<fat_node<pointer_for_fat_size>, pointer_for_fat_size>::type	m_next_hash_node;
public:
	fat_node_intrusive_base() : m_next_hash_node(nullptr)		{}
	void								set_next_hash_node (fat_node<pointer_for_fat_size> * node)	{ m_next_hash_node = node; }
	fat_node<pointer_for_fat_size> *	get_next_hash_node () { return (fat_node<pointer_for_fat_size> *) m_next_hash_node; }
};

class fat_disk_node_base
{
public:
			fat_disk_node_base		() : m_node_lock_count(0) {}
	void	set_node_is_locked		(bool lock);
	bool	is_node_locked			() const;
	void	set_is_erased			() { threading::interlocked_or(m_node_lock_count, 0x80000000); }
	bool	is_erased				() const { return (m_node_lock_count & 0x80000000) != 0; }

protected:
 	threading::atomic32_type		m_node_lock_count;
};

#pragma pack(push, 2)

template <pointer_for_fat_size_enum pointer_for_fat_size>
class fat_node : public fat_node_intrusive_base<pointer_for_fat_size>
{
public:
	typedef	typename platform_to_file_size_type<pointer_for_fat_size>::file_size_type		file_size_type;

public:
						fat_node				() : m_next(nullptr), m_prev(nullptr), m_parent(nullptr) {}
					   ~fat_node				() {}

	fat_node *			get_next				()		{ return m_next; }
	fat_node const *	get_next				() const { return const_cast<fat_node *>(this)->get_next(); }
	fat_node *			get_next				(bool skip_erased);
	fat_node *			get_first_child			();
	fat_node const *	get_first_child			() const { return const_cast<fat_node *>(this)->get_first_child(); }
	fat_node *			get_first_child			(bool skip_erased);
	fat_node *			find_referenced_link_node ();
	fat_node const *	find_referenced_link_node () const { return const_cast<fat_node *>(this)->find_referenced_link_node(); }

	void				find_link_target_path	(path_string * out_path) const;
	u32					get_sizeof				() const;

	pcstr				get_name				() const { return m_name; }
	u32					get_num_children		() const;
	u32					get_num_nodes			() const;
	void				get_num_nodes			(u32 & num_folders, u32 & num_files) const;
	u32					get_file_size			() const;
	u32					get_raw_file_size		() const;
	u32					get_compressed_file_size	() const;
	file_size_type		get_file_offs			() const;
	bool				get_hash				(u32 * out_hash) const;
	void				set_hash				(u32 hash);

	void				get_full_path			(path_string & str) const;
	u32					get_max_fat_size		(fat_inline_data const & inline_data) const;

	int					get_flags				() const { return m_flags; }
	bool				has_flags				(u32 flags) const { return ((u32)m_flags & flags) != 0; }

	bool				is_folder				() const { return has_flags(file_system::is_folder); }
	bool				is_link					() const { return has_flags(file_system::is_hard_link | file_system::is_soft_link); }
	bool				is_disk					() const { return has_flags(file_system::is_disk); }
	bool				is_db					() const { return has_flags(file_system::is_db); }
	bool				is_replicated			() const { return has_flags(file_system::is_replicated); }
	bool				is_compressed			() const { return has_flags(file_system::is_compressed); }
	bool				is_inlined				() const { return has_flags(file_system::is_inlined); }

	void								set_associated	(resources::resource_base * resource);
	bool								try_clean_associated_if_zero_reference_resource ();
	resources::resource_base * &		ref_associated_ptr							();
	resources::managed_resource_ptr		get_associated_managed_resource_ptr			();
	resources::unmanaged_resource_ptr	get_associated_unmanaged_resource_ptr		();
	resources::query_result *			get_associated_query_result					();

	bool								is_associated								() const;
	bool								is_associated_with							(resources::resource_base * resource_base) const;

	fat_node *							get_next_overlapped							() const { return m_next_overlapped; }
	fat_disk_node_base *				cast_disk_node_base							() { return is_disk() ? (fat_disk_node_base*)this : nullptr; }

	bool								set_inline_data			(const_buffer const & buffer);
	bool								get_inline_data			(const_buffer * out_buffer) const;

	fat_folder_node<pointer_for_fat_size> *						cast_folder						();
	fat_disk_folder_node<pointer_for_fat_size> *				cast_disk_folder				();
	fat_disk_file_node<pointer_for_fat_size> *					cast_disk_file					();
	fat_db_file_node<pointer_for_fat_size> *					cast_db_file					();
	fat_db_compressed_file_node<pointer_for_fat_size> *			cast_db_compressed_file			();
	fat_db_inline_file_node<pointer_for_fat_size> *				cast_db_inline_file				();
	fat_db_inline_compressed_file_node<pointer_for_fat_size> *	cast_db_inline_compressed_file	();

	fat_folder_node<pointer_for_fat_size> const *				cast_folder			() const { return const_cast<fat_node *>(this)->cast_folder(); }
	fat_disk_folder_node<pointer_for_fat_size> const *			cast_disk_folder	() const { return const_cast<fat_node *>(this)->cast_disk_folder(); }
	fat_disk_file_node<pointer_for_fat_size> const *			cast_disk_file		() const { return const_cast<fat_node *>(this)->cast_disk_file(); }
	fat_db_file_node<pointer_for_fat_size> const *				cast_db_file		() const { return const_cast<fat_node *>(this)->cast_db_file(); }
	fat_db_compressed_file_node<pointer_for_fat_size> const *	cast_db_compressed_file	() const { return const_cast<fat_node *>(this)->cast_db_compressed_file(); }
	fat_db_inline_file_node<pointer_for_fat_size> const *				cast_db_inline_file		() const { return const_cast<fat_node *>(this)->cast_db_inline_file(); }
	fat_db_inline_compressed_file_node<pointer_for_fat_size> const *	cast_db_inline_compressed_file	() const { return const_cast<fat_node *>(this)->cast_db_inline_compressed_file(); }

	bool								operator ==			(fat_node const & n) const;
	bool								same_path			(fat_node const * n) const;

	void								reverse_bytes_for_real_class	();
	void								reverse_bytes		();

	// very special 4-bit counter, suited for interlocked inc/dec
	threading::atomic32_type &			ref_flags_as_32bit	() const;

private:
	resources::resource_base *			get_associated_unsecure		() const;
	u32									get_max_fat_size_impl		(fat_inline_data const & inline_data) const;
	void								get_associated_ptr_helper	(resources::managed_resource_ptr *	result_managed, 
																     resources::unmanaged_resource_ptr *	result_unmanaged,
																     resources::query_result * *			result_query_result);
	void								lock_associated		() const;
	void								unlock_associated	() const;


	typedef	typename pointer_for_fat<fat_node, pointer_for_fat_size>::type	pointer_to_node;

	pointer_to_node						m_next;
	pointer_to_node						m_prev;
	pointer_to_node						m_parent;
public:
	pointer_to_node						m_next_hash_node;
	pointer_to_node						m_next_overlapped;
private:

	// this ordering is very important and 2 bytes after m_flags must be allocated
	// only interlocked operations are allowed on m_flags
	u16									m_flags;

#pragma warning (push)
#pragma warning (disable:4200)
	char								m_name[];
#pragma warning (pop)

	friend								class				file_system_impl;
	
	template <pointer_for_fat_size_enum pointer_for_fat_size1>
	friend								class				fat_folder_node;
	template <pointer_for_fat_size_enum pointer_for_fat_size1>
	friend								class				fat_disk_file_node;
	template <pointer_for_fat_size_enum pointer_for_fat_size1>
	friend								class				fat_db_file_node;
	template <pointer_for_fat_size_enum pointer_for_fat_size1>
	friend								class				fat_node;

	template <pointer_for_fat_size_enum target_pointer_size>
	friend								struct				save_nodes_helper;
}; // class fat_node

#pragma pack(pop)

//-----------------------------------------------------------------------------------
// fat_folder_node
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_folder_node
{
public:
	typedef		fat_node<pointer_for_fat_size>		node_type;

public:
								fat_folder_node			() : m_first_child(nullptr) {}

	void						transfer_children		(fat_folder_node * dest, bool no_duplicate_names);

	node_type *					get_first_child					() { return m_first_child; }
	node_type const *			get_first_child					() const { return m_first_child; }
	node_type *					get_first_child					(bool skip_erased);
	bool						find_child				(node_type const * seeking_child) const;
	node_type *					find_child				(pcstr name);
	node_type const *			find_child				(pcstr name) const 
														{ return const_cast<fat_folder_node *>(this)->find_child(name); }

	void						prepend_child			(node_type * child);
	void						unlink_child			(node_type * child);

	node_type *					cast_node				() { return & m_base; }
	node_type const *			cast_node				() const { return const_cast<fat_folder_node *>(this)->cast_node(); }
	
	fat_disk_folder_node<pointer_for_fat_size> *	
								cast_disk_folder_node	() { return cast_node()->cast_disk_folder(); }

	void						reverse_bytes			();

public:
	typename pointer_for_fat<node_type, pointer_for_fat_size>::type	m_first_child;
	node_type					m_base;
}; // fat_folder_node

//-----------------------------------------------------------------------------------
// fat_disk_node_base
//-----------------------------------------------------------------------------------

// Moved up to before fat_node

//-----------------------------------------------------------------------------------
// fat_disk_folder_node
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_disk_folder_node : public fat_disk_node_base
{
public:
	fat_disk_folder_node	() : m_disk_path(nullptr) {}

	fat_folder_node<pointer_for_fat_size> *		cast_folder_node	()	{ return & m_folder; }
	fat_node<pointer_for_fat_size> *			cast_node			()	{ return & m_folder.m_base; }

public:
	pstr										m_disk_path; 
	fat_folder_node<pointer_for_fat_size>		m_folder;
};

//-----------------------------------------------------------------------------------
// fat_db_file_node_base
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_db_file_node_base
{
public:
	typedef	typename platform_to_file_size_type<pointer_for_fat_size>::file_size_type		file_size_type;
public:
										fat_db_file_node_base() : m_resource_base(nullptr), m_pos(0), m_size(0), m_hash(0) {}

	u32									size			() const { return m_size; }
	file_size_type						offs_in_db		() const { return m_pos; }
	void								reverse_bytes	();
	void								set_hash		(u32 hash) { m_hash = hash; }
	u32									hash			() const { return m_hash; }

public:
	typename pointer_for_fat<resources::resource_base, pointer_for_fat_size>::type	m_resource_base;
	file_size_type										m_pos;
	u32													m_size;
	u32													m_hash;
};

//-----------------------------------------------------------------------------------
// fat_db_soft_link_node
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_db_soft_link_node
{
public:
										fat_db_soft_link_node() : m_relative_path_to_referenced(nullptr) {}
	void								reverse_bytes	();
	void								absolute_path_to_referenced	(path_string * out_path) const;

public:
	typename pointer_for_fat<char, pointer_for_fat_size>::type						m_relative_path_to_referenced;
	fat_node<pointer_for_fat_size>		m_base;
};

//-----------------------------------------------------------------------------------
// fat_db_hard_link_node
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_db_hard_link_node
{
public:									
	fat_db_hard_link_node() : m_reference_node(nullptr) {}
	void								reverse_bytes	();

	fat_node<pointer_for_fat_size> *	reference_node	() const { return (fat_node<pointer_for_fat_size> *)m_reference_node; }

public:
	typename pointer_for_fat<fat_node<pointer_for_fat_size>, pointer_for_fat_size>::type	m_reference_node;
	fat_node<pointer_for_fat_size>		m_base;
};

//-----------------------------------------------------------------------------------
// fat_db_file_node
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_db_file_node : public fat_db_file_node_base<pointer_for_fat_size>
{
public:
	void								reverse_bytes	();
	fat_node<pointer_for_fat_size> *	cast_node		()		{ return & m_base; }

public:
	fat_node<pointer_for_fat_size>		m_base;
}; 

//-----------------------------------------------------------------------------------
// fat_db_compressed_file_node
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_db_compressed_file_node : public fat_db_file_node_base<pointer_for_fat_size>
{
public:
										fat_db_compressed_file_node() :	m_uncompressed_size(0) {}
	void								reverse_bytes	();
	fat_node<pointer_for_fat_size> *	cast_node		()		{ return & m_base; }

public:
	u32									m_uncompressed_size;
	fat_node<pointer_for_fat_size>		m_base;

}; 

//-----------------------------------------------------------------------------------
// fat_inline_base
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_inline_base
{
public:
										fat_inline_base () : m_inlined_data(0), m_inlined_size(0) {}

	void								set_inlined_data(const_buffer const & buffer) 
	{ 
		m_inlined_data				=	(pcstr)buffer.c_ptr(); 
		m_inlined_size				=	(u32)buffer.size(); 
	}
	const_buffer						get_inlined_data() const;
	void								reverse_bytes	();

public:
	typename pointer_for_fat<char const, pointer_for_fat_size>::type	m_inlined_data;
	u32																	m_inlined_size;

}; 

//-----------------------------------------------------------------------------------
// fat_db_inline_file_node
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_db_inline_file_node : public fat_db_file_node_base<pointer_for_fat_size>, 
								public fat_inline_base<pointer_for_fat_size>
{
public:
										fat_db_inline_file_node () {}

	void								reverse_bytes	();
	fat_node<pointer_for_fat_size> *	cast_node		()		{ return & m_base; }

public:
	fat_node<pointer_for_fat_size>		m_base;

}; 

//-----------------------------------------------------------------------------------
// fat_db_inline_compressed_file_node
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_db_inline_compressed_file_node :	public fat_db_file_node_base<pointer_for_fat_size>, 
											public fat_inline_base<pointer_for_fat_size>
{
public:
										fat_db_inline_compressed_file_node() :	m_uncompressed_size(0) {}
	void								reverse_bytes	();
	fat_node<pointer_for_fat_size> *	cast_node		()	{ return & m_base; }

public:
	u32									m_uncompressed_size;
	fat_node<pointer_for_fat_size>		m_base;

}; 

//-----------------------------------------------------------------------------------
// fat_disk_file_node
//-----------------------------------------------------------------------------------

template <pointer_for_fat_size_enum pointer_for_fat_size = pointer_for_fat_size_platform_default>
class fat_disk_file_node : public fat_disk_node_base
{
public:
	fat_disk_file_node   									() : m_resource_base(nullptr), m_disk_path(nullptr), m_size(0) {}

	pcstr								disk_path			() const	{ return m_disk_path; }
	u32									size				() const	{ return (u32)m_size; }
	fat_node<pointer_for_fat_size> *	cast_node 			()			{ return & m_base; }

public:
	resources::resource_base *			m_resource_base;
	pstr								m_disk_path;	
	threading::atomic32_type			m_size;
	fat_node<pointer_for_fat_size>		m_base;
}; 

namespace detail {
inline int		bits_to_flags_byte				() { return	platform::big_endian() ? 16 : 0; }
} // namespace detail

class fat_node_lock : public fat_disk_node_base
{
public:
	void				set_node_is_locked				(bool lock);
};

template <pointer_for_fat_size_enum pointer_for_fat_size>
fat_node<pointer_for_fat_size> *   fat_node<pointer_for_fat_size>::find_referenced_link_node ()
{
	return									nullptr;
}

template <pointer_for_fat_size_enum pointer_for_fat_size>
inline bool   is_erased_node (fat_node<pointer_for_fat_size> * node)
{	
	XRAY_UNREFERENCED_PARAMETER				(node);
	return									false;
}

template <pointer_for_fat_size_enum pointer_for_fat_size>
typename fat_folder_node<pointer_for_fat_size>::node_type *   fat_folder_node<pointer_for_fat_size>::get_first_child (bool skip_erased)
{
	if ( !skip_erased )
		return						m_first_child; 
	
	node_type * out_node		=	m_first_child;
	while ( out_node && is_erased_node(out_node) )
		out_node				=	out_node->m_next;
	
	return							out_node; 
}

template <pointer_for_fat_size_enum pointer_for_fat_size>
inline fat_node<pointer_for_fat_size> *   fat_node<pointer_for_fat_size>::get_first_child ()
{
	if ( is_folder() ) 
		return	cast_folder()->get_first_child(); 

	return						nullptr;
}

template <pointer_for_fat_size_enum pointer_for_fat_size>
fat_node<pointer_for_fat_size> *   fat_node<pointer_for_fat_size>::get_first_child (bool skip_erased)
{
	if ( fat_node<pointer_for_fat_size> * const referenced_node = find_referenced_link_node() ) 
		return						referenced_node->get_first_child(skip_erased);
	
	if ( is_folder() ) 
		return	cast_folder()->get_first_child(skip_erased); 
	
	return							nullptr;
}

template <pointer_for_fat_size_enum pointer_for_fat_size>
fat_node<pointer_for_fat_size> *   fat_node<pointer_for_fat_size>::get_next (bool skip_erased)       
{ 
	if ( !skip_erased )
		return						m_next;
	
	fat_node<pointer_for_fat_size> *   out_node	=	m_next;
	while ( out_node && is_erased_node(out_node) )
		out_node				=	out_node->m_next;
	
	return							out_node; 
}

template <pointer_for_fat_size_enum pointer_for_fat_size>
void   fat_node<pointer_for_fat_size>::find_link_target_path (path_string * out_path) const
{
	XRAY_UNREFERENCED_PARAMETER				(out_path);
	NOT_IMPLEMENTED();
}

template <pointer_for_fat_size_enum pointer_for_fat_size>
u32   fat_node<pointer_for_fat_size>::get_sizeof () const
{
	if ( is_folder() )
	{
		if ( is_disk() )
			return							sizeof(fat_disk_folder_node<pointer_for_fat_size>);
		return								sizeof(fat_folder_node<pointer_for_fat_size>);
	}
	if ( is_compressed() )
		return								sizeof(fat_db_compressed_file_node<pointer_for_fat_size>);
	
	return									sizeof(fat_db_file_node<pointer_for_fat_size>);
}

} // namespace fs 
} // namespace xray 

#endif // FS_FILE_SYSTEM_NODES_IMPL_H_INCLUDED

