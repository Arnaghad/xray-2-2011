////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Cached case-insensitive path resolver for case-sensitive filesystems
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_FS_CASE_INSENSITIVE_PATH_H_INCLUDED
#define XRAY_FS_CASE_INSENSITIVE_PATH_H_INCLUDED

#include <xray/fs/api.h>

#include <filesystem>
#include <string_view>

namespace xray {
namespace fs_new {

/**
 * Resolves `relative_path` starting from existing directory `root` by matching each path segment
 * case-insensitively against actual on-disk directory entries, returning the canonical on-disk casing.
 */
XRAY_FS_API bool	resolve_path_case_insensitive_segments(
						std::filesystem::path const& root,
						std::string_view relative_path,
						std::filesystem::path& out_resolved
					);

/**
 * Resolves `requested` path to an existing on-disk path. Returns immediately if `requested` exists
 * verbatim; otherwise searches from the deepest existing ancestor directory case-insensitively.
 */
XRAY_FS_API bool	resolve_path_case_insensitive(
						std::filesystem::path const& requested,
						std::filesystem::path& out_resolved
					);

/**
 * Clears the internal resolved-path lookup cache (intended for tests or filesystem remounts).
 */
XRAY_FS_API void	clear_case_insensitive_path_cache();

} // namespace fs_new
} // namespace xray

#endif // #ifndef XRAY_FS_CASE_INSENSITIVE_PATH_H_INCLUDED
