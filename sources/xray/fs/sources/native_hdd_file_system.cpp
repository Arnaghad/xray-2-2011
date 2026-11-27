////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Native HDD file system factory
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/fs/native_hdd_file_system.h>

namespace xray {
namespace fs_new {

device_file_system_interface* create_native_hdd_file_system()
{
	return new native_hdd_file_system();
}

void destroy_native_hdd_file_system( device_file_system_interface* const device )
{
	delete device;
}

} // namespace fs_new
} // namespace xray
