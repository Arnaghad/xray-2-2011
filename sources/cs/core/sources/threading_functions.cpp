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

#if defined(_WIN32)
#	include <process.h>						// _beginthread
#else
#	include <algorithm>
#	include <cstring>
#	include <errno.h>
#	include <pthread.h>
#	include <sched.h>
#	include <time.h>
#endif

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

#if defined(_WIN32)
	_beginthread				(&thread_entry,stack_size,argument);
#else
	pthread_attr_t attr;
	pthread_attr_init(&attr);
	if (stack_size > 0)
		pthread_attr_setstacksize(&attr, stack_size);
	pthread_attr_setdetachstate(&attr, PTHREAD_CREATE_DETACHED);
	pthread_t tid = {};
	auto start_routine = [](void* arg) -> void* {
		thread_entry(arg);
		return nullptr;
	};
	pthread_create(&tid, &attr, +start_routine, argument);
	pthread_attr_destroy(&attr);
#endif
}

void threading::yield			(u32 const milliseconds)
{
#if defined(_WIN32)
	if (milliseconds) {
		Sleep	(milliseconds);
		return;
	}
	
	if (SwitchToThread())
		return;

	Sleep		(0);
#else
	if (milliseconds) {
		struct timespec req = {};
		req.tv_sec = static_cast<time_t>(milliseconds / 1000u);
		req.tv_nsec = static_cast<long>((milliseconds % 1000u) * 1000000UL);
		while (::nanosleep(&req, &req) == -1 && errno == EINTR) {}
		return;
	}
	::sched_yield();
#endif
}

void threading::debugger_name	(pcstr debugger_name)
{
#if CS_DEBUG_LIBRARIES && defined(_WIN32)
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
#elif CS_DEBUG_LIBRARIES && !defined(_WIN32)
	if (debugger_name && *debugger_name) {
		char truncated[16] = {};
		std::size_t const len = std::min<std::size_t>(std::strlen(debugger_name), 15);
		std::memcpy(truncated, debugger_name, len);
		truncated[len] = '\0';
		pthread_setname_np(pthread_self(), truncated);
	}
#else // #if CS_DEBUG_LIBRARIES
	CS_UNREFERENCED_PARAMETER	( debugger_name );
#endif // #if CS_DEBUG_LIBRARIES
}
