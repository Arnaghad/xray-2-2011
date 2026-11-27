////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Platform-neutral native HDD filesystem alias & factory
////////////////////////////////////////////////////////////////////////////

#ifndef NATIVE_HDD_FILE_SYSTEM_H_INCLUDED
#define NATIVE_HDD_FILE_SYSTEM_H_INCLUDED

#include <xray/macro_platform.h>
#include <xray/fs/api.h>

#if XRAY_PLATFORM_WINDOWS
#	include <xray/fs/windows_hdd_file_system.h>
#else
#	include <xray/fs/posix_hdd_file_system.h>
#endif

namespace xray {
namespace fs_new {

#if XRAY_PLATFORM_WINDOWS
typedef windows_hdd_file_system	native_hdd_file_system;
#else
typedef posix_hdd_file_system	native_hdd_file_system;
#endif

XRAY_FS_API device_file_system_interface*	create_native_hdd_file_system();
XRAY_FS_API void							destroy_native_hdd_file_system( device_file_system_interface* device );

} // namespace fs_new
} // namespace xray

#endif // #ifndef NATIVE_HDD_FILE_SYSTEM_H_INCLUDED
