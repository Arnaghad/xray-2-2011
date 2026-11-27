/////////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Linux inotify + poll/eventfd implementation of fs_new::folder_watcher
/////////////////////////////////////////////////////////////////////////////////

#include "pch.h"

#if XRAY_FS_NEW_WATCHER_ENABLED && XRAY_PLATFORM_LINUX

#include <xray/fs/folder_watcher.h>
#include <xray/fs/watcher_utils.h>

#include <atomic>
#include <cerrno>
#include <cstring>
#include <filesystem>
#include <mutex>
#include <poll.h>
#include <string>
#include <sys/eventfd.h>
#include <sys/inotify.h>
#include <thread>
#include <unistd.h>
#include <unordered_map>
#include <vector>

namespace xray {
namespace fs_new {

namespace {

constexpr uint32_t k_watch_mask =
	IN_CREATE | IN_DELETE | IN_MODIFY | IN_CLOSE_WRITE | IN_MOVED_FROM | IN_MOVED_TO | IN_DELETE_SELF | IN_MOVE_SELF;

struct queued_notification
{
	fs_notification::type_enum	type = fs_notification::type_unset;
	std::string					path;
	std::string					old_path;
	std::string					new_path;
};

std::string join_subpath( std::string const & base, std::string const & leaf )
{
	if ( base.empty() )
		return leaf;
	if ( leaf.empty() )
		return base;
	if ( base.back() == '/' )
		return base + leaf;
	return base + "/" + leaf;
}

} // namespace

struct folder_watcher::posix_impl
{
	int									inotify_fd = -1;
	int									stop_eventfd = -1;
	std::mutex							mutex;
	std::unordered_map<int, std::string> wd_to_rel;
	std::unordered_map<u32, std::string> pending_moves;
	std::vector<queued_notification>	queue;
	std::thread							poll_thread;
	std::atomic<bool>					stopping{ false };

	void add_watch_tree( std::string const & root_path )
	{
		if ( inotify_fd < 0 )
			return;

		std::error_code ec;
		auto const root = std::filesystem::path( root_path );
		if ( !std::filesystem::is_directory( root, ec ) )
			return;

		int const root_wd = ::inotify_add_watch( inotify_fd, root.c_str(), k_watch_mask );
		if ( root_wd >= 0 )
			wd_to_rel[root_wd] = "";

		std::filesystem::recursive_directory_iterator it(
			root,
			std::filesystem::directory_options::skip_permission_denied,
			ec
		);
		std::filesystem::recursive_directory_iterator end;
		for ( ; it != end; it.increment( ec ) ) {
			if ( ec )
				continue;
			if ( !it->is_directory( ec ) )
				continue;

			auto const rel = it->path().lexically_relative( root ).generic_string();
			if ( rel.find( ".svn" ) != std::string::npos ) {
				it.disable_recursion_pending();
				continue;
			}
			int const wd = ::inotify_add_watch( inotify_fd, it->path().c_str(), k_watch_mask );
			if ( wd >= 0 )
				wd_to_rel[wd] = rel;
		}
	}

