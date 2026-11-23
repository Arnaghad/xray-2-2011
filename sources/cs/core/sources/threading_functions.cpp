////////////////////////////////////////////////////////////////////////////
//	Module 		: threading_functions.cpp
//	Created 	: 26.08.2006
//  Modified 	: 26.08.2006
//	Author		: Dmitriy Iassenev
//	Description : threading functions
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <cs/core/threading_functions.h>
#include <cs/core/os_include.h>				// Sleep, SwitchToThread

#	include <process.h>						// _beginthread

namespace debug {
	void	on_thread_spawn	();
} // namespace debug

namespace threading {

struct thread_entry_params {
	pcstr					name;
	pvoid					arguments;
	thread_function_type*	function_to_call;
};

void thread_entry				(void* argument)
{
	thread_entry_params		params = *(thread_entry_params const* )argument;
	u8* temp_ptr = (u8*)argument;
	cs_delete(temp_ptr);

	debugger_name			(params.name);
	debug::on_thread_spawn	();
	params.function_to_call	(params.arguments);
}

} // namespace threading

void threading::spawn			(
	thread_function_type* function_to_call,
	pcstr name,
	u32 const stack_size,
	pvoid arguments
)
{
	memory.switch_mt_alloc		();

	thread_entry_params* argument = cs_new<thread_entry_params>();
	argument->name				= name;
	argument->arguments			= arguments;
	argument->function_to_call	= function_to_call;

	_beginthread				(&thread_entry,stack_size,argument);
}

void threading::yield			(u32 const milliseconds)
{
	if (milliseconds) {
		Sleep	(milliseconds);
		return;
	}
	
	if (SwitchToThread())
		return;

	Sleep		(0);
}

void threading::debugger_name	(pcstr debugger_name)
{
#if CS_DEBUG_LIBRARIES
#	pragma pack(push,8)
	struct thread_profile {
		u32		type;
		pcstr	name;
		u32		thread_id;
		u32		flags;
	};
#	pragma pack(pop)

	thread_profile		profile;
	profile.type		= 4096;
	profile.name		= debugger_name;
	profile.thread_id	= u32(-1);
	profile.flags		= 0;
#ifdef _MSC_VER
	__try {
		RaiseException( MS_VC_EXCEPTION, 0, sizeof(info)/sizeof(ULONG_PTR), (ULONG_PTR*)&info );
	}
	__except(EXCEPTION_CONTINUE_EXECUTION) {
	}
#endif
#else // #if CS_DEBUG_LIBRARIES
	CS_UNREFERENCED_PARAMETER	( debugger_name );
#endif // #if CS_DEBUG_LIBRARIES
}
