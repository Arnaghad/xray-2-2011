////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX network initializer (SIGPIPE ignore; sockets need no WSAStartup)
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "platform_initializer.h"
#include <csignal>

namespace xray {
namespace network {
namespace lowlevel {

platform_initializer::platform_initializer()
{
	std::signal( SIGPIPE, SIG_IGN );
}

platform_initializer::~platform_initializer()
{
}

} // namespace lowlevel
} // namespace network
} // namespace xray
