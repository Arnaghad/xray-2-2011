////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux stdstream console logging backend
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <cerrno>
#include <cstdio>
#include <stdarg.h>
#include <unistd.h>
#include <xray/logging/api.h>
#include <xray/logging/extensions.h>

namespace xray {
namespace logging {

void write_to_stdstream( stdstream_enum stream, pcstr format, ... )
{
	int fd = -1;
	if ( stream == stdstream_out )
		fd = STDOUT_FILENO;
	else if ( stream == stdstream_error )
		fd = STDERR_FILENO;
	else
		return;

	va_list mark;
	va_start( mark, format );
	xray::fixed_string4096 message;
	message.appendf_va_list( format, mark );
	va_end( mark );

	char const * ptr = message.c_str();
	size_t remaining = message.length();
	while ( remaining > 0 ) {
		ssize_t const written = ::write( fd, ptr, remaining );
		if ( written < 0 ) {
			if ( errno == EINTR )
				continue;
			break;
		}
		ptr += written;
		remaining -= static_cast<size_t>( written );
	}
}

void initialize_stdstreams_if_needed()
{
}

bool initialize_console()
{
	return true;
}

void finalize_console()
{
	std::fflush( stdout );
	std::fflush( stderr );
}

} // namespace logging
} // namespace xray
