////////////////////////////////////////////////////////////////////////////
//	Created		: 26.01.2010
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef FS_PLATFORM_CONFIGURATION_H_INCLUDED
#define FS_PLATFORM_CONFIGURATION_H_INCLUDED

#include <xray/platform_pointer.h>
#include <functional>

namespace xray {

typedef		u64			file_size_type;

namespace fs {

template <platform_pointer_enum pointer_for_fat_size>
struct platform_to_file_size_type
{
	typedef		u32					file_size_type;
};

template <>
struct platform_to_file_size_type<platform_pointer_64bit>
{
	typedef		u64					file_size_type;
};

// forward declarations:
template <platform_pointer_enum pointer_for_fat_size = platform_pointer_default>
class fat_node;

class file_system;

typedef std::function<bool (pcstr physical_path, pcstr logical_path)>		mount_filter;

} // namespace fs
} // namespace xray

#endif // #ifndef FS_PLATFORM_CONFIGURATION_H_INCLUDED
