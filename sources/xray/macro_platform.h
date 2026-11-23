////////////////////////////////////////////////////////////////////////////
// Created: 29.08.2008
// Author: Dmitriy Iassenev
// Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_MACRO_PLATFORM_H_INCLUDED
#define XRAY_MACRO_PLATFORM_H_INCLUDED

#if !defined(_WIN64) || !(defined(__x86_64__) || defined(_M_X64))
# error X-Ray currently requires Windows x64
#endif

#define XRAY_PLATFORM_WINDOWS 1
#define XRAY_PLATFORM_WINDOWS_64 1
#define XRAY_PLATFORM_64_BIT 1
#define XRAY_LITTLE_ENDIAN 1
#define XRAY_BIG_ENDIAN 0

static_assert(sizeof(void*) == 8, "X-Ray requires the x64 pointer ABI");

#endif // XRAY_MACRO_PLATFORM_H_INCLUDED
