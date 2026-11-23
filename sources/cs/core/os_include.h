////////////////////////////////////////////////////////////////////////////
//	Module 		: os_inclue.h
//	Created 	: 24.08.2006
//  Modified 	: 24.08.2006
//	Author		: Dmitriy Iassenev
//	Description : operating system include
////////////////////////////////////////////////////////////////////////////

#ifndef OS_INCLUDE_H_INCLUDED
#define OS_INCLUDE_H_INCLUDED

#undef			INVALID_HANDLE_VALUE
#undef			HMODULE

#undef			HANDLE
#undef			DLL_PROCESS_DETACH
#undef			DLL_PROCESS_ATTACH
#undef			APIENTRY
#undef			BOOL

#define			VC_EXTRALEAN				
#define			WIN32_LEAN_AND_MEAN			
#define			STRICT						
#define			IDIRECTPLAY2_OR_GREATER

#ifndef _WIN32_WINNT
#   define _WIN32_WINNT 0x0500
#endif

#define			NOGDICAPMASKS
//#define			NOSYSMETRICS
#define			NOMENUS
#define			NOICONS
#define			NOKEYSTATES
//#define			NODRAWTEXT
#define			NOMEMMGR
#define			NOMETAFILE
#define			NOSERVICE
#define			NOCOMM
#define			NOHELP
#define			NOPROFILER
#define			NOMCX

#ifndef NOMINMAX
#   define NOMINMAX
#endif

//#define			WINVER					0x0500 

#	include		<windows.h>
#	define		IS_DEBUGGER_PRESENT			IsDebuggerPresent
#	define		OUTPUT_DEBUG_STRING			OutputDebugString

#endif // #ifndef OS_INCLUDE_H_INCLUDED
