////////////////////////////////////////////////////////////////////////////
//	Module 		: fs_writers_file.cpp
//	Created 	: 02.09.2006
//  Modified 	: 02.09.2006
//	Author		: Dmitriy Iassenev
//	Description : file system writer file class
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "fs_writers_file.h"

#if defined(_WIN32)
#	include <io.h>
#	include <share.h>
#	include <direct.h>
#else
#	include <unistd.h>
#endif

#include <fcntl.h>
#include <sys/stat.h>

fs::writers::file::file		(pcstr path)
{
	m_path				= path;

	size_t const length = sz_len(path);
	pstr const temp		= (pstr)ALLOCA((length + 1)*sizeof(char));
	pcstr i				= path;
	pstr j				= temp;
	for ( ; *i; ++i, ++j) {
		*j	= *i;

		if ( (*i != '\\') && (*i != '/') )
			continue;

		if ( i == path )
			continue;

		*j				= 0;
#if defined(_WIN32)
		_mkdir(temp);
		*j				= '\\';
#else
		::mkdir(temp, 0755);
		*j				= '/';
#endif
	}

#if defined(_WIN32)
	_unlink				(path);

	errno_t	error_code = 
		_sopen_s(
			&m_file_handle,
			path,
			_O_CREAT | _O_WRONLY | _O_BINARY,
			_SH_DENYWR, 
            _S_IREAD | _S_IWRITE
		);

	if (error_code) {
		threading::yield(1);
		error_code = 
			_sopen_s(
				&m_file_handle,
				path,
				_O_CREAT | _O_WRONLY | _O_BINARY,
				_SH_DENYWR, 
				_S_IREAD | _S_IWRITE
			);
	}

	R_ASSERT	(!error_code,"Cannot open file \"%s\" for writing",path);
#else
	::unlink			(path);
	m_file_handle		= ::open( path, O_CREAT | O_WRONLY | O_TRUNC, 0664 );
	R_ASSERT			( m_file_handle >= 0, "Cannot open file \"%s\" for writing", path );
#endif
}

fs::writers::file::~file	()
{
#if defined(_WIN32)
	R_ASSERT	(!_close(m_file_handle),"Cannot close file \"%s\" after writing",*m_path);
#else
	R_ASSERT	(!::close(m_file_handle),"Cannot close file \"%s\" after writing",*m_path);
#endif
}

void fs::writers::file::w	(pcvoid buffer, u32 const buffer_size)
{
#if defined(_WIN32)
	R_ASSERT	(_write(m_file_handle,buffer,buffer_size) == (int)buffer_size,"Cannot write to file \"%s\"",*m_path);
#else
	R_ASSERT	(::write(m_file_handle,buffer,buffer_size) == (ssize_t)buffer_size,"Cannot write to file \"%s\"",*m_path);
#endif
}
