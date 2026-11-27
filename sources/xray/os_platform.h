////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Unified cross-platform OS abstraction layer (PAL)
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_OS_PLATFORM_H_INCLUDED
#define XRAY_OS_PLATFORM_H_INCLUDED

#include <xray/macro_building.h>
#include <xray/macro_compiler.h>
#include <xray/macro_platform.h>
#include <xray/type_extensions.h>

#include <filesystem>
#include <string>
#include <vector>

namespace xray {
namespace memory {
	class base_allocator;
} // namespace memory

namespace os {

class XRAY_CORE_API dynamic_library
{
public:
	dynamic_library() noexcept = default;
	~dynamic_library() noexcept;

	dynamic_library( dynamic_library const& ) = delete;
	dynamic_library& operator=( dynamic_library const& ) = delete;

	dynamic_library( dynamic_library&& other ) noexcept;
	dynamic_library& operator=( dynamic_library&& other ) noexcept;

	bool open( std::filesystem::path const& path, bool search_library_dir = false );
	bool open_existing( std::filesystem::path const& path );
	void close() noexcept;

	bool valid() const noexcept { return m_handle != nullptr; }
	explicit operator bool() const noexcept { return valid(); }
	void* raw_handle() const noexcept { return m_handle; }

	void* symbol( pcstr name ) const noexcept;

	template <typename Fn>
	Fn symbol_as( pcstr name ) const noexcept
	{
		return reinterpret_cast<Fn>( symbol( name ) );
	}

	std::string const& last_error() const noexcept { return m_last_error; }

	static std::string format_library_name( pcstr base_name, bool with_lib_prefix = false );

private:
	void*		m_handle = nullptr;
	bool		m_owned = false;
	std::string	m_last_error;
};

XRAY_CORE_API std::filesystem::path	executable_path();
XRAY_CORE_API std::filesystem::path	executable_directory();
XRAY_CORE_API bool					get_environment_variable( pcstr name, std::string& out_value );
XRAY_CORE_API u32					current_process_id() noexcept;

struct process_result
{
	int			exit_code = -1;
	bool		started = false;
	std::string	output;
	std::string	launch_error;
};

XRAY_CORE_API process_result run_process(
	std::filesystem::path const& executable,
	std::vector<std::string> const& args
);

struct system_memory_info
{
	u64 total_physical = 0;
	u64 available_physical = 0;
	u64 page_size = 4096;
	u64 allocation_granularity = 65536;
};

XRAY_CORE_API system_memory_info	query_system_memory() noexcept;

class XRAY_CORE_API scoped_com_init
{
public:
	explicit scoped_com_init( bool multithreaded = false ) noexcept;
	~scoped_com_init() noexcept;

	scoped_com_init( scoped_com_init const& ) = delete;
	scoped_com_init& operator=( scoped_com_init const& ) = delete;

private:
	bool m_initialized = false;
};

struct clipboard_provider
{
	bool (*is_empty)() = nullptr;
	void (*copy)( pcstr buffer ) = nullptr;
	pstr (*paste)( xray::memory::base_allocator& allocator ) = nullptr;
};

XRAY_CORE_API void						set_clipboard_provider( clipboard_provider const* provider ) noexcept;
XRAY_CORE_API clipboard_provider const*	get_clipboard_provider() noexcept;

} // namespace os
} // namespace xray

#endif // #ifndef XRAY_OS_PLATFORM_H_INCLUDED
