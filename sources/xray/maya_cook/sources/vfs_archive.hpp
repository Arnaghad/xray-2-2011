#pragma once

#include <cstdint>
#include <filesystem>
#include <map>
#include <string>
#include <vector>

namespace xray::maya_cook::detail {

/// Build an X-Ray VFS pack archive (`little-endian\0\0\0`) compatible with
/// `xray::render::vulkan::original::read_export_archive`.
std::vector<std::uint8_t> build_vfs_archive(std::map<std::string, std::vector<std::uint8_t>> const& files);

void write_file_bytes(std::filesystem::path const& path, std::vector<std::uint8_t> const& bytes);
void write_file_text(std::filesystem::path const& path, std::string_view text);

} // namespace xray::maya_cook::detail

