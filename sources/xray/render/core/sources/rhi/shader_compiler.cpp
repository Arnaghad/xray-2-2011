#include "shader_compiler.h"
#include "content_hash.h"
#include "dxc_abi.h"
#include "shader_disk_cache.h"

#include <xray/os_platform.h>

#include <atomic>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <memory>
#include <mutex>
#include <sstream>
#include <stdexcept>
#include <string>
#include <unordered_set>
#include <utility>
#include <vector>

#if !XRAY_PLATFORM_WINDOWS
#	include <unistd.h>
#endif

namespace xray {
namespace render {
namespace rhi {

namespace {

constexpr std::uint32_t k_spirv_magic = 0x07230203u;
constexpr std::size_t   k_max_memory_cache_entries = 64u;

#if XRAY_PLATFORM_WINDOWS
constexpr char k_dxc_library_filename[] = "dxcompiler.dll";
constexpr char k_dxc_cli_filename[] = "dxc.exe";
#else
constexpr char k_dxc_library_filename[] = "libdxcompiler.so";
constexpr char k_dxc_cli_filename[] = "dxc";
#endif

struct backend_compile_output
{
	std::vector<u32>           bytecode;
	std::vector<include_probe> includes;
	std::string                error_log;
	bool                       success = false;
};

class i_shader_compiler_backend
{
public:
	virtual ~i_shader_compiler_backend() = default;
	virtual char const* name() const noexcept = 0;
	virtual bool available() const noexcept = 0;
	virtual std::string const& init_error() const noexcept = 0;
	virtual backend_compile_output compile(
		std::string const& source,
		std::vector<std::string> const& args_utf8
	) = 0;
};

struct cache_entry
{
	std::string                source;
	std::string                cwd_utf8;
	std::vector<std::string>   args_utf8;
	std::vector<include_probe> includes;
	compilation_result         result;
};

bool env_flag_enabled( char const* name )
{
	std::string value;
	if ( !xray::os::get_environment_variable( name, value ) )
		return false;
	return value == "1" || value == "true" || value == "TRUE" || value == "yes" || value == "YES";
}

bool is_cli_executable_name( std::filesystem::path const& path )
{
	std::string const name = path_to_utf8( path.filename() );
	return name == "dxc" || name == "dxc.exe" || name == "dxc-3.7";
}

class tracking_includes final : public dxc::i_dxc_include_handler
{
public:
	explicit tracking_includes( dxc::i_dxc_utils* utils ) noexcept : m_utils( utils ) {}

	std::vector<include_probe> probes;

	dxc::dxc_hresult XRAY_DXC_CALL QueryInterface( dxc::dxc_guid const& iid, void** out ) override
	{
		if ( !out )
			return dxc::k_dxc_e_pointer;
		*out = nullptr;
		if ( iid == dxc::k_iid_iunknown || iid == dxc::k_iid_dxc_include_handler ) {
			*out = static_cast<dxc::i_dxc_include_handler*>( this );
			AddRef();
			return dxc::k_dxc_s_ok;
		}
		return dxc::k_dxc_e_nointerface;
	}

	dxc::dxc_ulong XRAY_DXC_CALL AddRef() override
	{
		return ++m_refs;
	}

	dxc::dxc_ulong XRAY_DXC_CALL Release() override
	{
		dxc::dxc_ulong const n = --m_refs;
		if ( n == 0 )
			delete this;
		return n;
	}

