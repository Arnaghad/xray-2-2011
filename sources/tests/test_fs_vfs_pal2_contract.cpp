#include "test_check.hpp"

#define XRAY_LOG_MODULE_INITIATOR "tests"
#include <xray/extensions.h>
#include <xray/fs/case_insensitive_path.h>
#include <xray/fs/native_hdd_file_system.h>
#include <xray/fs/path_string_utils.h>
#include <xray/fs/physical_path_info.h>
#include <xray/os_raii.h>

#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>

namespace {

std::string path_to_utf8( std::filesystem::path const& p )
{
	auto const u8 = p.u8string();
	return std::string( reinterpret_cast<char const*>( u8.data() ), u8.size() );
}

} // namespace

int main()
{
	std::error_code ec;
	auto const tmp_root = std::filesystem::temp_directory_path() / "xray_pal2_fs_contract_test";
	std::filesystem::remove_all( tmp_root, ec );
	XRAY_TEST_CHECK( std::filesystem::create_directories( tmp_root, ec ) );

	auto cleanup_tmp = xray::os::make_scoped_cleanup( [&tmp_root]() {
		std::error_code cleanup_ec;
		xray::fs_new::clear_case_insensitive_path_cache();
		std::filesystem::remove_all( tmp_root, cleanup_ec );
	} );

	// 1. Verify native_hdd_file_system factory and roundtrip file/directory I/O.
	{
		xray::fs_new::device_file_system_interface* device = xray::fs_new::create_native_hdd_file_system();
		XRAY_TEST_CHECK( device != nullptr );
		auto cleanup_dev = xray::os::make_scoped_cleanup( [device]() {
			xray::fs_new::destroy_native_hdd_file_system( device );
		} );

		auto const work_dir_p = tmp_root / "io_work";
		auto const empty_dir_p = work_dir_p / "empty_sub";
		auto const file_p = work_dir_p / "sample.bin";

		xray::fs_new::native_path_string work_dir = xray::fs_new::native_path_string::convert( path_to_utf8( work_dir_p ).c_str() );
		xray::fs_new::native_path_string empty_dir = xray::fs_new::native_path_string::convert( path_to_utf8( empty_dir_p ).c_str() );
		xray::fs_new::native_path_string file_path = xray::fs_new::native_path_string::convert( path_to_utf8( file_p ).c_str() );

		XRAY_TEST_CHECK( device->create_folder( work_dir ) );
		XRAY_TEST_CHECK( device->create_folder( empty_dir ) );

		char const payload[] = "hello-pal2-fs!";
		xray::file_size_type const payload_len = sizeof( payload ) - 1;

		pvoid handle = nullptr;
		xray::fs_new::open_file_params write_params(
			xray::fs_new::file_mode::create_always,
			xray::fs_new::file_access::write,
			xray::assert_on_fail_false,
			xray::fs_new::notify_watcher_false,
			xray::fs_new::use_buffering_true
		);
		XRAY_TEST_CHECK( device->open( &handle, file_path, write_params ) );
		XRAY_TEST_CHECK( handle != nullptr );

		// Ensure setvbuf no longer steals or corrupts the OS handle.
		char vbuf[256] = {};
		device->setvbuf( handle, vbuf, _IOFBF, sizeof( vbuf ) );

		XRAY_TEST_CHECK( device->write( handle, payload, payload_len ) == payload_len );
		device->flush( handle );
		device->close( handle );
		handle = nullptr;

		xray::fs_new::open_file_params read_params(
			xray::fs_new::file_mode::open_existing,
			xray::fs_new::file_access::read,
			xray::assert_on_fail_false,
			xray::fs_new::notify_watcher_false,
			xray::fs_new::use_buffering_true
		);
		XRAY_TEST_CHECK( device->open( &handle, file_path, read_params ) );
		xray::file_size_type file_size = 0;
		XRAY_TEST_CHECK( device->get_file_size( &file_size, handle ) );
		XRAY_TEST_CHECK( file_size == payload_len );
		XRAY_TEST_CHECK( device->seek( handle, 6, xray::fs_new::seek_file_begin ) );
		XRAY_TEST_CHECK( device->tell( handle ) == 6 );
		char tail[16] = {};
		XRAY_TEST_CHECK( device->read( handle, tail, payload_len - 6 ) == payload_len - 6 );
		XRAY_TEST_CHECK( std::strcmp( tail, "pal2-fs!" ) == 0 );
		XRAY_TEST_CHECK( device->seek( handle, 0, xray::fs_new::seek_file_begin ) );
		char full_read[32] = {};
		XRAY_TEST_CHECK( device->read( handle, full_read, payload_len ) == payload_len );
		XRAY_TEST_CHECK( std::strcmp( full_read, payload ) == 0 );
		device->close( handle );

		auto const info = device->get_physical_path_info( file_path );
		XRAY_TEST_CHECK( info.exists() );
		XRAY_TEST_CHECK( info.is_file() );
		XRAY_TEST_CHECK( info.get_file_size() == payload_len );

		xray::fs_new::native_path_string mask = work_dir;
		mask += xray::fs_new::native_path_string::separator;
		mask += "*";

		u64 search_handle = u64( -1 );
		xray::fs_new::physical_path_info_data entry_data;
		XRAY_TEST_CHECK( device->find_first( &search_handle, &entry_data, mask.c_str() ) );
		bool saw_file = false;
		bool saw_empty_dir = false;
		if ( search_handle != u64( -1 ) ) {
			do {
				if ( std::strcmp( entry_data.path.c_str(), "sample.bin" ) == 0 ) {
					saw_file = true;
					XRAY_TEST_CHECK( entry_data.is_file() );
				} else if ( std::strcmp( entry_data.path.c_str(), "empty_sub" ) == 0 ) {
					saw_empty_dir = true;
					XRAY_TEST_CHECK( entry_data.is_folder() );
				}
			} while ( device->find_next( &search_handle, &entry_data ) );
			if ( search_handle != u64( -1 ) )
				device->find_close( search_handle );
		}
		XRAY_TEST_CHECK( saw_file );
		XRAY_TEST_CHECK( saw_empty_dir );

		xray::fs_new::native_path_string empty_mask = empty_dir;
		empty_mask += xray::fs_new::native_path_string::separator;
		empty_mask += "*";
		u64 empty_search = 0;
		xray::fs_new::physical_path_info_data empty_data;
		XRAY_TEST_CHECK( device->find_first( &empty_search, &empty_data, empty_mask.c_str() ) );
		XRAY_TEST_CHECK( empty_search == u64( -1 ) );

		XRAY_TEST_CHECK( device->erase( file_path ) );
		XRAY_TEST_CHECK( device->erase( empty_dir ) );
		XRAY_TEST_CHECK( device->erase( work_dir ) );
	}

	// 2. Verify cached case-insensitive path resolver recovers canonical on-disk casing.
	{
		auto const nested_dir = tmp_root / "MixedRoot" / "SubDirA";
		XRAY_TEST_CHECK( std::filesystem::create_directories( nested_dir, ec ) );
		auto const asset_file = nested_dir / "TextureFile.DDS";
		{
			std::ofstream os( asset_file, std::ios::binary );
			os << "DDS!";
			XRAY_TEST_CHECK( os.good() );
		}

		xray::fs_new::clear_case_insensitive_path_cache();

		std::filesystem::path resolved_lower;
		XRAY_TEST_CHECK( xray::fs_new::resolve_path_case_insensitive_segments(
			tmp_root,
			"mixedroot/subdira/texturefile.dds",
			resolved_lower
		) );
		XRAY_TEST_CHECK( path_to_utf8( resolved_lower.filename() ) == "TextureFile.DDS" );
		XRAY_TEST_CHECK( path_to_utf8( resolved_lower.parent_path().filename() ) == "SubDirA" );

		// Repeat with uppercase and backslashes (exercises cache lookup too).
		std::filesystem::path resolved_upper;
		XRAY_TEST_CHECK( xray::fs_new::resolve_path_case_insensitive_segments(
			tmp_root,
			"MIXEDROOT\\SUBDIRA\\TEXTUREFILE.DDS",
			resolved_upper
		) );
		XRAY_TEST_CHECK( path_to_utf8( resolved_upper.filename() ) == "TextureFile.DDS" );

		std::filesystem::path missing_out;
		XRAY_TEST_CHECK( !xray::fs_new::resolve_path_case_insensitive_segments(
			tmp_root,
			"mixedroot/subdira/does_not_exist.dds",
			missing_out
		) );
		xray::fs_new::clear_case_insensitive_path_cache();
	}

	// 3. Verify portable path predicates and separator-independent path_crc32.
	{
		xray::fs_new::native_path_string rel_p = xray::fs_new::native_path_string::convert( "meshes/actors/hero.ogf" );
		XRAY_TEST_CHECK( !xray::fs_new::is_absolute_path( rel_p ) );
#if XRAY_PLATFORM_WINDOWS
		xray::fs_new::native_path_string abs_drive = "C:\\xray\\resources";
		xray::fs_new::native_path_string abs_unc = "\\\\server\\share\\data";
		XRAY_TEST_CHECK( xray::fs_new::is_absolute_path( abs_drive ) );
		XRAY_TEST_CHECK( xray::fs_new::is_absolute_path( abs_unc ) );
#else
		xray::fs_new::native_path_string abs_posix = "/opt/xray/resources";
		XRAY_TEST_CHECK( xray::fs_new::is_absolute_path( abs_posix ) );
#endif
		u32 const h1 = xray::fs_new::path_crc32( "gamedata/configs/system.ltx", 27 );
		u32 const h2 = xray::fs_new::path_crc32( "gamedata\\configs\\system.ltx", 27 );
		XRAY_TEST_CHECK( h1 == h2 );
	}

	std::cout << "PASS: xray_fs_vfs_pal2_contract\n";
	return 0;
}
