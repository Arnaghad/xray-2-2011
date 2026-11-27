////////////////////////////////////////////////////////////////////////////
//	Created 	: 25.09.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_OS_EXTENSIONS_H_INCLUDED
#define XRAY_OS_EXTENSIONS_H_INCLUDED

#include <xray/macro_platform.h>
#include <xray/type_extensions.h>

#	define LPVOID					pvoid
#	define HANDLE					pvoid
#undef INVALID_HANDLE_VALUE
#define INVALID_HANDLE_VALUE ((HANDLE)(0xFFFFFFFFFFFFFFFFULL))

#	define BOOL						int
#	define FALSE					0
#	define TRUE						1

#	define DLL_PROCESS_DETACH		0
#	define DLL_PROCESS_ATTACH		1

#undef APIENTRY
#if XRAY_PLATFORM_WINDOWS
#	define APIENTRY __stdcall
#else
#	define APIENTRY
#endif

#	define HMODULE					pvoid

	struct HWND__;
#	define	HWND					HWND__*


#include <xray/os_functions.h>

#endif // #ifndef XRAY_OS_EXTENSIONS_H_INCLUDED
