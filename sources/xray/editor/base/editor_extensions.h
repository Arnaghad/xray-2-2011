////////////////////////////////////////////////////////////////////////////
//	Created		: 26.05.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef EDITOR_EXTENSIONS_H_INCLUDED
#define EDITOR_EXTENSIONS_H_INCLUDED

// Cross-platform editor extensions header.
// Removed all C++/CLI constructs:
//   - #pragma managed(push,off/pop) 
//   - #ifdef __cplusplus_cli guards
//   - #include <vcclr.h>
//   - System::* identity() overloads
//   - using Microsoft::Win32::RegistryKey
//   - using WeifenLuo::WinFormsUI::Docking::IDockContent
// These are now handled by the C# editor layer.

#include <xray/extensions.h>

#include <xray/os_preinclude.h>
#undef	NOGDICAPMASKS     			// CC_*, LC_*, PC_*, CP_*, TC_*, RC_
#undef	NOVIRTUALKEYCODES 			// VK_*
#undef	NOWINMESSAGES     			// WM_*, EM_*, LB_*, CB_*
#undef	NOWINSTYLES       			// WS_*, CS_*, ES_*, LBS_*, SBS_*, CBS_*
#undef	NOSYSMETRICS      			// SM_*
#undef	NOMENUS           			// MF_*
#undef	NOICONS           			// IDI_*
#undef	NOKEYSTATES       			// MK_*
#undef	NOSYSCOMMANDS     			// SC_*
//#undef	NORASTEROPS       			// Binary and Tertiary raster ops
#undef	NOSHOWWINDOW      			// SW_*
//#undef	OEMRESOURCE       			// OEM Resource values
#undef	NOATOM            			// Atom Manager routines
#undef	NOCLIPBOARD       			// Clipboard routines
#undef	NOCOLOR           			// Screen colors
#undef	NOCTLMGR          			// Control and Dialog routines
#undef	NODRAWTEXT        			// DrawText() and DT_*
#undef	NOGDI             			// All GDI defines and routines
#undef	NOKERNEL          			// All KERNEL defines and routines
#undef	NOUSER            			// All USER defines and routines
#undef	NONLS             			// All NLS defines and routines
#undef	NOMB              			// MB_* and MessageBox()
#undef	NOMEMMGR          			// GMEM_*, LMEM_*, GHND, LHND, associated routines
#undef	NOMETAFILE        			// typedef METAFILEPICT
//#undef	NOMINMAX          			// Macros min(a,b) and max(a,b)
#undef	NOMSG             			// typedef MSG and associated routines
//#undef	NOOPENFILE        			// OpenFile(), OemToAnsi, AnsiToOem, and OF_*
#undef	NOSCROLL          			// SB_* and scrolling routines
#undef	NOSERVICE         			// All Service Controller routines, SERVICE_ equates, etc.
#undef	NOSOUND           			// Sound driver routines
#undef	NOTEXTMETRIC      			// typedef TEXTMETRIC and associated routines
#undef	NOWH              			// SetWindowsHook and WH_*
#undef	NOWINOFFSETS      			// GWL_*, GCL_*, associated routines
//#undef	NOCOMM            			// COMM driver routines
//#undef	NOKANJI           			// Kanji support stuff.
//#undef	NOHELP            			// Help engine interface.
//#undef	NOPROFILER        			// Profiler interface.
//#undef	NODEFERWINDOWPOS  			// DeferWindowPos routines
//#undef	NOMCX             			// Modem Configuration Extensions
#include <xray/os_include.h>

#include <cstdio>
#include <cstdlib>

#include <xray/resources_fs.h>
#include <xray/fs_watcher.h>
#include <xray/configs.h>

#include <xray/editor/base/managed_delegate.h>

using xray::math::float4x4;
using xray::math::float4;
using xray::math::float3;
using xray::math::float2;
using xray::math::color;
using xray::math::aabb;

#include "converting.h"

#endif // #ifndef EDITOR_EXTENSIONS_H_INCLUDED
