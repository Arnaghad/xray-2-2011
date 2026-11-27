////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: OS-agnostic SPIR-V disk cache, SHA-256 keying & precompiled lookup
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_RHI_SHADER_DISK_CACHE_H_INCLUDED
#define XRAY_RENDER_RHI_SHADER_DISK_CACHE_H_INCLUDED

#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <xray/render/vulkan/library_linkage.h>

#include <array>
#include <filesystem>
#include <string>
#include <vector>

namespace xray {
namespace render {
namespace rhi {

struct include_probe
{
	std::string path_utf8;
	bool loaded = false;
	std::vector<unsigned char> bytes;
	bool operator==( include_probe const& ) const = default;
};

XRAY_RENDER_VULKAN_API std::filesystem::path path_from_utf8( std::string const& utf8 );
XRAY_RENDER_VULKAN_API std::string path_to_utf8( std::filesystem::path const& path );
XRAY_RENDER_VULKAN_API std::string normalize_path_utf8( std::filesystem::path const& path );

XRAY_RENDER_VULKAN_API include_probe probe_include_file( std::string const& path_utf8 );
XRAY_RENDER_VULKAN_API std::array<unsigned char, 32> hash_bytes( std::vector<unsigned char> const& bytes ) noexcept;

XRAY_RENDER_VULKAN_API std::string compute_spirv_cache_key(
	std::string const& source,
	std::string const& cwd_utf8,
	std::vector<std::string> const& args_utf8
);

XRAY_RENDER_VULKAN_API bool try_load_spirv_disk_cache(
	std::filesystem::path const& file_path,
	std::vector<include_probe>& out_includes,
	compilation_result& out_result
);

XRAY_RENDER_VULKAN_API void write_spirv_disk_cache(
	std::filesystem::path const& file_path,
	std::vector<include_probe> const& includes,
	std::vector<u32> const& bytecode
);

XRAY_RENDER_VULKAN_API bool try_load_precompiled_spv(
	std::filesystem::path const& spv_path,
	compilation_result& out_result
);

XRAY_RENDER_VULKAN_API std::filesystem::path resolve_precompiled_spv_path(
	std::filesystem::path const& precompiled_dir,
	std::string const& cache_key,
	std::filesystem::path const& source_path,
	shader_compile_options const& options
);

} // namespace rhi
} // namespace render
} // namespace xray

#endif // XRAY_RENDER_RHI_SHADER_DISK_CACHE_H_INCLUDED
