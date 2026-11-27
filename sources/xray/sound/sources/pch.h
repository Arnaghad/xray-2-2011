////////////////////////////////////////////////////////////////////////////
//	Created 	: 22.12.2009
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef PCH_H_INCLUDED
#define PCH_H_INCLUDED

#ifndef XRAY_SOUND_BUILDING
#define XRAY_SOUND_BUILDING
#endif // #ifndef XRAY_SOUND_BUILDING

#ifndef XRAY_STATIC_LIBRARIES
#	ifndef XRAY_ENGINE_BUILDING
#		define XRAY_ENGINE_BUILDING
#	endif
#endif // #ifndef XRAY_STATIC_LIBRARIES

#define XRAY_LOG_MODULE_INITIATOR	"sound"
#include <xray/extensions.h>

#include <xray/os_platform.h>
#if defined(XRAY_AUDIO_BACKEND_XAUDIO2)
#	include <xray/os_preinclude.h>
#	undef NOUSER
#	undef NOMSG
#	include <xray/os_include.h>

#	include <xaudio2.h>
#	include <x3daudio.h>
#	include <xapo.h>
// #	include <xapobase.h>
#	include <xapofx.h>
#endif

#include <vorbis/vorbisfile.h>
#include <vorbis/vorbisenc.h>

#include "sound_memory.h"

#endif // #ifndef PCH_H_INCLUDED