	dxc::dxc_hresult XRAY_DXC_CALL LoadSource( wchar_t const* filename, dxc::i_dxc_blob** out ) override
	{
		if ( !out )
			return dxc::k_dxc_e_pointer;
		*out = nullptr;
		if ( !filename || !m_utils )
			return dxc::k_dxc_e_fail;

		try {
			std::string const utf8_name = dxc::dxc_wide_to_utf8( filename, std::wcslen( filename ) );
			include_probe probed = probe_include_file( utf8_name );
			bool const loaded = probed.loaded;
			if ( loaded ) {
				dxc::dxc_com_ptr<dxc::i_dxc_blob_encoding> blob;
				void const* ptr = probed.bytes.empty() ? static_cast<void const*>( "" ) : probed.bytes.data();
				dxc::dxc_hresult const hr = m_utils->CreateBlob(
					ptr,
					static_cast<dxc::dxc_uint32>( probed.bytes.size() ),
					dxc::k_dxc_cp_utf8,
					blob.put()
				);
				if ( dxc::dxc_failed( hr ) || !blob ) {
					probes.push_back( std::move( probed ) );
					return dxc::k_dxc_e_outofmemory;
				}
				*out = blob.detach();
			}
			probes.push_back( std::move( probed ) );
			return loaded ? dxc::k_dxc_s_ok : dxc::k_dxc_e_file_not_found;
		} catch ( ... ) {
			if ( *out ) {
				( *out )->Release();
				*out = nullptr;
			}
			return dxc::k_dxc_e_outofmemory;
		}
	}

private:
	std::atomic<dxc::dxc_ulong>             m_refs{ 1 };
	dxc::dxc_com_ptr<dxc::i_dxc_utils>      m_utils;
};

class dxc_library_backend final : public i_shader_compiler_backend
{
public:
	dxc_library_backend()
	{
		std::string configured;
		if ( xray::os::get_environment_variable( "XRAY_DXC_PATH", configured ) && !configured.empty() ) {
			std::filesystem::path const configured_path = path_from_utf8( configured );
			if ( !configured_path.is_absolute() ) {
				m_error = std::string( "XRAY_DXC_PATH must be an absolute path to " ) + k_dxc_library_filename;
				return;
			}
			if ( is_cli_executable_name( configured_path ) ) {
				m_error = "XRAY_DXC_PATH points to DXC CLI executable rather than shared library";
				return;
			}
			if ( !m_library.open( configured_path, true ) ) {
				m_error = "Cannot load DXC from XRAY_DXC_PATH (" + configured + "): " + m_library.last_error();
				return;
			}
		} else {
			bool opened = false;
			std::filesystem::path const exe_dir = xray::os::executable_directory();
			if ( !exe_dir.empty() ) {
				std::filesystem::path const adjacent = exe_dir / k_dxc_library_filename;
				std::error_code ec;
				if ( std::filesystem::is_regular_file( adjacent, ec ) )
					opened = m_library.open( adjacent, true );
#if !XRAY_PLATFORM_WINDOWS
				if ( !opened ) {
					std::filesystem::path const sibling_lib = ( exe_dir / ".." / "lib" / k_dxc_library_filename ).lexically_normal();
					if ( std::filesystem::is_regular_file( sibling_lib, ec ) )
						opened = m_library.open( sibling_lib, true );
				}
#endif
			}
			if ( !opened )
				opened = m_library.open( std::filesystem::path( k_dxc_library_filename ), false );

			if ( !opened ) {
				m_error = std::string( "Cannot load DXC (" ) + m_library.last_error()
					+ "). Deploy " + k_dxc_library_filename + " beside the executable, set XRAY_DXC_PATH, or use XRAY_SHADER_PRECOMPILED_DIR.";
				return;
			}
		}

		auto const create = m_library.symbol_as<dxc::dxc_create_instance_fn>( "DxcCreateInstance" );
		if ( !create ) {
			m_error = "DxcCreateInstance symbol not found in loaded DXC library";
			m_library.close();
			return;
		}

		dxc::dxc_hresult const hr_utils = create(
			dxc::k_clsid_dxc_utils,
			dxc::k_iid_dxc_utils,
			reinterpret_cast<void**>( m_utils.put() )
		);
		dxc::dxc_hresult const hr_compiler = create(
			dxc::k_clsid_dxc_compiler,
			dxc::k_iid_dxc_compiler3,
			reinterpret_cast<void**>( m_compiler.put() )
		);
		if ( dxc::dxc_failed( hr_utils ) || !m_utils || dxc::dxc_failed( hr_compiler ) || !m_compiler ) {
			m_error = "DXC COM initialization failed (DxcCreateInstance)";
			m_compiler.reset();
			m_utils.reset();
			m_library.close();
		}
	}

	~dxc_library_backend() override
	{
		m_compiler.reset();
		m_utils.reset();
		m_library.close();
	}

	char const* name() const noexcept override { return "dxc_library"; }
	bool available() const noexcept override { return m_error.empty() && m_utils && m_compiler; }
	std::string const& init_error() const noexcept override { return m_error; }

