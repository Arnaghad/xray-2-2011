////////////////////////////////////////////////////////////////////////////
//	Created		: 29.05.2009
//	Author		: Andrew Kolomiets
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef MANAGED_DELEGATE_H_INCLUDED
#define MANAGED_DELEGATE_H_INCLUDED

// Cross-platform callback wrappers.
// Replaces former C++/CLI implementation that used:
//   delegate, gcroot<D^>, D^ managed delegate handles
// Now uses std::function for type-safe native callbacks.

#include <functional>

namespace xray {
namespace vfs { struct vfs_notification; }
} // namespace xray

// Native callback type definitions (replacing CLI delegates)
typedef std::function<void(xray::resources::queries_result&)>                rq_callback_t;
typedef std::function<void(xray::resources::unmanaged_resource_ptr)>         gr_callback_t;
typedef std::function<void(xray::vfs::vfs_locked_iterator const&)>          fs_callback_t;
typedef std::function<void(xray::vfs::vfs_notification const&)>             fw_callback_t;

// Query result wrapper — invokes callback and self-destructs.
// Replaces the CLI template 'qr<T,D>' that used gcroot<D^>.
template <class T>
struct qr
{
	typedef std::function<void(T)> callback_fn;

	qr(callback_fn fn, xray::memory::base_allocator* a)
		: m_callback(std::move(fn)), m_allocator(a), m_rejected(false) {}

	void callback(T data)
	{
		if(!m_rejected)
		{
			m_callback(data);
		}
		else
		{
			LOG_WARNING("rejected query - skipping");
		}
		XRAY_DELETE_IMPL( m_allocator, this);
	}

	xray::memory::base_allocator*	m_allocator;
	callback_fn						m_callback;
	bool							m_rejected;
};

// Non-self-destructing variant. Caller must delete manually.
// Replaces the CLI template 'qr2<T,D>' that used gcroot<D^>.
template <class T>
struct qr2
{
	typedef std::function<void(T)> callback_fn;

	qr2(callback_fn fn) : m_callback(std::move(fn)) {}

	void callback(T data)
	{
		m_callback(data);
	}

	callback_fn m_callback;
};

typedef qr<xray::resources::queries_result&>                query_result_delegate;
typedef qr<xray::vfs::vfs_locked_iterator const&>          fs_iterator_delegate;
typedef qr2<xray::vfs::vfs_notification const&>             file_watcher_delegate;

#endif // #ifndef MANAGED_DELEGATE_H_INCLUDED
