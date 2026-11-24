#include "pch.h"
#include "resources_device_manager.h"
#include "resources_macros.h"
#include <xray/managed_allocator.h>
#include "resources_manager.h"
#include <xray/resources_query_result.h>
#include <xray/os_include.h>
#include <xray/fs_utils.h>
#include "fs_watcher_internal.h"

namespace xray {
namespace resources {

using namespace		fs;

// out: false if cannot open file, ready and fail flags are set
bool   device_manager::open_file (fs_new::file_type * *							out_file, 
								  query_result *									query, 
								  fs_new::synchronous_device_interface const &	device)
{
	XRAY_UNREFERENCED_PARAMETER(device);
	using namespace							fs;

	ASSERT									(query);
	ASSERT									(!m_file_handle);

	if ( !query->is_save_type() )
	{
		auto it					=	query->get_fat_it();
		if ( it.is_end() || it.is_folder() )
		{
			query->set_error_type			(query_result::error_type_file_not_found);
			return							false;
		}
	}

	fs_new::native_path_string const &	native_path		=	query->absolute_physical_path();
	const fs_new::virtual_path_string portable_path = fs_new::virtual_path_string::convert(native_path.c_str());

	u32	notifications_to_skip			=	0;

	if ( query->is_load_type() )
	{
 		m_file_handle					=	CreateFile (native_path.c_str(),
 														GENERIC_READ,
 														FILE_SHARE_READ | FILE_SHARE_WRITE,
 														nullptr,
 														OPEN_EXISTING,
 														FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED
 																			  /*| FILE_FLAG_NO_BUFFERING*/,
 														nullptr);
	}
	else
	{
		notifications_to_skip			=	fs::open_file_notifications_count(portable_path.c_str(), open_file_create | open_file_truncate) 
											 + 1; // for write about WriteFile

		skip_deassociation_notifications	(portable_path.c_str(), notifications_to_skip);
		make_dir_r							(portable_path.c_str(), false);

		m_file_handle					=	CreateFile (native_path.c_str(),
 														GENERIC_WRITE,
 														FILE_SHARE_WRITE | FILE_SHARE_READ,
 														nullptr,
 														CREATE_ALWAYS,//OPEN_ALWAYS,
 														FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED,
 														nullptr);
	}

	LOGI_DEBUG								("resources:device_manager", "async %s opening %s %s", 
											query->is_load_type() ? "read" : "write",
											m_file_handle == INVALID_HANDLE_VALUE ? "failed" : "successfull",
											detail::make_query_logging_string (query).c_str());

	if ( m_last_file_name != native_path )
	{
		m_sector_data_last_file_pos		=	(file_size_type)-1;
		m_last_file_name				=	native_path;
	}
	
	if ( m_file_handle == INVALID_HANDLE_VALUE )
	{
		m_file_handle = nullptr;
		bool const removed				=	try_remove_deassociation_notifications(portable_path.c_str(), notifications_to_skip);
		R_ASSERT_U							(removed);

		query->set_error_type				(query_result::error_type_cannot_open_file);
		return								false;
	}

	if ( out_file )
		*out_file = (fs_new::file_type*)m_file_handle;

	return									true;
}

bool   device_manager::do_async_operation (fs_new::file_type *							file, 
										   query_result *									query,
										   fs_new::synchronous_device_interface const &	device,
										   mutable_buffer									out_data, 
										   file_size_type const							file_pos, 
										   bool											sector_aligned)
{
	XRAY_UNREFERENCED_PARAMETER(device);
	XRAY_UNREFERENCED_PARAMETER(file);
	COMPILE_ASSERT	(	sizeof(OVERLAPPED) == sizeof(device_manager::m_overlapped), 
						please_change_buffer_size );

	OVERLAPPED overlapped				=	{};
	overlapped.hEvent					=	m_async_file_operation_finished_event.get_handle();
	if ( sector_aligned )
	{
		R_ASSERT							(!(file_pos % m_sector_size));
		R_ASSERT							(!(out_data.size() % m_sector_size));
	}
	
	overlapped.Offset					=	file_pos & 0xFFFFFFFF;
	overlapped.OffsetHigh				=	file_pos >> 32;

	BOOL const async_op_result			=	query->is_load_type() ? 
											ReadFile(m_file_handle, out_data.c_ptr(), out_data.size(), nullptr, & overlapped) :
											WriteFile(m_file_handle, out_data.c_ptr(), out_data.size(), nullptr, & overlapped);

	DWORD bytes_done;
	bool result							=	true;
	if ( !async_op_result )
		if ( GetLastError() != ERROR_IO_PENDING )
			result						=	false;

	if ( result )
	{
		m_async_file_operation_finished_event.wait	(threading::wait_time_infinite);
		if ( !GetOverlappedResult(m_file_handle, & overlapped, & bytes_done, FALSE) )
			result						=	false;
		else if (bytes_done != out_data.size())
			result = false;
	}

	if ( !result )
	{
		query->set_error_type				(query->is_load_type() ? 
											 query_result::error_type_cannot_read_file :
											 query_result::error_type_cannot_write_file);
		return								false;
	}

	return									true;
}

void   device_manager::close_file		()
{
	CloseHandle								(m_file_handle);
	m_file_handle = nullptr;
}


} // namespace resources
} // namespace xray
