////////////////////////////////////////////////////////////////////////////
//	Created 	: 29.08.2008
//	Author		: Sergey Chechin
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_LOGGING_HELPER_H_INCLUDED
#define XRAY_LOGGING_HELPER_H_INCLUDED

#include <xray/logging/api.h>
#include <xray/logging/format.h>

namespace xray {
namespace logging {
	class settings;
struct XRAY_LOGGING_API helper_data : private core::noncopyable {
	pcstr			m_initiator;
	pcstr			m_file;
	pcstr			m_function_signature;
	int				m_line;
	verbosity		m_verbosity;

					helper_data	(
						pcstr 		file,
						pcstr 		function_signature,
						int   		line,
						pcstr 		major_initiator,
						verbosity	verbosity
					);
}; // struct helper_data

enum	log_flags_enum : int;

bool XRAY_LOGGING_API check_verbosity (pcstr initiator, verbosity verbosity);

class XRAY_LOGGING_API helper : private helper_data {
public:
					helper		(
						pcstr 		file,
						pcstr 		function_signature,
						int   		line,
						pcstr 		major_initiator,
						verbosity	verbosity
					);
	void XRAY_CDECL	operator( )	( log_flags_enum const log_flags, pcstr format, ... );
	void XRAY_CDECL	operator( )	( format_specifier const & log_format, pcstr format, ... );
	void XRAY_CDECL	operator( )	( format_specifier const & log_format, int const log_flags, pcstr format, ... );
	void XRAY_CDECL	operator( )	( log_format *  format_list, int const log_flags, pcstr format, ... );
	void XRAY_CDECL	operator( )	( pcstr format, ... );
	void XRAY_CDECL	operator( )	( settings const & log_settings, pcstr format, ... );
}; // class helper

} // namespace logging
} // namespace xray

#endif // #ifndef XRAY_LOGGING_HELPER_H_INCLUDED
