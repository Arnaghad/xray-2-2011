////////////////////////////////////////////////////////////////////////////
//	Created 	: 07.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_THREADING_FUNCTIONS_H_INCLUDED
#define XRAY_THREADING_FUNCTIONS_H_INCLUDED

#include <xray/macro_extensions.h>


#if defined(__GNUC__)
// Removed xtls.h
#endif // #if defined(__GNUC__)

#include <functional>

//#include <intrin.h>

namespace xray { namespace command_line { class key; } }

namespace xray {
namespace threading {

	typedef u32								tls_key_id_type;
	typedef long							atomic32_value_type;
	typedef atomic32_value_type volatile	atomic32_type;
	typedef s64								atomic64_value_type;
	typedef atomic64_value_type volatile	atomic64_type;
	typedef pvoid							atomic_ptr_value_type;
	typedef atomic_ptr_value_type volatile	atomic_ptr_type;

	template <typename T>
	struct atomic_ptr {
		typedef T* volatile type;
	};

	typedef u32								thread_id_type;

XRAY_CORE_API	tls_key_id_type	tls_create_key				( );
XRAY_CORE_API	void			tls_delete_key				( tls_key_id_type key );
XRAY_CORE_API	void			tls_set_value				( tls_key_id_type key, pvoid value );
XRAY_CORE_API	pvoid			tls_get_value				( tls_key_id_type key );
XRAY_CORE_API	bool			tls_is_valid_key			( tls_key_id_type key );
XRAY_CORE_API	tls_key_id_type	tls_get_invalid_key			( );

XRAY_CORE_API	u32				current_thread_id			( );
XRAY_CORE_API	pcstr			current_thread_logging_name	( );
XRAY_CORE_API	void			yield						( u32 ms = 0 );
XRAY_CORE_API	void			set_thread_name				( pcstr name );
XRAY_CORE_API	void			set_thread_name				( pcstr name_for_debugger, pcstr log_string );
XRAY_CORE_API	u32				core_count					( );
XRAY_CORE_API	void			set_current_thread_affinity	( u32 hardware_thread );
XRAY_CORE_API	u32				current_thread_affinity		( );

} // namespace threading
namespace command_line { class key; }
namespace threading {
extern XRAY_CORE_API command_line::key g_debug_single_thread;

enum tasks_awareness {
	tasks_unaware,
	tasks_aware,
};

typedef std::function< void ( ) >	thread_function_type;

XRAY_CORE_API	thread_id_type	spawn						(
	thread_function_type const& function_to_call,
	pcstr thread_name_for_debugger,
	pcstr thread_name_for_logging,
	u32 stack_size,
	u32 hardware_thread,
	tasks_awareness tasks_awareness = tasks_aware,
	pvoid * out_handle = nullptr
);

} // namespace threading
} // namespace xray

#	define XRAY_THREADING_INLINE inline
#	include <xray/threading_functions_win_inline.h>

// CPU+compiler reorder preventing macro:
#	define XRAY_MEMORY_BARRIER_EXCEPT_READS_BEFORE_WRITES	MemoryBarrier
#	define XRAY_MEMORY_BARRIER_FULL							MemoryBarrier

#endif // #ifndef XRAY_THREADING_FUNCTIONS_H_INCLUDED
