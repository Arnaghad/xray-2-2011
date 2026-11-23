#include "skeleton_cooker.hpp"

#include "lua_and_binary_config.hpp"
#include "model_cook_common.hpp"
#include "vfs_archive.hpp"

#include <set>
#include <stdexcept>
#include <string>

namespace xray::maya_cook::detail {

namespace {

config_value build_skeleton_table(
    json_value const& node,
    std::set<std::string>& seen_bones,
    unsigned depth
) {
    if (depth > 64) {
        throw std::runtime_error("Skeleton hierarchy exceeds max depth 64");
    }
    std::string const& name = node.require("name").as_string();
    validate_safe_segment(name, "skeleton.bone_name", 127);
    if (!seen_bones.insert(name).second) {
        throw std::runtime_error("Duplicate skeleton bone: " + name);
    }
    if (seen_bones.size() > 256) {
        throw std::runtime_error("Skeleton bone count exceeds 256 limit");
    }

    config_value tbl = config_value::make_table(name, false);
    if (auto const* children = node.find("children"); children && !children->is_null()) {
        for (auto const& ch : children->as_array()) {
            tbl.set(build_skeleton_table(ch, seen_bones, depth + 1));
        }
    }
    return tbl;
}

} // namespace

cook_result cook_skeleton(
    json_value const& manifest,
    std::filesystem::path const& output_dir,
    std::filesystem::path const& converted_dir
) {
    if (output_dir.empty()) {
        throw std::runtime_error("output_dir is required");
    }

    std::string const& logical_name = manifest.require("name").as_string();
    std::set<std::string> seen_bones;
    config_value root = config_value::make_table({}, false);
    root.set(build_skeleton_table(manifest.require("root"), seen_bones, 0));

    auto targets = resolve_single_file_targets(output_dir, converted_dir, logical_name, ".skeleton");
    std::string lua_text = serialize_lua_config(root);
    write_file_text(targets.raw_file, lua_text);
    if (!targets.converted_file.empty()) {
        write_file_bytes(targets.converted_file, serialize_binary_config(std::move(root)));
    }

    cook_result result;
    result.asset_kind = "skeleton";
    result.logical_name = logical_name;
    result.output_dir = targets.raw_file;
    result.converted_dir = targets.converted_file;
    result.bone_count = static_cast<std::uint32_t>(seen_bones.size());
    return result;
}

} // namespace xray::maya_cook::detail