	backend_compile_output compile(
		std::string const& source,
		std::vector<std::string> const& args_utf8 ) override
	{
		backend_compile_output out;

		std::vector<std::wstring> wide_args;
		wide_args.reserve( args_utf8.size() );
		for ( auto const& arg : args_utf8 )
			wide_args.push_back( dxc::utf8_to_dxc_wide( arg ) );

		std::vector<wchar_t const*> arg_ptrs;
		arg_ptrs.reserve( wide_args.size() );
		for ( auto const& warg : wide_args )
			arg_ptrs.push_back( warg.c_str() );

		dxc::dxc_com_ptr<tracking_includes> includes;
		includes.attach( new tracking_includes( m_utils.get() ) );

		dxc::dxc_buffer const buffer = {
			source.data(),
			source.size(),
			dxc::k_dxc_cp_utf8
		};

		dxc::dxc_com_ptr<dxc::i_dxc_result> compiled;
		dxc::dxc_hresult hr = m_compiler->Compile(
			&buffer,
			arg_ptrs.data(),
			static_cast<dxc::dxc_uint32>( arg_ptrs.size() ),
			includes.get(),
			dxc::k_iid_dxc_result,
			reinterpret_cast<void**>( compiled.put() )
		);
		if ( dxc::dxc_failed( hr ) || !compiled )
			throw std::runtime_error( "IDxcCompiler3::Compile failed (HRESULT=" + std::to_string( hr ) + ")" );

		dxc::dxc_com_ptr<dxc::i_dxc_blob_utf8> errors;
		compiled->GetOutput(
			dxc::dxc_out_errors,
			dxc::k_iid_dxc_blob_utf8,
			reinterpret_cast<void**>( errors.put() ),
			nullptr
		);
		if ( errors && errors->GetStringLength() > 0 && errors->GetStringPointer() )
			out.error_log.assign( errors->GetStringPointer(), errors->GetStringLength() );

		dxc::dxc_hresult status = dxc::k_dxc_e_fail;
		if ( dxc::dxc_failed( compiled->GetStatus( &status ) ) || dxc::dxc_failed( status ) ) {
			if ( out.error_log.empty() )
				out.error_log = "DXC returned failed compilation status";
			return out;
		}

		dxc::dxc_com_ptr<dxc::i_dxc_blob> object;
		hr = compiled->GetOutput(
			dxc::dxc_out_object,
			dxc::k_iid_dxc_blob,
			reinterpret_cast<void**>( object.put() ),
			nullptr
		);
		if ( dxc::dxc_failed( hr ) || !object || object->GetBufferSize() < 20u || ( object->GetBufferSize() % sizeof( u32 ) ) != 0 )
			throw std::runtime_error( "DXC returned invalid SPIR-V object size" );

		out.bytecode.resize( object->GetBufferSize() / sizeof( u32 ) );
		std::memcpy( out.bytecode.data(), object->GetBufferPointer(), object->GetBufferSize() );
		if ( out.bytecode[0] != k_spirv_magic )
			throw std::runtime_error( "DXC output is not SPIR-V" );

		out.includes = std::move( includes->probes );
		out.success = true;
		return out;
	}

private:
	xray::os::dynamic_library              m_library;
	dxc::dxc_com_ptr<dxc::i_dxc_utils>     m_utils;
	dxc::dxc_com_ptr<dxc::i_dxc_compiler3> m_compiler;
	std::string                            m_error;
};

class dxc_process_backend final : public i_shader_compiler_backend
{
public:
	dxc_process_backend()
	{
		std::error_code ec;
		std::string cli_env;
		if ( xray::os::get_environment_variable( "XRAY_DXC_CLI_PATH", cli_env ) && !cli_env.empty() ) {
			std::filesystem::path const cli_path = path_from_utf8( cli_env );
			if ( !cli_path.is_absolute() || !is_executable_file( cli_path ) ) {
				m_error = "XRAY_DXC_CLI_PATH does not point to an executable DXC CLI binary: " + cli_env;
				return;
			}
			m_executable = cli_path;
			return;
		}

		std::string dxc_env;
		if ( xray::os::get_environment_variable( "XRAY_DXC_PATH", dxc_env ) && !dxc_env.empty() ) {
			std::filesystem::path const p = path_from_utf8( dxc_env );
			if ( p.is_absolute() && is_cli_executable_name( p ) && is_executable_file( p ) ) {
				m_executable = p;
				return;
			}
			std::filesystem::path const parent = p.parent_path();
			if ( !parent.empty() ) {
				for ( auto const& candidate : {
						parent / k_dxc_cli_filename,
						( parent / ".." / "bin" / k_dxc_cli_filename ).lexically_normal() } ) {
					if ( is_executable_file( candidate ) ) {
						m_executable = candidate;
						return;
					}
				}
			}
		}

		std::filesystem::path const exe_dir = xray::os::executable_directory();
		if ( !exe_dir.empty() ) {
			for ( auto const& candidate : {
					exe_dir / k_dxc_cli_filename,
					( exe_dir / ".." / "bin" / k_dxc_cli_filename ).lexically_normal() } ) {
				if ( is_executable_file( candidate ) ) {
					m_executable = candidate;
					return;
				}
			}
		}

		std::string path_env;
		if ( xray::os::get_environment_variable( "PATH", path_env ) && !path_env.empty() ) {
#if XRAY_PLATFORM_WINDOWS
			constexpr char k_sep = ';';
#else
			constexpr char k_sep = ':';
#endif
			std::size_t start = 0;
			while ( start <= path_env.size() ) {
				std::size_t const end = path_env.find( k_sep, start );
				std::string const entry = ( end == std::string::npos )
					? path_env.substr( start )
					: path_env.substr( start, end - start );
				if ( !entry.empty() ) {
					std::filesystem::path const candidate = path_from_utf8( entry ) / k_dxc_cli_filename;
					if ( is_executable_file( candidate ) ) {
						m_executable = candidate;
						return;
					}
				}
				if ( end == std::string::npos )
					break;
				start = end + 1;
			}
		}

		m_error = std::string( "Cannot locate DXC CLI executable (" ) + k_dxc_cli_filename + ")";
	}

