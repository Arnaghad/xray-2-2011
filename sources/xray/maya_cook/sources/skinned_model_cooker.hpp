#pragma once

#include "json_value.hpp"
#include "xray/maya_cook/cook.hpp"

#include <cstdint>
#include <filesystem>
#include <span>

namespace xray::maya_cook::detail {

cook_result cook_skinned_model(
    json_value const& manifest,
    std::span<std::uint8_t const> payload,
    std::filesystem::path const& output_dir,
    std::filesystem::path const& converted_dir,
    std::filesystem::path const& archive_path,
    cook_options const& options
);

} // namespace xray::maya_cook::detail

