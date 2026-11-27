////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux floating-point control setup per thread
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <cfenv>
#if defined(__x86_64__)
#	include <xmmintrin.h>
#endif

static xray::command_line::key s_floating_point_control_disabled(
	"floating_point_control_disabled", "", "math", "disables floating point control flags setup for each thread"
);

namespace xray {
namespace math {

void on_thread_spawn()
{
	if ( s_floating_point_control_disabled )
		return;

	std::fesetround( FE_TONEAREST );
#if defined(__x86_64__)
	_MM_SET_ROUNDING_MODE( _MM_ROUND_NEAREST );
	_MM_SET_FLUSH_ZERO_MODE( _MM_FLUSH_ZERO_ON );
#endif
}

} // namespace math
} // namespace xray