	char const* name() const noexcept override { return "dxc_process"; }
	bool available() const noexcept override { return m_error.empty() && !m_executable.empty(); }
	std::string const& init_error() const noexcept override { return m_error; }

	backend_compile_output compile(
		std::string const& source,
		std::vector<std::string> const& args_utf8 ) override
	{
		backend_compile_output out;

		struct temp_cleanup
		{
			std::filesystem::path hlsl;
			std::filesystem::path spv;
			~temp_cleanup() noexcept
			{
				std::error_code ec;
				if ( !hlsl.empty() ) std::filesystem::remove( hlsl, ec );
				if ( !spv.empty() ) std::filesystem::remove( spv, ec );
			}
		} guard;

		static std::atomic<std::uint64_t> s_seq{ 0 };
		std::uint64_t const seq = ++s_seq;
		std::filesystem::path const tmp_dir = std::filesystem::temp_directory_path();
		std::string const stamp = "xray_dxc_" + std::to_string( xray::os::current_process_id() )
			+ "_" + std::to_string( seq );
		guard.hlsl = tmp_dir / ( stamp + ".hlsl" );
		guard.spv = tmp_dir / ( stamp + ".spv" );

		{
			std::ofstream src_out( guard.hlsl, std::ios::binary | std::ios::trunc );
			if ( !src_out )
				throw std::runtime_error( "Failed to create temporary HLSL file for dxc CLI" );
			if ( !source.empty() )
				src_out.write( source.data(), static_cast<std::streamsize>( source.size() ) );
			src_out.flush();
			if ( src_out.fail() )
				throw std::runtime_error( "Failed to write temporary HLSL file for dxc CLI" );
		}

		std::vector<std::string> cli_args = args_utf8;
		cli_args.push_back( "-Fo" );
		cli_args.push_back( path_to_utf8( guard.spv ) );
		cli_args.push_back( path_to_utf8( guard.hlsl ) );

		xray::os::process_result const proc = xray::os::run_process( m_executable, cli_args );
		if ( !proc.started )
			throw std::runtime_error( "Failed to launch dxc CLI (" + path_to_utf8( m_executable ) + "): " + proc.launch_error );

		if ( proc.exit_code != 0 ) {
			out.error_log = proc.output.empty()
				? ( "dxc CLI exited with code " + std::to_string( proc.exit_code ) )
				: proc.output;
			return out;
		}
		if ( !proc.output.empty() )
			out.error_log = proc.output;

		std::error_code ec;
		auto const spv_size = std::filesystem::file_size( guard.spv, ec );
		if ( ec || spv_size < 20u || ( spv_size % sizeof( u32 ) ) != 0 )
			throw std::runtime_error( "dxc CLI returned invalid SPIR-V file size" );

		std::ifstream spv_in( guard.spv, std::ios::binary );
		if ( !spv_in )
			throw std::runtime_error( "Failed to open SPIR-V output produced by dxc CLI" );

		out.bytecode.resize( static_cast<std::size_t>( spv_size / sizeof( u32 ) ) );
		if ( !spv_in.read( reinterpret_cast<char*>( out.bytecode.data() ), static_cast<std::streamsize>( spv_size ) )
			|| static_cast<std::size_t>( spv_in.gcount() ) != spv_size )
			throw std::runtime_error( "Failed to read SPIR-V output produced by dxc CLI" );

		if ( out.bytecode[0] != k_spirv_magic )
			throw std::runtime_error( "dxc CLI output is not SPIR-V" );

		collect_source_includes( source, args_utf8, out.includes );
		out.success = true;
		return out;
	}

private:
	static bool is_executable_file( std::filesystem::path const& path ) noexcept
	{
		std::error_code ec;
		if ( !std::filesystem::is_regular_file( path, ec ) )
			return false;
#if XRAY_PLATFORM_WINDOWS
		return true;
#else
		return ::access( path.c_str(), X_OK ) == 0;
#endif
	}

