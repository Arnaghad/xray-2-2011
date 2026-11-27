////////////////////////////////////////////////////////////////////////////
//	Module 		: fs.cpp
//	Created 	: 02.09.2006
//  Modified 	: 02.09.2006
//	Author		: Dmitriy Iassenev
//	Description : file system
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <cs/core/fs.h>
#include "fs_readers_file.h"
#include "fs_writers_file.h"

#if defined(_WIN32)
#	include <io.h>
#	include <share.h>
#else
#	include <unistd.h>
#endif

#include <fcntl.h>
#include <sys/stat.h>

fs::reader fs::file_reader	(pcstr path)
{
#if defined(_WIN32)
	int	file_handle;
	errno_t	error_code	= 
		_sopen_s(
			&file_handle,
			path,
			_O_RDONLY | _O_BINARY,
			_SH_DENYNO, 
            _S_IREAD | _S_IWRITE
		);

	if (error_code) {
		threading::yield( 1 );
		error_code		= 
			_sopen_s(
				&file_handle,
				path,
				_O_RDONLY | _O_BINARY | _O_SEQUENTIAL,
				_SH_DENYNO, 
				_S_IREAD | _S_IWRITE
			);
	}

	if (error_code)
		return			0;

	return				cs_new<readers::file>(file_handle,path);
#else
	int const fd		= ::open( path, O_RDONLY );
	if ( fd < 0 )
		return			0;
	return				cs_new<readers::file>(fd, path);
#endif
}

fs::writer fs::file_writer	(pcstr path)
{
	return				cs_new<writers::file>(path);
}
