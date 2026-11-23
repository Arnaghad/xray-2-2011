#ifndef XRAY_RENDER_VULKAN_LIBRARY_LINKAGE_H_INCLUDED
#define XRAY_RENDER_VULKAN_LIBRARY_LINKAGE_H_INCLUDED

#include <xray/macro_library_name.h>

#if defined(XRAY_RENDER_SHARED)
#   if defined(XRAY_RENDER_VULKAN_BUILDING)
#       define XRAY_RENDER_VULKAN_API __declspec(dllexport)
#   else
#       define XRAY_RENDER_VULKAN_API __declspec(dllimport)
#   endif
#elif defined(XRAY_RENDER_VULKAN_BUILDING)
#	define XRAY_RENDER_VULKAN_API
#else // #ifdef XRAY_RENDER_VULKAN_BUILDING
#	define XRAY_RENDER_VULKAN_API
#endif // #ifdef XRAY_RENDER_VULKAN_BUILDING

#endif // #ifndef XRAY_RENDER_VULKAN_LIBRARY_LINKAGE_H_INCLUDED