	static void collect_source_includes(
		std::string const& root_source,
		std::vector<std::string> const& args_utf8,
		std::vector<include_probe>& out_includes )
	{
		std::vector<std::filesystem::path> include_dirs;
		for ( std::size_t i = 0; i + 1 < args_utf8.size(); ++i ) {
			if ( args_utf8[i] == "-I" )
				include_dirs.push_back( path_from_utf8( args_utf8[i + 1] ) );
		}
		include_dirs.push_back( std::filesystem::current_path() );

		std::unordered_set<std::string> visited;
		std::vector<std::pair<std::filesystem::path, std::string>> queue;
		queue.emplace_back( std::filesystem::path{}, root_source );

		while ( !queue.empty() ) {
			auto [base_dir, text] = std::move( queue.back() );
			queue.pop_back();

			std::istringstream lines( text );
			std::string line;
			while ( std::getline( lines, line ) ) {
				std::size_t const pos = line.find_first_not_of( " \t\r" );
				if ( pos == std::string::npos || line[pos] != '#' )
					continue;
				std::size_t const inc = line.find( "include", pos + 1 );
				if ( inc == std::string::npos )
					continue;
				std::size_t const q1 = line.find( '"', inc + 7 );
				if ( q1 == std::string::npos )
					continue;
				std::size_t const q2 = line.find( '"', q1 + 1 );
				if ( q2 == std::string::npos || q2 <= q1 + 1 )
					continue;

				std::string const rel = line.substr( q1 + 1, q2 - q1 - 1 );
				std::filesystem::path const rel_path = path_from_utf8( rel );

				auto try_dir = [&]( std::filesystem::path const& dir ) -> bool {
					std::filesystem::path const candidate = dir.empty() ? rel_path : ( dir / rel_path );
					include_probe probed = probe_include_file( path_to_utf8( candidate ) );
					if ( !probed.loaded )
						return false;
					if ( visited.insert( probed.path_utf8 ).second ) {
						std::string nested( reinterpret_cast<char const*>( probed.bytes.data() ), probed.bytes.size() );
						queue.emplace_back( path_from_utf8( probed.path_utf8 ).parent_path(), std::move( nested ) );
						out_includes.push_back( std::move( probed ) );
					}
					return true;
				};

				bool found = !base_dir.empty() && try_dir( base_dir );
				for ( std::size_t d = 0; !found && d < include_dirs.size(); ++d )
					found = try_dir( include_dirs[d] );
			}
		}
	}

