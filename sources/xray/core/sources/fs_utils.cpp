////////////////////////////////////////////////////////////////////////////
//	Created		: 19.05.2010
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/fs_utils.h>
#include <xray/resources_fs_iterator.h>
#include <xray/fs_path_iterator.h>
#include "fs_helper.h"
#include "fs_watcher_internal.h"

namespace xray {
namespace fs {

signalling_bool   open_file_impl (FILE * * const					out_file,
								  enum_flags<open_file_enum> const	open_flags, 
								  pcstr	const 						portable_path,
								  bool const						assert_on_fail);

bool   make_dir_r (pcstr portable_path, bool create_last)
{
	verify_path_is_portable			(portable_path);
	pcstr colon_pos				=	strchr(portable_path, ':');

	bool const has_drive		=	!!colon_pos;
	bool const has_root_slash	=	!has_drive && portable_path[0] == '/';

	path_string		cur_path;
	u32 drive_part_length		=	0;
	if ( has_drive )
	{
		pcstr disk_letter_end	=	colon_pos + 1;
		cur_path.append				(portable_path, disk_letter_end);
		cur_path				+=	'/';
		drive_part_length		=	u32(disk_letter_end - portable_path) + 1;
	}
	else if ( has_root_slash )
	{
		cur_path				=	"/";
		drive_part_length		=	1;
	}

	u32 const path_len			=	strings::length(portable_path);
	if ( drive_part_length >= path_len )
		return						true;

	path_iterator	it				(portable_path + drive_part_length, // skip '/' if absolute path
									 int(path_len - drive_part_length));

	path_iterator	end_it		=	path_iterator::end();
	if ( it == end_it )
		return						true;

	bool			result		=	true;

	while ( it != end_it )
	{
		fs::path_string				part;
		it.to_string				(part);
		cur_path				+=	part;

		path_iterator next_it	=	it;
		++next_it;

		if ( !create_last && next_it == end_it )
			return					result;

		result					&=	make_dir(cur_path.c_str());
		cur_path				+=	"/";
		it						=	next_it;
	}

	return							result;
}

#if XRAY_FS_WATCHER_ENABLED
signalling_bool	  calculate_file_size (file_size_type * const out_file_size, file_type * const file)
{
	return						calculate_file_size(out_file_size, file_type_to_FILE(file));
}
#endif // #if XRAY_FS_WATCHER_ENABLED

signalling_bool   calculate_file_size (file_size_type * const out_file_size, pcstr const file_path)
{
	FILE * file				=	nullptr;
	if ( !open_file_impl(& file, open_file_read, file_path, false) )
		return					false;
	
	bool const out_result	=	calculate_file_size	(out_file_size, file);
	fclose						(file);
	return						out_result;
}

void   setvbuf (file_type * stream, char * buffer, int mode, size_t size)
{
	::setvbuf					(file_type_to_FILE(stream), buffer, mode, size);
}

void   flush_file (file_type * file)
{
	fflush						(file_type_to_FILE(file));
}


bool   seek_file (file_type * file, file_size_type offset, int origin)
{
#if XRAY_PLATFORM_WINDOWS
	// dunno why _fseeki64 tells non-0 success result sometimes
	// while fseek works correctly!
	_fseeki64					(file_type_to_FILE(file), offset, origin);
#else
	::fseeko					(file_type_to_FILE(file), static_cast<off_t>(offset), origin);
#endif
	return						true;
}

file_size_type   tell_file (file_type * file)
{
#if XRAY_PLATFORM_WINDOWS
	return						_ftelli64(file_type_to_FILE(file));
#else
	return						static_cast<file_size_type>(::ftello(file_type_to_FILE(file)));
#endif
}



static bool s_user_data_folder_initialized = false;
static string_path s_user_data_folder;

pcstr get_default_user_data_folder( );

pcstr get_user_data_folder	( ) 
{
	static pcstr s_user_data_folder_default = get_default_user_data_folder( );
	if ( !s_user_data_folder_initialized )
		set_user_data_folder( s_user_data_folder_default );

	return					s_user_data_folder;
}

void set_user_data_folder	( pcstr const user_data_folder ) 
{
	s_user_data_folder_initialized	= true;
	strings::copy	( s_user_data_folder, user_data_folder );
}

void   calculate_file_hash (u32 * const out_file_hash, FILE * file)
{
	u32 const		chunk_size	=	128*1024;
	char			file_chunk		[chunk_size];

	* out_file_hash				=	0;
	do
	{
		u32 const bytes_read	=	(u32)fread(file_chunk, 1, chunk_size, file);
		if ( !bytes_read )
			break;

		* out_file_hash			=	crc32(file_chunk, bytes_read, * out_file_hash);
	} while ( xray::identity(true) ) ;
}

signalling_bool   calculate_file_hash (u32 * const out_file_hash, pcstr const file_path)
{
	FILE * file				=	nullptr;
	if ( !open_file_impl(& file, open_file_read, file_path, false) )
		return					false;
	
	calculate_file_hash			(out_file_hash, file);
	fclose						(file);
	return						true;
}

u32	  last_modify_time_by_logical_path (pcstr logical_path, memory::base_allocator * allocator)
{
	path_info					info;
	if ( get_path_info_by_logical_path(& info, logical_path, allocator) == path_info::type_nothing )
		return					0;

	return						info.file_last_modify_time;
}

bool   file_exists (pcstr path)
{
	FILE * f							=	0;
	if ( !open_file_impl(&f, open_file_read, path, false) )
		return								false;

	fclose									(f);
	return									true;
}

} // namespace fs
} // namespace xray
