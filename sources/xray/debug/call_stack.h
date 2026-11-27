////////////////////////////////////////////////////////////////////////////
//	Created 	: 15.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef DEBUG_CALL_STACK_H_INCLUDED
#define DEBUG_CALL_STACK_H_INCLUDED

#include <functional>
#include <xray/type_extensions.h>

struct _EXCEPTION_POINTERS;

namespace xray {
namespace debug {
namespace call_stack {

typedef std::function<
			bool (
				u32,	// call stack id ( 0 means the calling function )
				u32,	// num_call_stack_lines
				pcstr,	// module
				pcstr,	// file
				int,	// line number
				pcstr,	// function,
				size_t	// address
			)
		>		Callback;

void	iterate	( _EXCEPTION_POINTERS*	pointers,  // can be nullptr
				  void*					call_stack[], // can be nullptr
				  Callback const&		callback,
				  bool					invert_order = false);

void	get_stack_trace (void*	stacktrace[], 
						 u32	stacktrace_max, 
						 u32	num_to_capture, 
						 u32*	out_hash);

void	finalize_symbols( );

} // namespace call_stack
} // namespace debug
} // namespace xray

#endif // #ifndef DEBUG_CALL_STACK_H_INCLUDED
