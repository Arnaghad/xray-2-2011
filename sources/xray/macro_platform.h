////////////////////////////////////////////////////////////////////////////
// Created: 29.08.2008
// Author: Dmitriy Iassenev
// Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_MACRO_PLATFORM_H_INCLUDED
#define XRAY_MACRO_PLATFORM_H_INCLUDED

#if defined(_WIN64) && (defined(__x86_64__) || defined(_M_X64))
#	define XRAY_PLATFORM_WINDOWS	1
#	define XRAY_PLATFORM_WINDOWS_64	1
#	define XRAY_PLATFORM_LINUX		0
#	define XRAY_PLATFORM_POSIX		0
#elif defined(__linux__) && (defined(__x86_64__) || defined(__aarch64__))
#	define XRAY_PLATFORM_WINDOWS	0
#	define XRAY_PLATFORM_WINDOWS_64	0
#	define XRAY_PLATFORM_LINUX		1
#	define XRAY_PLATFORM_POSIX		1
#else
#	error X-Ray requires a 64-bit Windows x64 or Linux x86_64/arm64 target
#endif

#define XRAY_PLATFORM_64_BIT 1
#define XRAY_LITTLE_ENDIAN 1
#define XRAY_BIG_ENDIAN 0

static_assert(sizeof(void*) == 8, "X-Ray requires the x64 pointer ABI");

#endif // XRAY_MACRO_PLATFORM_H_INCLUDED
