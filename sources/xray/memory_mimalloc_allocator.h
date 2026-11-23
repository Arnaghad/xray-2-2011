////////////////////////////////////////////////////////////////////////////
//	Created 	: 10.05.2026
//	Author		: 
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_MEMORY_MIMALLOC_ALLOCATOR_H_INCLUDED
#define XRAY_MEMORY_MIMALLOC_ALLOCATOR_H_INCLUDED

#include <xray/memory_base_allocator.h>
#include <xray/memory_debug_parameters.h>

namespace xray {
namespace memory {

class XRAY_CORE_API mimalloc_allocator : public base_allocator {
public:
					mimalloc_allocator	(
#ifndef MASTER_GOLD
						bool const use_leak_detector = true
#endif // #ifndef MASTER_GOLD
					);
	template <typename T1>
	inline mimalloc_allocator( T1 const& ) : mimalloc_allocator() { }
	template <typename T1, typename T2>
	inline mimalloc_allocator( T1 const&, T2 const& ) : mimalloc_allocator() { }
	template <typename T1, typename T2, typename T3>
	inline mimalloc_allocator( T1 const&, T2 const&, T3 const& ) : mimalloc_allocator() { }
	template <typename T1, typename T2, typename T3, typename T4>
	inline mimalloc_allocator( T1 const&, T2 const&, T3 const&, T4 const& ) : mimalloc_allocator() { }
	inline	void	user_thread_id		( u32 ) const { }
	inline	void	user_current_thread_id	( ) const { }
			pvoid	malloc_impl			( size_t size XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_DECLARATION XRAY_CORE_DEBUG_PARAMETERS_DECLARATION );
			pvoid	realloc_impl		( pvoid pointer, size_t new_size XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_DECLARATION XRAY_CORE_DEBUG_PARAMETERS_DECLARATION );
			void	free_impl			( pvoid pointer XRAY_CORE_DEBUG_PARAMETERS_DECLARATION );

	virtual	size_t	total_size			( ) const;
	virtual	size_t	allocated_size		( ) const;

protected:
	virtual	pvoid	call_malloc			( size_t size XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_DECLARATION XRAY_CORE_DEBUG_PARAMETERS_DECLARATION );
	virtual	pvoid	call_realloc		( pvoid pointer, size_t new_size XRAY_CORE_DEBUG_PARAMETERS_DESCRIPTION_DECLARATION XRAY_CORE_DEBUG_PARAMETERS_DECLARATION );
	virtual	void	call_free			( pvoid pointer XRAY_CORE_DEBUG_PARAMETERS_DECLARATION );
	virtual	size_t	usable_size_impl	( pvoid pointer ) const;
	virtual	void	initialize_impl		( pvoid arena, u64 arena_size, pcstr arena_id );
	virtual	void	finalize_impl		( );

private:
	inline	pvoid	on_malloc			( pvoid buffer, size_t, size_t, pcstr ) const { return buffer; }
	inline	void	on_free				( pvoid&, bool = true ) const { }
	inline	size_t	needed_size			( size_t const size ) const { return size; }
}; // class mimalloc_allocator

} // namespace memory
} // namespace xray

#endif // #ifndef XRAY_MEMORY_MIMALLOC_ALLOCATOR_H_INCLUDED
