////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Cached case-insensitive path resolver implementation
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/fs/case_insensitive_path.h>

#include <algorithm>
#include <cctype>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace xray {
namespace fs_new {

namespace {

std::mutex& cache_mutex()
{
	static std::mutex s_mutex;
	return s_mutex;
}

std::unordered_map<std::string, std::string>& resolved_path_cache()
{
	static std::unordered_map<std::string, std::string> s_cache;
	return s_cache;
}

std::string to_ascii_lower( std::string_view text )
{
	std::string out( text );
	std::transform( out.begin(), out.end(), out.begin(), []( unsigned char c ) {
		return static_cast<char>( std::tolower( c ) );
	} );
	return out;
}

bool ascii_iequals( std::string_view a, std::string_view b )
{
	if ( a.size() != b.size() )
		return false;
	for ( std::size_t i = 0; i < a.size(); ++i ) {
		if ( std::tolower( static_cast<unsigned char>( a[i] ) ) !=
			 std::tolower( static_cast<unsigned char>( b[i] ) ) )
			return false;
	}
	return true;
}

std::string path_to_utf8( std::filesystem::path const& p )
{
	auto const u8 = p.u8string();
	return std::string( reinterpret_cast<char const*>( u8.data() ), u8.size() );
}

std::string normalize_cache_key( std::filesystem::path const& dir, std::string_view leaf_lower )
{
	std::string key = to_ascii_lower( path_to_utf8( dir ) );
	std::replace( key.begin(), key.end(), '\\', '/' );
	while ( key.size() > 1 && key.back() == '/' )
		key.pop_back();
	if ( !key.empty() && key.back() != '/' )
		key.push_back( '/' );
	key.append( leaf_lower );
	return key;
}

bool resolve_single_segment(
	std::filesystem::path const& current_dir,
	std::string_view segment,
	std::filesystem::path& out_next
)
{
	std::string const segment_lower = to_ascii_lower( segment );
	std::string const key = normalize_cache_key( current_dir, segment_lower );

	{
		std::lock_guard<std::mutex> lock( cache_mutex() );
		auto const& cache = resolved_path_cache();
		if ( auto it = cache.find( key ); it != cache.end() ) {
			std::filesystem::path cached = std::filesystem::path( reinterpret_cast<char8_t const*>( it->second.c_str() ) );
			std::error_code ec;
			if ( std::filesystem::exists( cached, ec ) ) {
				out_next = std::move( cached );
				return true;
			}
		}
	}

	std::error_code ec;
	if ( !std::filesystem::is_directory( current_dir, ec ) )
		return false;

	for ( auto const& entry : std::filesystem::directory_iterator( current_dir, std::filesystem::directory_options::skip_permission_denied, ec ) ) {
		std::string const entry_name = path_to_utf8( entry.path().filename() );
		if ( ascii_iequals( entry_name, segment ) ) {
			out_next = entry.path();
			std::string const resolved_utf8 = path_to_utf8( out_next );
			std::lock_guard<std::mutex> lock( cache_mutex() );
			resolved_path_cache()[key] = resolved_utf8;
			return true;
		}
	}

	return false;
}

std::vector<std::string> split_segments( std::string_view relative_path )
{
	std::vector<std::string> segments;
	std::size_t start = 0;
	while ( start < relative_path.size() ) {
		std::size_t const next = relative_path.find_first_of( "/\\", start );
		std::string_view token = ( next == std::string_view::npos )
			? relative_path.substr( start )
			: relative_path.substr( start, next - start );

		if ( !token.empty() && token != "." ) {
			if ( token == ".." ) {
				if ( segments.empty() )
					return {};
				segments.pop_back();
			} else {
				segments.emplace_back( token );
			}
		}

		if ( next == std::string_view::npos )
			break;
		start = next + 1;
	}
	return segments;
}

} // namespace

bool resolve_path_case_insensitive_segments(
	std::filesystem::path const& root,
	std::string_view relative_path,
	std::filesystem::path& out_resolved
)
{
	std::error_code ec;
	if ( !std::filesystem::exists( root, ec ) )
		return false;

	if ( relative_path.empty() ) {
		out_resolved = root;
		return true;
	}

	auto const segments = split_segments( relative_path );
	if ( segments.empty() )
		return false;

	std::filesystem::path current = root;
	for ( auto const& segment : segments ) {
		std::filesystem::path next_path;
		if ( !resolve_single_segment( current, segment, next_path ) )
			return false;
		current = std::move( next_path );
	}

	out_resolved = std::move( current );
	return true;
}

bool resolve_path_case_insensitive(
	std::filesystem::path const& requested,
	std::filesystem::path& out_resolved
)
{
	if ( requested.empty() )
		return false;

	std::error_code ec;
	if ( std::filesystem::exists( requested, ec ) ) {
		out_resolved = requested;
		return true;
	}

	std::filesystem::path probe = requested.lexically_normal();
	std::vector<std::string> trailing_segments;

	while ( !probe.empty() ) {
		if ( std::filesystem::exists( probe, ec ) )
			break;

		auto const filename = path_to_utf8( probe.filename() );
		if ( !filename.empty() && filename != "." )
			trailing_segments.push_back( filename );

		auto const parent = probe.parent_path();
		if ( parent == probe )
			break;
		probe = parent;
	}

	if ( probe.empty() || !std::filesystem::exists( probe, ec ) )
		return false;

	std::filesystem::path current = probe;
	for ( auto it = trailing_segments.rbegin(); it != trailing_segments.rend(); ++it ) {
		std::filesystem::path next_path;
		if ( !resolve_single_segment( current, *it, next_path ) )
			return false;
		current = std::move( next_path );
	}

	out_resolved = std::move( current );
	return true;
}

void clear_case_insensitive_path_cache()
{
	std::lock_guard<std::mutex> lock( cache_mutex() );
	resolved_path_cache().clear();
}

} // namespace fs_new
} // namespace xray
