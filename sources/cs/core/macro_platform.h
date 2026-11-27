////////////////////////////////////////////////////////////////////////////
// Created: 14.09.2009
// Author: Dmitriy Iassenev
// Copyright (C) Challenge Solutions(tm) - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef CS_CORE_MACRO_PLATFORM_H_INCLUDED
#define CS_CORE_MACRO_PLATFORM_H_INCLUDED

#if defined(_WIN64) && (defined(__x86_64__) || defined(_M_X64))
#	define CS_PLATFORM_WINDOWS		1
#	define CS_PLATFORM_WINDOWS_64	1
#	define CS_PLATFORM_LINUX		0
#	define CS_PLATFORM_POSIX		0
#	define CS_CDECL					__cdecl
#elif defined(__linux__) && (defined(__x86_64__) || defined(__aarch64__))
#	define CS_PLATFORM_WINDOWS		0
#	define CS_PLATFORM_WINDOWS_64	0
#	define CS_PLATFORM_LINUX		1
#	define CS_PLATFORM_POSIX		1
#	define CS_CDECL
#else
#	error CS currently requires Windows x64 or Linux x86_64/arm64
#endif

#define CS_LITTLE_ENDIAN 1
#define CS_BIG_ENDIAN 0

#if defined(_MSC_VER)
# define CS_DECLARE_PURE_VIRTUAL_DESTRUCTOR(type) virtual ~type() = 0 { }
#elif defined(__GNUC__)
# define CS_DECLARE_PURE_VIRTUAL_DESTRUCTOR(type) inline virtual ~type() { }
#else
# error Unsupported C++ compiler
#endif

static_assert(sizeof(void*) == 8, "CS requires the x64 pointer ABI");

#endif // CS_CORE_MACRO_PLATFORM_H_INCLUDED
