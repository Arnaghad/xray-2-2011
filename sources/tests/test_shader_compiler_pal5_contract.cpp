#include "test_check.hpp"

#include <xray/os_platform.h>
#include <xray/render/core/sources/rhi/dxc_abi.h>
#include <xray/render/core/sources/rhi/shader_disk_cache.h>

#include <atomic>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

namespace rhi = xray::render::rhi;
namespace dxc = xray::render::rhi::dxc;

struct dummy_com_object final : public dxc::i_dxc_unknown
{
	std::atomic<int>& alive_count;
	std::atomic<dxc::dxc_ulong> refs{ 1 };

	explicit dummy_com_object( std::atomic<int>& counter ) : alive_count( counter )
	{
		++alive_count;
	}

	~dummy_com_object()
	{
		--alive_count;
	}

	dxc::dxc_hresult XRAY_DXC_CALL QueryInterface( dxc::dxc_guid const& riid, void** out ) override
	{
		if ( !out )
			return dxc::k_dxc_e_pointer;
		*out = nullptr;
		if ( riid == dxc::k_iid_iunknown ) {
			*out = static_cast<dxc::i_dxc_unknown*>( this );
			AddRef();
			return dxc::k_dxc_s_ok;
		}
		return dxc::k_dxc_e_nointerface;
	}

	dxc::dxc_ulong XRAY_DXC_CALL AddRef() override
	{
		return ++refs;
	}

	dxc::dxc_ulong XRAY_DXC_CALL Release() override
	{
		dxc::dxc_ulong const n = --refs;
		if ( n == 0 )
			delete this;
		return n;
	}
};

void set_env_var( char const* name, char const* value )
{
#if XRAY_PLATFORM_WINDOWS
	_putenv_s( name, value ? value : "" );
#else
	if ( value )
		::setenv( name, value, 1 );
	else
		::unsetenv( name );
#endif
}

struct env_guard
{
	std::string name;
	std::string prev;
	bool        had = false;

	env_guard( char const* n, char const* val ) : name( n )
	{
		had = xray::os::get_environment_variable( n, prev );
		set_env_var( n, val );
	}

	~env_guard()
	{
		set_env_var( name.c_str(), had ? prev.c_str() : nullptr );
	}
};

void test_utf8_wide_roundtrip()
{
	std::vector<std::string> const samples = {
		"",
		"static_gbuffer.hlsl",
		"-fspv-target-env=vulkan1.3",
		"/mnt/steam_games/Шейдери/текст_тест.hlsl",
		"emoji_\xF0\x9F\x9A\x80_end"
	};

	for ( auto const& s : samples ) {
		std::wstring const wide = dxc::utf8_to_dxc_wide( s );
		std::string const back = dxc::dxc_wide_to_utf8( wide );
		XRAY_TEST_CHECK( back == s );
	}

	bool threw = false;
	try {
		std::string const bad = "\xFF\xFE\x80";
		(void)dxc::utf8_to_dxc_wide( bad );
	} catch ( std::runtime_error const& ) {
		threw = true;
	}
	XRAY_TEST_CHECK( threw );
}

void test_com_ptr()
{
	std::atomic<int> alive{ 0 };
	{
		dxc::dxc_com_ptr<dummy_com_object> p1;
		p1.attach( new dummy_com_object( alive ) );
		XRAY_TEST_CHECK( alive.load() == 1 );
		XRAY_TEST_CHECK( static_cast<bool>( p1 ) );

		dxc::dxc_com_ptr<dummy_com_object> p2 = p1;
		XRAY_TEST_CHECK( alive.load() == 1 );
		XRAY_TEST_CHECK( p1.get() == p2.get() );

		dxc::dxc_com_ptr<dummy_com_object> p3 = std::move( p1 );
		XRAY_TEST_CHECK( !p1 );
		XRAY_TEST_CHECK( alive.load() == 1 );

		p2.reset();
		XRAY_TEST_CHECK( alive.load() == 1 );
		p3.reset();
		XRAY_TEST_CHECK( alive.load() == 0 );
	}
	XRAY_TEST_CHECK( alive.load() == 0 );
}

