////////////////////////////////////////////////////////////////////////////
//	Created		: 01.04.2010
//	Author		: Alexander Maniluk
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef ASIO_TYPES_MULTI_H_INCLUDED
#define ASIO_TYPES_MULTI_H_INCLUDED

#include "boost_api.h"
#include <asio.hpp>


namespace xray {
namespace network {
namespace lowlevel {

typedef asio::io_context					asio_io_service;
typedef asio::steady_timer				deadline_timer;
typedef asio::executor_work_guard<asio::io_context::executor_type> io_service_work;
typedef std::error_code				error_code;
typedef asio::ip::address_v4				ip_address_v4;
typedef asio::ip::address_v6				ip_address_v6;

typedef unsigned short							port_type;


typedef asio::ip::udp::endpoint			datagram_endpoint;
typedef asio::ip::udp::socket			datagram_socket;
typedef	asio::ip::udp::resolver			datagram_address_resolver;
												
typedef asio::ip::tcp::endpoint			stream_endpoint;
typedef asio::ip::tcp::socket			stream_socket;	
typedef asio::ip::tcp::acceptor			stream_acceptor;
typedef	asio::ip::tcp::resolver			stream_address_resolver;


} // namespace lowlevel
} // namespace network
} // namespace xray

#endif // #ifndef ASIO_TYPES_MULTI_H_INCLUDED