	std::filesystem::path m_executable;
	std::string           m_error;
};

void validate_reserved_flag( std::string const& flag )
{
	if ( flag == "-spirv" || flag.starts_with( "-fspv-target-env" ) || flag.starts_with( "-fspv-entrypoint-name" )
		|| flag.starts_with( "-T" ) || flag.starts_with( "/T" ) || flag.starts_with( "-E" ) || flag.starts_with( "/E" )
		|| flag.starts_with( "-Fo" ) || flag.starts_with( "/Fo" ) || flag == "-Vd" ) {
		throw std::runtime_error( "Reserved DXC option: " + flag );
	}
}

std::vector<std::string> build_compile_args_utf8(
	std::string const& profile,
	shader_compile_options const& options )
{
	if ( options.entry.empty() )
		throw std::runtime_error( "Shader entry must not be empty" );

	std::vector<std::string> args = {
		"-spirv",
		"-fspv-target-env=vulkan1.3",
		"-HV",
		"2021",
		"-T",
		profile,
		"-E",
		options.entry,
		"-fspv-entrypoint-name=" + options.entry
	};
	for ( auto const& directory : options.include_directories ) {
		args.push_back( "-I" );
		args.push_back( directory );
	}
	for ( auto const& define : options.defines ) {
		args.push_back( "-D" );
		args.push_back( define );
	}
	for ( auto const& flag : options.flags ) {
		validate_reserved_flag( flag );
		args.push_back( flag );
	}
	return args;
}

} // namespace

struct shader_compiler_impl
{
	std::unique_ptr<i_shader_compiler_backend> backend;
	std::vector<cache_entry>                   cache;
	std::mutex                                 mutex;
	std::string                                error;