	void drain_inotify_locked( std::string const & root_path )
	{
		if ( inotify_fd < 0 )
			return;

		alignas( struct inotify_event ) char buf[64 * 1024];
		for ( ;; ) {
			ssize_t const n = ::read( inotify_fd, buf, sizeof( buf ) );
			if ( n < 0 ) {
				if ( errno == EINTR )
					continue;
				break;
			}
			if ( n == 0 )
				break;

			ssize_t offset = 0;
			while ( offset < n ) {
				auto const * ev = reinterpret_cast<struct inotify_event const *>( buf + offset );
				offset += static_cast<ssize_t>( sizeof( struct inotify_event ) + ev->len );

				std::string rel_dir;
				if ( auto it = wd_to_rel.find( ev->wd ); it != wd_to_rel.end() )
					rel_dir = it->second;

				std::string const name = ( ev->len > 0 && ev->name[0] != '\0' ) ? ev->name : "";
				std::string const rel_path = join_subpath( rel_dir, name );
				std::string const full_path = join_subpath( root_path, rel_path );

				if ( rel_path.find( ".svn" ) != std::string::npos )
					continue;

				if ( ( ev->mask & IN_CREATE ) && ( ev->mask & IN_ISDIR ) ) {
					int const child_wd = ::inotify_add_watch( inotify_fd, full_path.c_str(), k_watch_mask );
					if ( child_wd >= 0 )
						wd_to_rel[child_wd] = rel_path;
				}
				if ( ev->mask & IN_IGNORED ) {
					wd_to_rel.erase( ev->wd );
					continue;
				}

				if ( ev->mask & IN_MOVED_FROM ) {
					pending_moves[ev->cookie] = full_path;
					continue;
				}
				if ( ev->mask & IN_MOVED_TO ) {
					if ( auto mit = pending_moves.find( ev->cookie ); mit != pending_moves.end() ) {
						queue.push_back( queued_notification{
							fs_notification::type_renamed,
							"",
							mit->second,
							full_path
						} );
						pending_moves.erase( mit );
					} else {
						queue.push_back( queued_notification{
							fs_notification::type_added,
							full_path,
							"",
							""
						} );
					}
					continue;
				}

				if ( ev->mask & IN_CREATE )
					queue.push_back( queued_notification{ fs_notification::type_added, full_path, "", "" } );
				else if ( ev->mask & ( IN_DELETE | IN_DELETE_SELF ) )
					queue.push_back( queued_notification{ fs_notification::type_removed, full_path, "", "" } );
				else if ( ev->mask & ( IN_MODIFY | IN_CLOSE_WRITE ) )
					queue.push_back( queued_notification{ fs_notification::type_modified, full_path, "", "" } );
			}
		}

		for ( auto const & [cookie, old_path] : pending_moves ) {
			XRAY_UNREFERENCED_PARAMETER( cookie );
			queue.push_back( queued_notification{ fs_notification::type_removed, old_path, "", "" } );
		}
		pending_moves.clear();
	}
};

folder_watcher::folder_watcher(
	native_path_string const &			path,
	fs_notification_callback const &	callback,
	threading::event *					receive_changes_event
)
	:	m_next_watcher( nullptr ),
		m_prev_watcher( nullptr ),
		m_path( path ),
		m_receive_changes_event( receive_changes_event ),
		m_callback( callback ),
		m_posix_impl( new posix_impl() )
{
	m_posix_impl->inotify_fd = ::inotify_init1( IN_NONBLOCK | IN_CLOEXEC );
	m_posix_impl->stop_eventfd = ::eventfd( 0, EFD_NONBLOCK | EFD_CLOEXEC );
	R_ASSERT( m_posix_impl->inotify_fd >= 0 );

	{
		std::lock_guard<std::mutex> lock( m_posix_impl->mutex );
		m_posix_impl->add_watch_tree( m_path.c_str() );
	}

	if ( !m_receive_changes_event ) {
		try_receive_changes_sync();
	} else {
		m_posix_impl->poll_thread = std::thread( [this]() {
			struct pollfd fds[2] = {
				{ m_posix_impl->inotify_fd, POLLIN, 0 },
				{ m_posix_impl->stop_eventfd, POLLIN, 0 },
			};
			int const nfds = ( m_posix_impl->stop_eventfd >= 0 ) ? 2 : 1;

			while ( !m_posix_impl->stopping.load( std::memory_order_acquire ) ) {
				int const rc = ::poll( fds, static_cast<nfds_t>( nfds ), -1 );
				if ( rc < 0 ) {
					if ( errno == EINTR )
						continue;
					break;
				}
				if ( nfds == 2 && ( fds[1].revents & POLLIN ) )
					break;

				if ( fds[0].revents & POLLIN ) {
					bool has_events = false;
					{
						std::lock_guard<std::mutex> lock( m_posix_impl->mutex );
						m_posix_impl->drain_inotify_locked( m_path.c_str() );
						has_events = !m_posix_impl->queue.empty();
					}
					if ( has_events )
						m_receive_changes_event->set( true );
				}
			}
		} );
	}
}

folder_watcher::~folder_watcher()
{
	if ( m_posix_impl ) {
		m_posix_impl->stopping.store( true, std::memory_order_release );
		if ( m_posix_impl->stop_eventfd >= 0 ) {
			u64 const wake = 1;
			ssize_t const wr = ::write( m_posix_impl->stop_eventfd, &wake, sizeof( wake ) );
			XRAY_UNREFERENCED_PARAMETER( wr );
		}
		if ( m_posix_impl->poll_thread.joinable() )
			m_posix_impl->poll_thread.join();
		if ( m_posix_impl->stop_eventfd >= 0 )
			::close( m_posix_impl->stop_eventfd );
		if ( m_posix_impl->inotify_fd >= 0 )
			::close( m_posix_impl->inotify_fd );
		delete m_posix_impl;
		m_posix_impl = nullptr;
	}
}

void folder_watcher::try_receive_changes_sync()
{
	R_ASSERT( !m_receive_changes_event );
	std::vector<queued_notification> drained;
	{
		std::lock_guard<std::mutex> lock( m_posix_impl->mutex );
		m_posix_impl->drain_inotify_locked( m_path.c_str() );
		drained.swap( m_posix_impl->queue );
	}

	for ( auto const & item : drained ) {
		native_path_string path = item.path.c_str();
		native_path_string old_path = item.old_path.c_str();
		native_path_string new_path = item.new_path.c_str();
		path.make_lowercase();
		old_path.make_lowercase();
		new_path.make_lowercase();

		fs_notification notification;
		notification.type = item.type;
		notification.path = const_cast<pstr>( path.c_str() );
		notification.old_path = const_cast<pstr>( old_path.c_str() );
		notification.new_path = const_cast<pstr>( new_path.c_str() );
		m_callback( notification );
	}
}

void folder_watcher::try_receive_changes_async()
{
	std::vector<queued_notification> drained;
	{
		std::lock_guard<std::mutex> lock( m_posix_impl->mutex );
		m_posix_impl->drain_inotify_locked( m_path.c_str() );
		drained.swap( m_posix_impl->queue );
	}

	for ( auto const & item : drained ) {
		native_path_string path = item.path.c_str();
		native_path_string old_path = item.old_path.c_str();
		native_path_string new_path = item.new_path.c_str();
		path.make_lowercase();
		old_path.make_lowercase();
		new_path.make_lowercase();

		fs_notification notification;
		notification.type = item.type;
		notification.path = const_cast<pstr>( path.c_str() );
		notification.old_path = const_cast<pstr>( old_path.c_str() );
		notification.new_path = const_cast<pstr>( new_path.c_str() );
		m_callback( notification );
	}
}

} // namespace fs_new
} // namespace xray

#endif // #if XRAY_FS_NEW_WATCHER_ENABLED && XRAY_PLATFORM_LINUX
