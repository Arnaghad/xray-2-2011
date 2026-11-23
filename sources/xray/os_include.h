////////////////////////////////////////////////////////////////////////////
//	Created 	: 25.09.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_OS_INCLUDE_H_INCLUDED
#define XRAY_OS_INCLUDE_H_INCLUDED

#include <xray/os_preinclude.h>


#	pragma warning ( push )
#	pragma warning ( disable : 4005 )		// because of the DELETE macro
#	include			<windows.h>
#	pragma warning ( pop )

#	undef DELETE
#	undef max
#	undef min


#endif // #ifndef XRAY_OS_INCLUDE_H_INCLUDED
