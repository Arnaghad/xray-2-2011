////////////////////////////////////////////////////////////////////////////
//	Created		: 26.05.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef CONVERTING_H_INCLUDED
#define CONVERTING_H_INCLUDED

// Cross-platform string conversion utilities (pure C++)
// Replaces former C++/CLI implementations that used pin_ptr, gcnew, safe_cast, System::String^

#include <cstdlib>
#include <cstring>
#include <cstdio>

namespace xray {
namespace editor_base {

// Convert float to string (native)
inline void convert_float_to_string( char* buffer, size_t buffer_size, float value )
{
	snprintf( buffer, buffer_size, "%.8g", value );
}

// Convert string to float (native)
inline float convert_string_to_single( const char* str )
{
	return (float)atof( str );
}

// Convert wide string to float (native)
inline float convert_wstring_to_single( const wchar_t* wstr )
{
	return (float)wcstod( wstr, nullptr );
}

} // namespace editor_base
} // namespace xray

#endif // #ifndef CONVERTING_H_INCLUDED
