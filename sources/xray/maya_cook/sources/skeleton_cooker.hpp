#pragma once

#include "json_value.hpp"
#include "xray/maya_cook/cook.hpp"

#include <filesystem>

namespace xray::maya_cook::detail {

cook_result cook_skeleton(
    json_value const& manifest,
    std::filesystem::path const& output_dir,
    std::filesystem::path const& converted_dir
);

} // namespace xray::maya_cook::detail

