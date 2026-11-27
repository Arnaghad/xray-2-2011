////////////////////////////////////////////////////////////////////////////
//	Module 		: internal.h
//	Created 	: 27.04.2008
//  Modified 	: 27.04.2008
//	Author		: Dmitriy Iassenev
//	Description : internal file, shouldn't be accessible from library users
////////////////////////////////////////////////////////////////////////////

#ifndef INTERNAL_H_INCLUDED
#define INTERNAL_H_INCLUDED


#ifdef CS_SINGLE_DLL
#	define CS_API
#endif // #ifdef CS_SINGLE_DLL

#include <cs/config.h>

#ifndef CS_STATIC_LIBRARIES
#	if CS_PLATFORM_WINDOWS
#		define CS_CORE_API	__declspec(dllexport)
#	else
#		define CS_CORE_API	__attribute__((visibility("default")))
#	endif
#endif // #ifdef CS_STATIC_LIBRARIES

#endif // #ifndef INTERNAL_H_INCLUDED
