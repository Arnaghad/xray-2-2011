////////////////////////////////////////////////////////////////////////////
//	Created		: 23.11.2026
//	Description	: Lightweight RAII wrappers for Win32 HANDLE and COM pointers
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_OS_RAII_WIN_H_INCLUDED
#define XRAY_OS_RAII_WIN_H_INCLUDED

#include <xray/os_include.h>
#include <cstddef>
#include <cstdint>
#include <type_traits>
#include <utility>

namespace xray {
namespace os {

class unique_handle
{
public:
	explicit unique_handle( HANDLE handle = nullptr ) noexcept
		: m_handle( handle )
	{
	}

	~unique_handle() noexcept
	{
		reset();
	}

	unique_handle( unique_handle const& )				= delete;
	unique_handle& operator=( unique_handle const& )	= delete;

	unique_handle( unique_handle&& other ) noexcept
		: m_handle( other.release() )
	{
	}

	unique_handle& operator=( unique_handle&& other ) noexcept
	{
		if ( this != &other )
			reset( other.release() );
		return *this;
	}

	bool valid() const noexcept
	{
		HANDLE const invalid_handle = reinterpret_cast<HANDLE>( static_cast<std::intptr_t>( -1 ) );
		return m_handle != nullptr && m_handle != invalid_handle;
	}

	explicit operator bool() const noexcept
	{
		return valid();
	}

	HANDLE get() const noexcept
	{
		return m_handle;
	}

	HANDLE release() noexcept
	{
		HANDLE const result = m_handle;
		m_handle = nullptr;
		return result;
	}

	void reset( HANDLE handle = nullptr ) noexcept
	{
		if ( m_handle != handle ) {
			if ( valid() )
				CloseHandle( m_handle );
			m_handle = handle;
		}
	}

private:
	HANDLE m_handle;
};

template <typename T>
class com_ptr
{
public:
	com_ptr() noexcept = default;
	com_ptr( std::nullptr_t ) noexcept {}

	explicit com_ptr( T* ptr, bool add_ref = false ) noexcept
		: m_ptr( ptr )
	{
		if ( m_ptr && add_ref )
			m_ptr->AddRef();
	}

	com_ptr( com_ptr const& other ) noexcept
		: m_ptr( other.m_ptr )
	{
		if ( m_ptr )
			m_ptr->AddRef();
	}

	com_ptr( com_ptr&& other ) noexcept
		: m_ptr( other.detach() )
	{
	}

	~com_ptr() noexcept
	{
		reset();
	}

	com_ptr& operator=( std::nullptr_t ) noexcept
	{
		reset();
		return *this;
	}

	com_ptr& operator=( com_ptr const& other ) noexcept
	{
		if ( this != &other ) {
			T* const next = other.m_ptr;
			if ( next )
				next->AddRef();
			T* const prev = m_ptr;
			m_ptr = next;
			if ( prev )
				prev->Release();
		}
		return *this;
	}

	com_ptr& operator=( com_ptr&& other ) noexcept
	{
		if ( this != &other )
			attach( other.detach() );
		return *this;
	}

	T* get() const noexcept				{ return m_ptr; }
	T* operator->() const noexcept		{ return m_ptr; }
	T& operator*() const noexcept		{ return *m_ptr; }
	explicit operator bool() const noexcept { return m_ptr != nullptr; }

	T** put() noexcept
	{
		reset();
		return &m_ptr;
	}

	void attach( T* ptr ) noexcept
	{
		if ( m_ptr != ptr ) {
			T* const prev = m_ptr;
			m_ptr = ptr;
			if ( prev )
				prev->Release();
		}
	}

	T* detach() noexcept
	{
		T* const result = m_ptr;
		m_ptr = nullptr;
		return result;
	}

	void reset() noexcept
	{
		T* const prev = m_ptr;
		m_ptr = nullptr;
		if ( prev )
			prev->Release();
	}

private:
	T* m_ptr = nullptr;
};

template <typename Fn>
class scoped_cleanup
{
public:
	explicit scoped_cleanup( Fn fn ) noexcept
		: m_fn( std::move(fn) )
	{
	}

	~scoped_cleanup() noexcept
	{
		if ( m_active )
			m_fn();
	}

	scoped_cleanup( scoped_cleanup const& )				= delete;
	scoped_cleanup& operator=( scoped_cleanup const& )	= delete;
	scoped_cleanup( scoped_cleanup&& )					= delete;
	scoped_cleanup& operator=( scoped_cleanup&& )		= delete;

	void dismiss() noexcept { m_active = false; }

private:
	Fn		m_fn;
	bool	m_active = true;
};

template <typename Fn>
[[nodiscard]] inline scoped_cleanup<std::decay_t<Fn>> make_scoped_cleanup( Fn&& fn ) noexcept
{
	return scoped_cleanup<std::decay_t<Fn>>( std::forward<Fn>(fn) );
}

} // namespace os
} // namespace xray

#endif // #ifndef XRAY_OS_RAII_WIN_H_INCLUDED