void test_disk_cache_and_precompiled()
{
	std::filesystem::path const root = std::filesystem::temp_directory_path()
		/ ( "xray_pal5_contract_" + std::to_string( xray::os::current_process_id() ) );
	std::error_code ec;
	std::filesystem::remove_all( root, ec );
	std::filesystem::create_directories( root, ec );

	struct dir_cleanup
	{
		std::filesystem::path path;
		~dir_cleanup()
		{
			std::error_code e;
			std::filesystem::remove_all( path, e );
		}
	} cleanup{ root };

	std::filesystem::path const inc_path = root / "common.hlsli";
	{
		std::ofstream f( inc_path, std::ios::binary );
		f << "// v1\n";
	}

	rhi::include_probe probed = rhi::probe_include_file( rhi::path_to_utf8( inc_path ) );
	XRAY_TEST_CHECK( probed.loaded );
	XRAY_TEST_CHECK( probed.path_utf8.find( '\\' ) == std::string::npos );

	std::vector<std::string> const args1 = { "-spirv", "-T", "ps_6_0", "-E", "main" };
	std::vector<std::string> const args2 = { "-spirv", "-T", "vs_6_0", "-E", "main" };
	std::string const key1 = rhi::compute_spirv_cache_key( "float4 main() : SV_Target { return 0; }", "/tmp/cwd", args1 );
	std::string const key2 = rhi::compute_spirv_cache_key( "float4 main() : SV_Target { return 0; }", "/tmp/cwd", args1 );
	std::string const key3 = rhi::compute_spirv_cache_key( "float4 main() : SV_Target { return 0; }", "/tmp/cwd", args2 );
	XRAY_TEST_CHECK( key1.size() == 64 );
	XRAY_TEST_CHECK( key1 == key2 );
	XRAY_TEST_CHECK( key1 != key3 );

	std::vector<u32> const fake_spv = { 0x07230203u, 0x00010500u, 0x00080001u, 0x00000010u, 0x00000000u };
	std::filesystem::path const cache_file = root / "cache.bin";
	rhi::write_spirv_disk_cache( cache_file, { probed }, fake_spv );
	XRAY_TEST_CHECK( std::filesystem::is_regular_file( cache_file ) );

	std::vector<rhi::include_probe> loaded_includes;
	rhi::compilation_result loaded_res;
	XRAY_TEST_CHECK( rhi::try_load_spirv_disk_cache( cache_file, loaded_includes, loaded_res ) );
	XRAY_TEST_CHECK( loaded_res.success );
	XRAY_TEST_CHECK( loaded_res.bytecode == fake_spv );
	XRAY_TEST_CHECK( loaded_includes.size() == 1 );

	// Mutate include file -> cache must invalidate
	{
		std::ofstream f( inc_path, std::ios::binary | std::ios::trunc );
		f << "// v2 modified\n";
	}
	loaded_res = {};
	loaded_includes.clear();
	XRAY_TEST_CHECK( !rhi::try_load_spirv_disk_cache( cache_file, loaded_includes, loaded_res ) );

	// Corrupt cache file -> must fail cleanly
	std::filesystem::path const corrupt_file = root / "corrupt.bin";
	{
		std::ofstream f( corrupt_file, std::ios::binary );
		f << "not_a_valid_spirv_cache_blob_payload";
	}
	XRAY_TEST_CHECK( !rhi::try_load_spirv_disk_cache( corrupt_file, loaded_includes, loaded_res ) );

	// Precompiled .spv lookup without live DXC
	std::filesystem::path const precompiled_dir = root / "precompiled";
	std::filesystem::create_directories( precompiled_dir, ec );
	std::filesystem::path const spv_path = precompiled_dir / "sample.custom_ps.spv";
	{
		std::ofstream f( spv_path, std::ios::binary );
		f.write( reinterpret_cast<char const*>( fake_spv.data() ), static_cast<std::streamsize>( fake_spv.size() * sizeof( u32 ) ) );
	}
	std::filesystem::path const hlsl_path = root / "sample.hlsl";
	{
		std::ofstream f( hlsl_path, std::ios::binary );
		f << "float4 custom_ps() : SV_Target { return 1; }\n";
	}

	env_guard disable_cache( "XRAY_SDK_DISABLE_COOKED_CACHE", "1" );
	env_guard bad_dxc( "XRAY_DXC_PATH", "/nonexistent/dxc/libdxcompiler.so" );
	env_guard pre_dir( "XRAY_SHADER_PRECOMPILED_DIR", rhi::path_to_utf8( precompiled_dir ).c_str() );
	env_guard pre_only( "XRAY_SHADER_PRECOMPILED_ONLY", "1" );

	rhi::shader_compiler compiler;
	rhi::shader_compile_options opts;
	opts.entry = "custom_ps";
	rhi::compilation_result const pre_res = compiler.compile_file( rhi::path_to_utf8( hlsl_path ), rhi::shader_stage::pixel, opts );
	XRAY_TEST_CHECK( pre_res.success );
	XRAY_TEST_CHECK( pre_res.cache_hit );
	XRAY_TEST_CHECK( pre_res.bytecode == fake_spv );

	rhi::shader_compile_options missing_opts;
	missing_opts.entry = "missing_entry";
	rhi::compilation_result const miss_res = compiler.compile_file( rhi::path_to_utf8( hlsl_path ), rhi::shader_stage::pixel, missing_opts );
	XRAY_TEST_CHECK( !miss_res.success );
	XRAY_TEST_CHECK( !miss_res.error_log.empty() );
}

void test_run_process()
{
#if !XRAY_PLATFORM_WINDOWS
	xray::os::process_result const ok = xray::os::run_process( "/bin/echo", { "pal5-proc-ok" } );
	XRAY_TEST_CHECK( ok.started );
	XRAY_TEST_CHECK( ok.exit_code == 0 );
	XRAY_TEST_CHECK( ok.output.find( "pal5-proc-ok" ) != std::string::npos );

	xray::os::process_result const missing = xray::os::run_process( "/nonexistent/xray_missing_binary_pal5", {} );
	XRAY_TEST_CHECK( !missing.started );
	XRAY_TEST_CHECK( !missing.launch_error.empty() );
#endif
}

} // namespace

int main()
{
	test_utf8_wide_roundtrip();
	test_com_ptr();
	test_disk_cache_and_precompiled();
	test_run_process();
	std::cout << "PASS: xray_shader_compiler_pal5_contract\n";
	return 0;
}
