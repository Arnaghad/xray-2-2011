#include "shader_disk_cache.h"
#include "content_hash.h"
#include "dxc_abi.h"

#include <cstring>
#include <fstream>
#include <limits>
#include <stdexcept>

namespace xray {
namespace render {
namespace rhi {

namespace {

constexpr std::uint32_t k_disk_cache_magic = 0x56505358u;   // "XSPV"
constexpr std::uint32_t k_disk_cache_version = 1u;
constexpr std::uint32_t k_disk_cache_footer = 0x444E4553u;  // "SEND"
constexpr std::uint32_t k_spirv_magic = 0x07230203u;
constexpr std::size_t   k_max_include_count = 64u;
constexpr std::size_t   k_max_include_path_len = 16384u;
constexpr std::uint64_t k_max_disk_bytes = 32ull * 1024u * 1024u;
constexpr std::uint32_t k_max_spirv_words = 4u * 1024u * 1024u;

std::uint32_t decode_utf8_codepoint( std::string const& utf8, std::size_t& pos )
{
	auto const b0 = static_cast<unsigned char>( utf8[pos] );
	if ( b0 < 0x80u ) {
		++pos;
		return b0;
	}

	auto invalid = []() -> std::uint32_t {
		throw std::runtime_error( "Invalid UTF-8 compiler option" );
	};

	std::size_t extra = 0;
	std::uint32_t cp = 0;
	std::uint32_t min_cp = 0;
	if ( ( b0 & 0xE0u ) == 0xC0u ) {
		extra = 1;
		cp = b0 & 0x1Fu;
		min_cp = 0x80u;
	} else if ( ( b0 & 0xF0u ) == 0xE0u ) {
		extra = 2;
		cp = b0 & 0x0Fu;
		min_cp = 0x800u;
	} else if ( ( b0 & 0xF8u ) == 0xF0u ) {
		extra = 3;
		cp = b0 & 0x07u;
		min_cp = 0x10000u;
	} else {
		return invalid();
	}

	if ( pos + 1 + extra > utf8.size() )
		return invalid();

	for ( std::size_t i = 1; i <= extra; ++i ) {
		auto const bx = static_cast<unsigned char>( utf8[pos + i] );
		if ( ( bx & 0xC0u ) != 0x80u )
			return invalid();
		cp = ( cp << 6 ) | ( bx & 0x3Fu );
	}
	pos += 1 + extra;

	if ( cp < min_cp || cp > 0x10FFFFu || ( cp >= 0xD800u && cp <= 0xDFFFu ) )
		return invalid();
	return cp;
}

void append_utf8_codepoint( std::string& out, std::uint32_t cp )
{
	if ( cp <= 0x7Fu ) {
		out.push_back( static_cast<char>( cp ) );
	} else if ( cp <= 0x7FFu ) {
		out.push_back( static_cast<char>( 0xC0u | ( cp >> 6 ) ) );
		out.push_back( static_cast<char>( 0x80u | ( cp & 0x3Fu ) ) );
	} else if ( cp <= 0xFFFFu ) {
		out.push_back( static_cast<char>( 0xE0u | ( cp >> 12 ) ) );
		out.push_back( static_cast<char>( 0x80u | ( ( cp >> 6 ) & 0x3Fu ) ) );
		out.push_back( static_cast<char>( 0x80u | ( cp & 0x3Fu ) ) );
	} else if ( cp <= 0x10FFFFu ) {
		out.push_back( static_cast<char>( 0xF0u | ( cp >> 18 ) ) );
		out.push_back( static_cast<char>( 0x80u | ( ( cp >> 12 ) & 0x3Fu ) ) );
		out.push_back( static_cast<char>( 0x80u | ( ( cp >> 6 ) & 0x3Fu ) ) );
		out.push_back( static_cast<char>( 0x80u | ( cp & 0x3Fu ) ) );
	} else {
		// U+FFFD replacement character for unexpected out-of-range wide code units
		out.push_back( static_cast<char>( 0xEF ) );
		out.push_back( static_cast<char>( 0xBF ) );
		out.push_back( static_cast<char>( 0xBD ) );
	}
}

bool is_trivial_precompiled_flags( shader_compile_options const& options ) noexcept
{
	if ( !options.defines.empty() )
		return false;
	for ( auto const& flag : options.flags ) {
		if ( flag != "-fvk-invert-y" )
			return false;
	}
	return true;
}

} // namespace

namespace dxc {

std::wstring utf8_to_dxc_wide( std::string const& utf8 )
{
	if ( utf8.empty() )
		return {};

	std::wstring result;
	result.reserve( utf8.size() );
	std::size_t pos = 0;
	while ( pos < utf8.size() ) {
		std::uint32_t const cp = decode_utf8_codepoint( utf8, pos );
		if constexpr ( sizeof( wchar_t ) == 2 ) {
			if ( cp <= 0xFFFFu ) {
				result.push_back( static_cast<wchar_t>( cp ) );
			} else {
				std::uint32_t const adjusted = cp - 0x10000u;
				result.push_back( static_cast<wchar_t>( 0xD800u + ( adjusted >> 10 ) ) );
				result.push_back( static_cast<wchar_t>( 0xDC00u + ( adjusted & 0x3FFu ) ) );
			}
		} else {
			result.push_back( static_cast<wchar_t>( cp ) );
		}
	}
	return result;
}

std::string dxc_wide_to_utf8( wchar_t const* data, std::size_t length )
{
	if ( !data || length == 0 )
		return {};

	std::string out;
	out.reserve( length );
	std::size_t i = 0;
	while ( i < length ) {
		std::uint32_t cp = static_cast<std::uint32_t>( data[i] );
		if constexpr ( sizeof( wchar_t ) == 2 ) {
			cp &= 0xFFFFu;
			if ( cp >= 0xD800u && cp <= 0xDBFFu && ( i + 1 ) < length ) {
				std::uint32_t const low = static_cast<std::uint32_t>( data[i + 1] ) & 0xFFFFu;
				if ( low >= 0xDC00u && low <= 0xDFFFu ) {
					cp = 0x10000u + ( ( ( cp - 0xD800u ) << 10 ) | ( low - 0xDC00u ) );
					++i;
				}
			}
		}
		append_utf8_codepoint( out, cp );
		++i;
	}
	return out;
}

std::string dxc_wide_to_utf8( std::wstring const& wide )
{
	return dxc_wide_to_utf8( wide.data(), wide.size() );
}

} // namespace dxc

std::filesystem::path path_from_utf8( std::string const& utf8 )
{
	if ( utf8.empty() )
		return {};
	std::u8string_view const u8( reinterpret_cast<char8_t const*>( utf8.data() ), utf8.size() );
	return std::filesystem::path( u8 );
}

std::string path_to_utf8( std::filesystem::path const& path )
{
	std::u8string const u8 = path.u8string();
	return std::string( reinterpret_cast<char const*>( u8.data() ), u8.size() );
}

std::string normalize_path_utf8( std::filesystem::path const& path )
{
	std::error_code ec;
	std::filesystem::path resolved = std::filesystem::absolute( path, ec );
	std::filesystem::path const& normal_base = ec ? path : resolved;
	std::u8string const u8 = normal_base.lexically_normal().generic_u8string();
	return std::string( reinterpret_cast<char const*>( u8.data() ), u8.size() );
}

include_probe probe_include_file( std::string const& path_utf8 )
{
	include_probe probed;
	std::filesystem::path const native_path = path_from_utf8( path_utf8 );
	probed.path_utf8 = normalize_path_utf8( native_path );

	std::filesystem::path const normalized_path = path_from_utf8( probed.path_utf8 );
	std::error_code ec;
	if ( !std::filesystem::is_regular_file( normalized_path, ec ) )
		return probed;

	auto const file_size = std::filesystem::file_size( normalized_path, ec );
	if ( ec || file_size > k_max_disk_bytes )
		return probed;

	std::ifstream file( normalized_path, std::ios::binary );
	if ( !file )
		return probed;

	probed.bytes.resize( static_cast<std::size_t>( file_size ) );
	if ( !probed.bytes.empty() ) {
		if ( !file.read( reinterpret_cast<char*>( probed.bytes.data() ), static_cast<std::streamsize>( probed.bytes.size() ) )
			|| static_cast<std::size_t>( file.gcount() ) != probed.bytes.size() ) {
			probed.bytes.clear();
			return probed;
		}
	}
	probed.loaded = true;
	return probed;
}

std::array<unsigned char, 32> hash_bytes( std::vector<unsigned char> const& bytes ) noexcept
{
	cache_util::sha256_hasher hasher;
	if ( !bytes.empty() )
		hasher.update( bytes.data(), bytes.size() );
	return hasher.finalize();
}

std::string compute_spirv_cache_key(
	std::string const& source,
	std::string const& cwd_utf8,
	std::vector<std::string> const& args_utf8 )
{
	cache_util::sha256_hasher hasher;
	static constexpr char tag[] = "xray_spv_v1";
	hasher.update( tag, sizeof( tag ) - 1 );
	hasher.update_string( source );
	hasher.update_string( cwd_utf8 );
	std::uint32_t const arg_count = static_cast<std::uint32_t>( args_utf8.size() );
	hasher.update_pod( arg_count );
	for ( auto const& arg : args_utf8 )
		hasher.update_string( arg );
	return cache_util::hex_string( hasher.finalize() );
}

bool try_load_spirv_disk_cache(
	std::filesystem::path const& file_path,
	std::vector<include_probe>& out_includes,
	compilation_result& out_result )
{
	if ( file_path.empty() )
		return false;

	std::error_code ec;
	auto const file_size = std::filesystem::file_size( file_path, ec );
	if ( ec || file_size < 16 || file_size > k_max_disk_bytes )
		return false;

	std::ifstream in( file_path, std::ios::binary );
	if ( !in )
		return false;

	std::vector<unsigned char> raw( static_cast<std::size_t>( file_size ) );
	if ( !in.read( reinterpret_cast<char*>( raw.data() ), static_cast<std::streamsize>( raw.size() ) )
		|| static_cast<std::size_t>( in.gcount() ) != raw.size() )
		return false;

	std::size_t pos = 0;
	auto read_pod = [&]( auto& value ) -> bool {
		if ( pos + sizeof( value ) > raw.size() )
			return false;
		std::memcpy( &value, raw.data() + pos, sizeof( value ) );
		pos += sizeof( value );
		return true;
	};

	std::uint32_t magic = 0, version = 0, include_count = 0;
	if ( !read_pod( magic ) || !read_pod( version ) || !read_pod( include_count ) )
		return false;
	if ( magic != k_disk_cache_magic || version != k_disk_cache_version || include_count > k_max_include_count )
		return false;

	std::vector<include_probe> probed;
	probed.reserve( include_count );
	for ( std::uint32_t i = 0; i < include_count; ++i ) {
		std::uint32_t path_len = 0;
		if ( !read_pod( path_len ) || path_len == 0 || path_len > k_max_include_path_len || path_len > raw.size() - pos )
			return false;
		std::string inc_path_utf8( reinterpret_cast<char const*>( raw.data() + pos ), path_len );
		pos += path_len;

		std::uint8_t stored_loaded = 0;
		if ( !read_pod( stored_loaded ) || stored_loaded > 1u )
			return false;

		std::array<unsigned char, 32> stored_hash = {};
		if ( !read_pod( stored_hash ) )
			return false;

		include_probe current = probe_include_file( inc_path_utf8 );
		if ( current.loaded != ( stored_loaded != 0 ) )
			return false;
		if ( current.loaded && hash_bytes( current.bytes ) != stored_hash )
			return false;
		probed.push_back( std::move( current ) );
	}

	std::uint32_t word_count = 0;
	if ( !read_pod( word_count ) || word_count < 5u || word_count > k_max_spirv_words )
		return false;

	std::size_t const byte_count = static_cast<std::size_t>( word_count ) * sizeof( std::uint32_t );
	if ( byte_count > raw.size() - pos )
		return false;

	std::vector<u32> bytecode( word_count );
	std::memcpy( bytecode.data(), raw.data() + pos, byte_count );
	pos += byte_count;
	if ( bytecode[0] != k_spirv_magic )
		return false;

	std::uint32_t footer = 0;
	if ( !read_pod( footer ) || footer != k_disk_cache_footer || pos != raw.size() )
		return false;

	out_includes = std::move( probed );
	out_result = {};
	out_result.bytecode = std::move( bytecode );
	out_result.success = true;
	out_result.cache_hit = false;
	return true;
}

void write_spirv_disk_cache(
	std::filesystem::path const& file_path,
	std::vector<include_probe> const& includes,
	std::vector<u32> const& bytecode )
{
	if ( file_path.empty() || bytecode.size() < 5u )
		return;

	std::error_code ec;
	if ( std::filesystem::is_regular_file( file_path, ec ) )
		return;

	struct encoded_include
	{
		std::string path_utf8;
		std::uint8_t loaded = 0;
		std::array<unsigned char, 32> hash = {};
	};

	std::vector<encoded_include> items;
	items.reserve( includes.size() );
	std::size_t total_bytes = 5u * sizeof( std::uint32_t ) + bytecode.size() * sizeof( u32 );
	for ( auto const& inc : includes ) {
		if ( inc.path_utf8.empty() || inc.path_utf8.size() > k_max_include_path_len )
			return;
		total_bytes += sizeof( std::uint32_t ) + inc.path_utf8.size() + 1u + 32u;
		items.push_back( {
			inc.path_utf8,
			static_cast<std::uint8_t>( inc.loaded ? 1u : 0u ),
			inc.loaded ? hash_bytes( inc.bytes ) : std::array<unsigned char, 32>{}
		} );
	}

	std::vector<unsigned char> blob( total_bytes );
	std::size_t pos = 0;
	auto push_pod = [&]( auto const& v ) {
		std::memcpy( blob.data() + pos, &v, sizeof( v ) );
		pos += sizeof( v );
	};

	std::uint32_t const inc_count = static_cast<std::uint32_t>( items.size() );
	push_pod( k_disk_cache_magic );
	push_pod( k_disk_cache_version );
	push_pod( inc_count );
	for ( auto const& item : items ) {
		std::uint32_t const len = static_cast<std::uint32_t>( item.path_utf8.size() );
		push_pod( len );
		std::memcpy( blob.data() + pos, item.path_utf8.data(), len );
		pos += len;
		push_pod( item.loaded );
		push_pod( item.hash );
	}

	std::uint32_t const word_count = static_cast<std::uint32_t>( bytecode.size() );
	push_pod( word_count );
	std::size_t const spv_bytes = bytecode.size() * sizeof( u32 );
	std::memcpy( blob.data() + pos, bytecode.data(), spv_bytes );
	pos += spv_bytes;
	push_pod( k_disk_cache_footer );

	cache_util::atomic_write_file( file_path, blob.data(), blob.size() );
}

bool try_load_precompiled_spv(
	std::filesystem::path const& spv_path,
	compilation_result& out_result )
{
	if ( spv_path.empty() )
		return false;

	std::error_code ec;
	if ( !std::filesystem::is_regular_file( spv_path, ec ) )
		return false;

	auto const file_size = std::filesystem::file_size( spv_path, ec );
	if ( ec || file_size < 20u || ( file_size % sizeof( u32 ) ) != 0 || file_size > k_max_disk_bytes )
		return false;

	std::ifstream file( spv_path, std::ios::binary );
	if ( !file )
		return false;

	std::vector<u32> words( static_cast<std::size_t>( file_size / sizeof( u32 ) ) );
	if ( !file.read( reinterpret_cast<char*>( words.data() ), static_cast<std::streamsize>( file_size ) )
		|| static_cast<std::size_t>( file.gcount() ) != file_size )
		return false;

	if ( words[0] != k_spirv_magic )
		return false;

	out_result = {};
	out_result.bytecode = std::move( words );
	out_result.success = true;
	out_result.cache_hit = true;
	return true;
}

std::filesystem::path resolve_precompiled_spv_path(
	std::filesystem::path const& precompiled_dir,
	std::string const& cache_key,
	std::filesystem::path const& source_path,
	shader_compile_options const& options )
{
	if ( precompiled_dir.empty() )
		return {};

	std::error_code ec;
	if ( !std::filesystem::is_directory( precompiled_dir, ec ) )
		return {};

	if ( !cache_key.empty() ) {
		std::filesystem::path const keyed = precompiled_dir / ( cache_key + ".spv" );
		if ( std::filesystem::is_regular_file( keyed, ec ) )
			return keyed;
	}

	if ( !source_path.empty() && is_trivial_precompiled_flags( options ) ) {
		std::string const stem = path_to_utf8( source_path.stem() );
		if ( !stem.empty() && !options.entry.empty() ) {
			std::filesystem::path const by_entry = precompiled_dir / path_from_utf8( stem + "." + options.entry + ".spv" );
			if ( std::filesystem::is_regular_file( by_entry, ec ) )
				return by_entry;

			if ( options.entry == "main" ) {
				std::filesystem::path const by_stem = precompiled_dir / path_from_utf8( stem + ".spv" );
				if ( std::filesystem::is_regular_file( by_stem, ec ) )
					return by_stem;
			}
		}
	}

	return {};
}

} // namespace rhi
} // namespace render
} // namespace xray
