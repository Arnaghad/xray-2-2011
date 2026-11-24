////////////////////////////////////////////////////////////////////////////
//	Created		: 07.12.2009
//	Author		: Alexander Maniluk
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "io_service.h"

namespace xray {
namespace network {
namespace lowlevel {

io_service::io_service() :
	m_asio_work(asio::make_work_guard(m_asio_service))
{
}

io_service::~io_service()
{
	m_asio_work.reset();
	m_asio_service.stop();
}

void io_service::tick()
{
	m_asio_service.poll();
	m_dispatch_host.invoke_ready_callbacks();
}

} // namespace lowlevel
} // namespace network
} // namespace xray
