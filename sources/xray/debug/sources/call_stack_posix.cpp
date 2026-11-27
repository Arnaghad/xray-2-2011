////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: POSIX / Linux call-stack capture & symbolization
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <cxxabi.h>
#include <dlfcn.h>
#include <execinfo.h>
#include <string>

#if defined(__has_include)
#	if __has_include(<stacktrace>)
#		include <stacktrace>
#	endif
#endif

#include <xray/debug/call_stack.h>
#include <xray/debug/extensions.h>

namespace {

constexpr u32 k_max_stack_frames = 512;

struct resolved_frame {
	std::string module_name;
	std::string function_name;
};

char const* basename_or_full( char const* const path )
{
	if ( !path || !*path )
		return "?";
	char const* const slash = std::strrchr( path, '/' );
	return slash ? ( slash + 1 ) : path;
}

resolved_frame resolve_address_dladdr( void* const addr )
{
	resolved_frame out;
	out.module_name = "?";
	out.function_name = "?";

	if ( !addr )
		return out;

	Dl_info info = {};
	if ( ::dladdr( addr, &info ) == 0 )
		return out;

	if ( info.dli_fname && *info.dli_fname )
		out.module_name = basename_or_full( info.dli_fname );

	if ( info.dli_sname && *info.dli_sname ) {
		int status = -1;
		char* demangled = abi::__cxa_demangle( info.dli_sname, nullptr, nullptr, &status );
		if ( status == 0 && demangled ) {
			out.function_name = demangled;
		} else {
			out.function_name = info.dli_sname;
		}
		std::free( demangled );
	}

	return out;
}

u32 fnv1a_hash_pointers( void* const* const ptrs, u32 const count )
{
	u32 hash = 2166136261u;
	for ( u32 i = 0; i < count; ++i ) {
		std::uintptr_t val = reinterpret_cast<std::uintptr_t>( ptrs[i] );
		for ( unsigned b = 0; b < sizeof( val ); ++b ) {
			hash ^= static_cast<u32>( val & 0xFFu );
			hash *= 16777619u;
			val >>= 8;
		}
	}
	return hash;
}

void iterate_raw_frames(
	void* const* const frames,
	u32 const frame_count,
	xray::debug::call_stack::Callback const& callback,
	bool const invert_order
)
{
	if ( frame_count == 0 )
		return;

	int const start = invert_order ? static_cast<int>( frame_count ) - 1 : 0;
	int const end = invert_order ? -1 : static_cast<int>( frame_count );
	int const step = invert_order ? -1 : 1;

	for ( int i = start; i != end; i += step ) {
		void* const addr = frames[i];
		resolved_frame const resolved = resolve_address_dladdr( addr );
		bool const keep_going = callback(
			static_cast<u32>( i ),
			frame_count,
			resolved.module_name.c_str(),
			"?",
			-1,
			resolved.function_name.c_str(),
			static_cast<size_t>( reinterpret_cast<std::uintptr_t>( addr ) )
		);
		if ( !keep_going )
			break;
	}
}

} // namespace

namespace xray {
namespace debug {
namespace call_stack {

void iterate(
	_EXCEPTION_POINTERS* const pointers,
	void* call_stack[],
	Callback const& callback,
	bool const invert_order
)
{
	XRAY_UNREFERENCED_PARAMETER( pointers );

	if ( !callback )
		return;

	if ( call_stack ) {
		u32 count = 0;
		while ( count < k_max_stack_frames && call_stack[count] != nullptr )
			++count;
		iterate_raw_frames( call_stack, count, callback, invert_order );
		return;
	}

#if defined(__cpp_lib_stacktrace) && __cpp_lib_stacktrace >= 202011L
	auto const st = std::stacktrace::current( 1 );
	if ( !st.empty() ) {
		u32 const frame_count = static_cast<u32>( st.size() );
		int const start = invert_order ? static_cast<int>( frame_count ) - 1 : 0;
		int const end = invert_order ? -1 : static_cast<int>( frame_count );
		int const step = invert_order ? -1 : 1;

		for ( int i = start; i != end; i += step ) {
			auto const& entry = st[static_cast<std::size_t>( i )];
			std::uintptr_t const raw_addr = static_cast<std::uintptr_t>( entry.native_handle() );
			void* const ptr_addr = reinterpret_cast<void*>( raw_addr );

			resolved_frame const dl = resolve_address_dladdr( ptr_addr );
			std::string file = entry.source_file();
			int const line = entry.source_line() > 0 ? static_cast<int>( entry.source_line() ) : -1;
			std::string desc = entry.description();
			if ( desc.empty() )
				desc = dl.function_name;
			if ( file.empty() )
				file = "?";

			bool const keep_going = callback(
				static_cast<u32>( i ),
				frame_count,
				dl.module_name.c_str(),
				file.c_str(),
				line,
				desc.c_str(),
				static_cast<size_t>( raw_addr )
			);
			if ( !keep_going )
				break;
		}
		return;
	}
#endif

	void* frames[256] = {};
	int const captured = ::backtrace( frames, 256 );
	if ( captured <= 0 )
		return;

	u32 const skip = captured > 1 ? 1u : 0u;
	iterate_raw_frames( frames + skip, static_cast<u32>( captured ) - skip, callback, invert_order );
}

void get_stack_trace(
	void* stacktrace[],
	u32 const stacktrace_max,
	u32 const num_to_capture,
	u32* const out_hash
)
{
	if ( !stacktrace || stacktrace_max == 0 ) {
		if ( out_hash )
			*out_hash = 0;
		return;
	}

	u32 const max_frames = std::min( num_to_capture, stacktrace_max - 1 );
	if ( max_frames == 0 ) {
		stacktrace[0] = nullptr;
		if ( out_hash )
			*out_hash = 0;
		return;
	}

	int const captured_raw = ::backtrace( stacktrace, static_cast<int>( max_frames ) );
	u32 const captured = captured_raw > 0 ? static_cast<u32>( captured_raw ) : 0u;
	stacktrace[captured] = nullptr;

	if ( out_hash )
		*out_hash = fnv1a_hash_pointers( stacktrace, captured );
}

void finalize_symbols()
{
}

} // namespace call_stack
} // namespace debug
} // namespace xray
