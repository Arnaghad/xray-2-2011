////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux path conversion helpers
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/fs_path.h>

namespace xray {
namespace fs {

path_string convert_to_native( pcstr path )
{
	path_string res = path;
	for ( path_string::iterator it = res.begin(); it != res.end(); ++it ) {
		if ( *it == '\\' )
			*it = '/';
	}
	return res;
}

} // namespace fs
} // namespace xray
