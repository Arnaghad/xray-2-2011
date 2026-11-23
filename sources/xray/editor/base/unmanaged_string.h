////////////////////////////////////////////////////////////////////////////
//	Created 	: 31.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef UNMANAGED_STRING_H_INCLUDED
#define UNMANAGED_STRING_H_INCLUDED

// Cross-platform string wrapper for converting wide strings to narrow strings.
// Replaces former C++/CLI implementation that used System::String^, pin_ptr, PtrToStringChars.
// Now works with native wchar_t* and char* directly.
// Uses engine memory allocator macros (MALLOC/FREE) to be compatible with
// the engine's memory override system.

#include <cstdlib>
#include <cstring>

namespace xray {
namespace editor_base {

class unmanaged_string {
public:
	// Construct from narrow string (copy)
	explicit unmanaged_string( const char* str )
		: m_string( nullptr )
	{
		if ( str )
		{
			size_t const len = strlen( str ) + 1;
			m_string = static_cast<char*>( MALLOC( (u32)len, "unmanaged string" ) );
			if ( m_string )
				memcpy( m_string, str, len );
		}
	}

	// Construct from wide string (convert to narrow)
	explicit unmanaged_string( const wchar_t* wstr )
		: m_string( nullptr )
	{
		if ( wstr )
		{
			size_t const wlen = wcslen( wstr );
			size_t const buffer_size = ( wlen + 1 ) * 4; // worst case UTF-8
			m_string = static_cast<char*>( MALLOC( (u32)buffer_size, "unmanaged string" ) );
			if ( m_string )
			{
#ifdef _MSC_VER
				size_t converted = 0;
				wcstombs_s( &converted, m_string, buffer_size, wstr, _TRUNCATE );
#else
				wcstombs( m_string, wstr, buffer_size );
#endif
			}
		}
	}

	~unmanaged_string()
	{
		if ( m_string )
			FREE( m_string );
	}

	const char* c_str() const { return m_string ? m_string : ""; }

	// Non-copyable
	unmanaged_string( const unmanaged_string& ) = delete;
	unmanaged_string& operator=( const unmanaged_string& ) = delete;

	// Move support
	unmanaged_string( unmanaged_string&& other ) noexcept
		: m_string( other.m_string )
	{
		other.m_string = nullptr;
	}

	unmanaged_string& operator=( unmanaged_string&& other ) noexcept
	{
		if ( this != &other )
		{
			if ( m_string )
				FREE( m_string );
			m_string = other.m_string;
			other.m_string = nullptr;
		}
		return *this;
	}

private:
	char* m_string;
}; // class unmanaged_string

} // namespace editor_base
} // namespace xray

// Backward compatibility alias at global scope
using xray::editor_base::unmanaged_string;

#endif // #ifndef UNMANAGED_STRING_H_INCLUDED
