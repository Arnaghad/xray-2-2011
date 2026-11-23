////////////////////////////////////////////////////////////////////////////
//	Created 	: 25.09.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_THREADING_MUTEX_H_INCLUDED
#define XRAY_THREADING_MUTEX_H_INCLUDED

#include <xray/noncopyable.h>
#include <xray/threading_policies.h>

namespace xray {
namespace threading {

template < typename MutexType >
class mutex_raii_impl : private xray::noncopyable {
public:
	inline				mutex_raii_impl	( MutexType const& lock ) : m_lock( lock )	{ m_lock.lock(); m_locked = true; }
	inline	void		clear			( )											{ if ( m_locked ) { m_lock.unlock(); m_locked = false; } }
	inline				~mutex_raii_impl( )											{ clear(); }
private:
	MutexType const&	m_lock;
	bool				m_locked;
}; // class mutex_raii

class XRAY_CORE_API mutex_tasks_unaware : private xray::noncopyable {
public:
				mutex_tasks_unaware	( );
				~mutex_tasks_unaware( );
				void	lock		( ) const;
				void	unlock		( ) const;
				bool	try_lock	( ) const;
public:
	typedef mutex_raii_impl< mutex_tasks_unaware >	mutex_raii;

private:
	mutable u64			m_mutex[5]; // sizeof(CRITICAL_SECTION);
}; // class mutex_tasks_unaware

class XRAY_CORE_API mutex : private xray::noncopyable {
public:
	inline				mutex		( ) { }
				void	lock		( ) const;
				void	unlock		( ) const;
				bool	try_lock	( ) const;

public:
	typedef mutex_raii_impl< mutex >	mutex_raii;

private:
	mutex_tasks_unaware	m_mutex;
};

typedef	mutex_tasks_unaware::mutex_raii	mutex_tasks_unaware_raii;
typedef	mutex::mutex_raii				mutex_raii;

} // namespace threading
} // namespace xray

#endif // #ifndef XRAY_THREADING_MUTEX_H_INCLUDED
