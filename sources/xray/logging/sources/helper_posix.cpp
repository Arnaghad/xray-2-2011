////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux logging timestamp helper
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <chrono>
#include <ctime>

namespace xray {
namespace logging {

void fill_local_time( xray::buffer_string& dest )
{
	auto const now = std::chrono::system_clock::now();
	auto const ms = std::chrono::duration_cast<std::chrono::milliseconds>( now.time_since_epoch() ) % 1000;
	std::time_t const t = std::chrono::system_clock::to_time_t( now );
	std::tm local_tm = {};
	::localtime_r( &t, &local_tm );

	dest.appendf(
		"%02d:%02d:%02d:%03d",
		local_tm.tm_hour,
		local_tm.tm_min,
		local_tm.tm_sec,
		static_cast<int>( ms.count() )
	);
}

} // namespace logging
} // namespace xray
