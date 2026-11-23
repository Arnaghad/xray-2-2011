////////////////////////////////////////////////////////////////////////////
//	Created		: 06.05.2011
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef VFS_FIND_RESULTS_STRUCT_H_INCLUDED
#define VFS_FIND_RESULTS_STRUCT_H_INCLUDED

#include <xray/vfs/locked_iterator.h>

namespace xray {
namespace vfs {

typedef	std::function< void (vfs_locked_iterator const &, result_enum) >	find_callback;

enum	find_enum					{	find_recursively	=	1 << 0,
										find_file_only		=	1 << 1, };

} // namespace vfs
} // namespace xray

template class XRAY_VFS_API std::function	< void (xray::vfs::vfs_locked_iterator const &, xray::vfs::result_enum) >;

#endif // #ifndef VFS_FIND_RESULTS_STRUCT_H_INCLUDED