	shader_compiler_impl()
	{
		std::string mode;
		xray::os::get_environment_variable( "XRAY_DXC_BACKEND", mode );

		if ( mode == "process" ) {
			auto proc = std::make_unique<dxc_process_backend>();
			if ( proc->available() )
				backend = std::move( proc );
			else
				error = proc->init_error();
			return;
		}

		auto lib = std::make_unique<dxc_library_backend>();
		if ( lib->available() ) {
			backend = std::move( lib );
			return;
		}

		if ( mode == "library" ) {
			error = lib->init_error();
			return;
		}

		auto proc = std::make_unique<dxc_process_backend>();
		if ( proc->available() ) {
			backend = std::move( proc );
			return;
		}

		error = lib->init_error() + " (CLI fallback: " + proc->init_error() + ")";
	}
};

shader_compiler::shader_compiler()
	: m_impl( std::make_unique<shader_compiler_impl>() )
{
}

shader_compiler::~shader_compiler() = default;

std::string shader_compiler::get_target_profile( shader_stage stage )
{
	switch ( stage ) {
		case shader_stage::vertex:   return "vs_6_0";
		case shader_stage::pixel:    return "ps_6_0";
		case shader_stage::geometry: return "gs_6_0";
		case shader_stage::hull:     return "hs_6_0";
		case shader_stage::domain:   return "ds_6_0";
		case shader_stage::compute:  return "cs_6_0";
		default:                     throw std::runtime_error( "Unknown shader stage" );
	}
}

namespace {

compilation_result compile_internal(
	shader_compiler_impl& impl,
	std::string const& profile,
	std::string const& source,
	std::filesystem::path const& source_path,
	shader_compile_options const& options )
{
	std::lock_guard lock( impl.mutex );
	compilation_result result;
	try {
		std::vector<std::string> const args_utf8 = build_compile_args_utf8( profile, options );
		std::string const cwd_utf8 = normalize_path_utf8( std::filesystem::current_path() );

		// 1. In-memory LRU-like cache
		for ( auto it = impl.cache.begin(); it != impl.cache.end(); ++it ) {
			if ( it->source != source || it->args_utf8 != args_utf8 || it->cwd_utf8 != cwd_utf8 )
				continue;
			bool current = true;
			for ( auto const& dependency : it->includes ) {
				if ( !( probe_include_file( dependency.path_utf8 ) == dependency ) ) {
					current = false;
					break;
				}
			}
			if ( current ) {
				result = it->result;
				result.cache_hit = true;
				return result;
			}
			impl.cache.erase( it );
			break;
		}

		// 2. OS-agnostic SPIR-V disk cache
		std::string const cache_key = compute_spirv_cache_key( source, cwd_utf8, args_utf8 );
		std::filesystem::path disk_cache_path;
		std::filesystem::path const user_data = cache_util::resolve_sdk_user_data_dir();
		if ( !user_data.empty() ) {
			disk_cache_path = user_data / "cache" / "spirv" / ( "spv_v1_" + cache_key + ".bin" );
			std::vector<include_probe> disk_includes;
			if ( try_load_spirv_disk_cache( disk_cache_path, disk_includes, result ) ) {
				if ( impl.cache.size() == k_max_memory_cache_entries )
					impl.cache.erase( impl.cache.begin() );
				impl.cache.push_back( { source, cwd_utf8, args_utf8, std::move( disk_includes ), result } );
				return result;
			}
		}

		// 3. Optional precompiled .spv directory
		std::string precompiled_dir_env;
		if ( xray::os::get_environment_variable( "XRAY_SHADER_PRECOMPILED_DIR", precompiled_dir_env ) && !precompiled_dir_env.empty() ) {
			std::filesystem::path const spv_path = resolve_precompiled_spv_path(
				path_from_utf8( precompiled_dir_env ),
				cache_key,
				source_path,
				options
			);
			if ( !spv_path.empty() && try_load_precompiled_spv( spv_path, result ) ) {
				if ( impl.cache.size() == k_max_memory_cache_entries )
					impl.cache.erase( impl.cache.begin() );
				impl.cache.push_back( { source, cwd_utf8, args_utf8, {}, result } );
				return result;
			}
		}

		if ( env_flag_enabled( "XRAY_SHADER_PRECOMPILED_ONLY" ) ) {
			result.error_log = "Precompiled SPIR-V not found (XRAY_SHADER_PRECOMPILED_ONLY=1)";
			return result;
		}

		// 4. Live DXC compilation via active backend
		if ( !impl.backend || !impl.backend->available() ) {
			result.error_log = impl.error;
			return result;
		}

		backend_compile_output compiled = impl.backend->compile( source, args_utf8 );
		result.error_log = std::move( compiled.error_log );
		if ( !compiled.success )
			return result;

		result.bytecode = std::move( compiled.bytecode );
		result.success = true;
		result.cache_hit = false;

		if ( !disk_cache_path.empty() )
			write_spirv_disk_cache( disk_cache_path, compiled.includes, result.bytecode );

		if ( impl.cache.size() == k_max_memory_cache_entries )
			impl.cache.erase( impl.cache.begin() );
		impl.cache.push_back( { source, cwd_utf8, args_utf8, std::move( compiled.includes ), result } );
	} catch ( std::exception const& error ) {
		result.error_log = error.what();
		result.bytecode.clear();
		result.success = false;
		result.cache_hit = false;
	}
	return result;
}

} // namespace

compilation_result shader_compiler::compile(
	std::string const& source,
	shader_stage stage,
	shader_compile_options const& options )
{
	std::string profile;
	try {
		profile = get_target_profile( stage );
	} catch ( std::exception const& error ) {
		return { {}, error.what(), false, false };
	}
	return compile_internal( *m_impl, profile, source, {}, options );
}

compilation_result shader_compiler::compile_file(
	std::string const& path,
	shader_stage stage,
	shader_compile_options const& options )
{
	std::string profile;
	try {
		profile = get_target_profile( stage );
	} catch ( std::exception const& error ) {
		return { {}, error.what(), false, false };
	}

	static std::filesystem::path const packaged_shaders = []() -> std::filesystem::path {
		std::filesystem::path const exe_path = xray::os::executable_path();
		if ( exe_path.empty() )
			return {};
		std::error_code ec;
		auto const pkg_root = exe_path.parent_path().parent_path();
		auto const marker = pkg_root / "settings" / "sdk-runtime.json";
		auto const shaders = pkg_root / "shaders";
		if ( std::filesystem::is_regular_file( marker, ec ) && std::filesystem::is_directory( shaders, ec ) )
			return shaders.lexically_normal();
		return {};
	}();

	std::filesystem::path effective_path = path_from_utf8( path );
	if ( !packaged_shaders.empty() ) {
		std::string const generic = effective_path.lexically_normal().generic_string();
		std::error_code ec;
		constexpr char marker[] = "/render/vulkan/assets/";
		if ( std::size_t const pos = generic.find( marker ); pos != std::string::npos ) {
			std::filesystem::path const candidate = packaged_shaders / path_from_utf8( generic.substr( pos + sizeof( marker ) - 1 ) );
			if ( std::filesystem::is_regular_file( candidate, ec ) )
				effective_path = candidate;
		}
	}

	std::ifstream file( effective_path, std::ios::binary );
	if ( !file )
		return { {}, "Could not open shader file: " + path, false, false };
	std::stringstream contents;
	contents << file.rdbuf();
	if ( file.bad() )
		return { {}, "Could not read shader file: " + path, false, false };

	auto resolved = options;
	auto const parent = effective_path.parent_path();
	if ( !parent.empty() )
		resolved.include_directories.insert( resolved.include_directories.begin(), path_to_utf8( parent ) );

	return compile_internal( *m_impl, profile, contents.str(), effective_path, resolved );
}

} // namespace rhi
} // namespace render
} // namespace xray
