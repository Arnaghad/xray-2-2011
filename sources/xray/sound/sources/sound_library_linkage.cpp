////////////////////////////////////////////////////////////////////////////
//	Created 	: 22.12.2009
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/core/library_linkage.h>

#include <xray/linkage_helper.h>

XRAY_DECLARE_LINKAGE_ID(sound_library_linkage)

#if defined(_MSC_VER) && defined(XRAY_AUDIO_BACKEND_XAUDIO2)
#	pragma comment( lib, "x3daudio.lib" )
#	pragma comment( lib, "xapofx.lib" )
#	pragma comment( lib, "xapobase.lib" )
#endif // #if defined(_MSC_VER) && defined(XRAY_AUDIO_BACKEND_XAUDIO2)

