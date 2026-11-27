////////////////////////////////////////////////////////////////////////////
//	Module 		: macro_extensions.h
//	Created 	: 27.08.2006
//  Modified 	: 06.12.2007
//	Author		: Dmitriy Iassenev
//	Description : macro extensions
////////////////////////////////////////////////////////////////////////////

#ifndef CS_CORE_MACRO_EXTENSIONS_H_INCLUDED
#define CS_CORE_MACRO_EXTENSIONS_H_INCLUDED

#if defined( _CPPUNWIND ) && !defined( CS_DOT_NET )
#	error please disable exceptions
#endif // #if defined( _CPPUNWIND ) && !defined( CS_DOT_NET )

#include <cs/core/macro_platform.h>
#if CS_PLATFORM_WINDOWS
#	include <malloc.h>
#	define ALLOCA	_alloca
#else
#	include <alloca.h>
#	define ALLOCA	alloca
#endif

////////////////////////////////////////////////////////////////////////////
// CS_UNREFERENCED_PARAMETER
////////////////////////////////////////////////////////////////////////////
#ifdef CS_UNREFERENCED_PARAMETER
#	error do not define CS_UNREFERENCED_PARAMETER macro
#endif

#define CS_UNREFERENCED_PARAMETER(parameter) (void)(parameter)

////////////////////////////////////////////////////////////////////////////
// CS_BUILDING
////////////////////////////////////////////////////////////////////////////
#ifdef CS_BUILDING
#	error do not define CS_BUILDING macro
#endif

#define CS_BUILDING

#endif // #ifndef CS_CORE_MACRO_EXTENSIONS_H_INCLUDED
