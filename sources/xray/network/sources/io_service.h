////////////////////////////////////////////////////////////////////////////
//	Created		: 07.12.2009
//	Author		: Alexander Maniluk
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef IO_SERVICE_H_INCLUDED
#define IO_SERVICE_H_INCLUDED

#include "asio_handler_hook.h"
#include "asio_types_multi.h"
#include "async_handler_host.h"

namespace xray {
namespace network {
namespace lowlevel {

typedef async_handler_host<std_allocator, xray::threading::mutex> async_host;

class io_service :
	private xray::noncopyable
{
public:
				io_service		();
				~io_service		();

	void		tick			();

	asio_io_service&	get_io_context		() { return m_asio_service; }
	async_host&			get_dispatch_host	() { return m_dispatch_host; }

private:
	asio_io_service						m_asio_service;
	async_host							m_dispatch_host;
	io_service_work						m_asio_work;
};// class io_service

} // namespace lowlevel
} // namespace network
} // namespace xray

#include "io_service_inline.h"

#endif // #ifndef IO_SERVICE_H_INCLUDED
