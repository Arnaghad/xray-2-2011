////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Cross-platform lightweight RAII utilities
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_OS_RAII_H_INCLUDED
#define XRAY_OS_RAII_H_INCLUDED

#include <type_traits>
#include <utility>

namespace xray {
namespace os {

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

#endif // #ifndef XRAY_OS_RAII_H_INCLUDED
