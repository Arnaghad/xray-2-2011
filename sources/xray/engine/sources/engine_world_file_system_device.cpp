////////////////////////////////////////////////////////////////////////////
//	Created		: 08.02.2011
//	Author		: Dmitriy Iassenev & Sergey Chechin
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "engine_world.h"
#include "apc.h"
#include <xray/core_entry_point.h>
#include <xray/debug/debug.h>
#include <iostream>
#include <syncstream>

namespace xray { 
namespace engine {

void engine_world::initialize_file_system_devices	( )
{
	// Both interfaces must exist before initialize_resources receives them. Use
	// dedicated device queues in the ordinary engine, including the prepared level.
	if (threading::g_debug_single_thread.is_set()) {
		XRAY_CONSTRUCT_REFERENCE(m_hdd_async_interface,fs_new::asynchronous_device_interface)(&core::get_core_synchronous_device());
		XRAY_CONSTRUCT_REFERENCE(m_dvd_async_interface,fs_new::asynchronous_device_interface)(&core::get_core_synchronous_device());
	} else {
		XRAY_CONSTRUCT_REFERENCE(m_hdd_async_interface,fs_new::asynchronous_device_interface)(core::get_core_device_file_system(),fs_new::watcher_enabled_true);
		XRAY_CONSTRUCT_REFERENCE(m_dvd_async_interface,fs_new::asynchronous_device_interface)(core::get_core_device_file_system(),fs_new::watcher_enabled_false);
	}
	initialize_file_system_device(*m_hdd_async_interface,apc::hdd,"hdd");
	initialize_file_system_device(*m_dvd_async_interface,apc::dvd,"dvd");
}

class device_ticker {
public:
	inline device_ticker	( fs_new::asynchronous_device_interface& device ) :
		m_device		( &device )
	{
	}

	inline void operator ( )( ) const
	{
		m_device->tick	( true );
	}

private:
	fs_new::asynchronous_device_interface * m_device;
}; // struct device_ticker

template < typename TickFunctionType >
void engine_world::thread_function	( apc::threads_enum const apc_thread_id,  TickFunctionType const& functor )
{
	apc::assign_thread_id			( apc_thread_id, threading::current_thread_id( ) );
	if (background_render_capture()) std::osyncstream(std::cerr)<<"M8 file device: "<<unsigned(apc_thread_id)<<" thread="<<threading::current_thread_id()<<'\n';
	apc::process					( apc_thread_id );

	for (;;) {
		if ( m_file_system_devices_destruction_started )
			break;

		functor						( );
	}
	
	apc::process					( apc_thread_id );
}

void engine_world::initialize_file_system_device	(
		fs_new::asynchronous_device_interface& device,
		apc::threads_enum const apc_thread_id,
		pcstr const debug_thread_id
	)
{
	if ( threading::g_debug_single_thread.is_set() )
		return;

	apc::assign_thread_id			( apc_thread_id, u32(-1) );

	device_ticker ticker(device);
	threading::spawn				(
		[this, apc_thread_id, ticker]() { this->thread_function(apc_thread_id, ticker); },
		debug_thread_id, 
		debug_thread_id, 
		0,
		7 % threading::core_count()
	);
}

void engine_world::finalize_file_system_device	( fs_new::asynchronous_device_interface * const device )
{
	device->finalize					( );
}

void engine_world::finalize_file_system_devices	( )
{
	xray::debug::scoped_fault_tracer fault_tracer( "M8 file retirement fault", background_render_capture() );
	threading::interlocked_exchange		( m_file_system_devices_destruction_started, true );
	auto hdd_ptr = m_hdd_async_interface.c_ptr();
	apc::run							( apc::hdd,	[this, hdd_ptr]() { this->finalize_file_system_device(hdd_ptr); }, apc::break_process_loop, apc::wait_for_completion );
	auto dvd_ptr = m_dvd_async_interface.c_ptr();
	apc::run							( apc::dvd,	[this, dvd_ptr]() { this->finalize_file_system_device(dvd_ptr); }, apc::break_process_loop, apc::wait_for_completion );
	if (background_render_capture()) std::cerr<<"M8 shutdown: file consumers drained\n";


	m_hdd_async_interface->finalize_thread_usage	( );
	m_dvd_async_interface->finalize_thread_usage	( );
	// Producer threads remain quiescent in APC until resource/VFS destruction
	// and device-consumer finalization have returned their last queue nodes.
	// Each producer must destroy its own TLS response queue before exiting.
	for (auto thread:{apc::logic,apc::editor,apc::network,apc::sound,apc::build,apc::res_man,apc::res_cook,apc::fs_watcher})
	{
		if (background_render_capture()) std::cerr<<"M8 shutdown: retiring file producer "<<unsigned(thread)<<'\n';
		apc::run_remote_only(thread,[this] {
			m_hdd_async_interface->finalize_thread_usage();m_dvd_async_interface->finalize_thread_usage();
		},apc::break_process_loop,apc::wait_for_completion);
	}

	XRAY_DESTROY_REFERENCE				( m_hdd_async_interface );
	XRAY_DESTROY_REFERENCE				( m_dvd_async_interface );

	apc::wait							( apc::hdd );
	apc::wait							( apc::dvd );
	if (background_render_capture()) std::cerr<<"M8 shutdown: file devices destroyed\n";
}

void engine_world::file_system_devices_tick	( )
{
	m_hdd_async_interface->tick			( false );
	m_dvd_async_interface->tick			( false );
}

} // namespace engine
} // namespace xray
