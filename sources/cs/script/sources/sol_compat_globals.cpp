////////////////////////////////////////////////////////////////////////////
//	Module 		: sol_compat_globals.cpp
//	Created 	: 01.05.2026
//	Description : Global variables for script_binding compatibility layer
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <cs/script/sol_compat.h>

namespace script_binding {
	memory_allocation_function_pointer  allocator = nullptr;
	memory_allocation_function_parameter allocator_parameter = nullptr;
} // namespace script_binding
