////////////////////////////////////////////////////////////////////////////
//	Created		: 23.11.2026
//	Description	: Normalized cross-thread input event queue implementation
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/input_event_queue.h>

#include <cstddef>
#include <deque>
#include <mutex>

namespace xray {
namespace input {

namespace {

constexpr std::size_t kMaxQueuedInputEvents = 4096;

std::mutex& queue_mutex()
{
	static std::mutex m;
	return m;
}

std::deque<normalized_input_event>& queue_storage()
{
	static std::deque<normalized_input_event> q;
	return q;
}

} // namespace

void push_normalized_input_event( normalized_input_event const& ev )
{
	std::lock_guard<std::mutex> lock( queue_mutex() );
	auto& q = queue_storage();
	if ( q.size() >= kMaxQueuedInputEvents )
		q.pop_front();
	q.push_back( ev );
}

bool pop_normalized_input_event( normalized_input_event& out_ev )
{
	std::lock_guard<std::mutex> lock( queue_mutex() );
	auto& q = queue_storage();
	if ( q.empty() )
		return false;
	out_ev = q.front();
	q.pop_front();
	return true;
}

void clear_normalized_input_events()
{
	std::lock_guard<std::mutex> lock( queue_mutex() );
	queue_storage().clear();
}

} // namespace input
} // namespace xray
