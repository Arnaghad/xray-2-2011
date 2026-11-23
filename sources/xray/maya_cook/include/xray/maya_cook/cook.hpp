#pragma once

#include <cstdint>
#include <filesystem>
#include <span>
#include <string>
#include <vector>

namespace xray::maya_cook {

enum class collision_shape_type {
    box,
    cylinder,
    sphere,
};

struct fitted_collision_primitive {
    collision_shape_type shape = collision_shape_type::box;
    float position[3]{};
    float rotation[3]{};
    float scale[3]{};
};

struct cook_options {
    bool optimize_mesh = true;
    bool optimize_uv = true;
    bool preserve_existing_settings = true;
    bool preserve_input_tangents = false;
    std::uint32_t max_verts_per_surface = 65500;
    bool high_quality_overdraw = true;
};

struct cook_surface_summary {
    std::string name;
    std::string material_name;
    std::string texture_name;
    std::uint32_t vertex_count = 0;
    std::uint32_t index_count = 0;
};

struct cook_result {
    std::string asset_kind;
    std::string logical_name;
    std::filesystem::path output_dir;
    std::filesystem::path converted_dir;
    std::filesystem::path archive_path;
    std::vector<cook_surface_summary> surfaces;
    bool has_collision = false;
    bool has_hq_collision = false;
    std::uint32_t locator_count = 0;
    std::uint32_t bone_count = 0;
    std::uint32_t frame_count = 0;
    std::uint32_t body_count = 0;
    std::uint32_t joint_count = 0;
};

/// Fit a minimal oriented box, Y-aligned cylinder, or minimal sphere around 3D points (Wm4).
fitted_collision_primitive fit_collision_primitive(
    collision_shape_type shape,
    std::span<float const> xyz_points,
    bool input_is_maya_right_handed = true,
    float unit_scale_to_meters = 0.01f,
    bool output_in_maya_space = false
);

/// Execute all `fit_requests` inside a physics `xray-maya-exchange/v1` bundle and return JSON.
std::string fit_collision_bundle_to_json(std::filesystem::path const& exchange_dir);

/// Cook an `xray-maya-exchange/v1` bundle directory into X-Ray engine assets.
cook_result cook_exchange_bundle(
    std::filesystem::path const& exchange_dir,
    std::filesystem::path const& output_dir,
    std::filesystem::path const& converted_dir = {},
    std::filesystem::path const& archive_path = {},
    cook_options const& options = {}
);

/// Verify a cooked `.clip` against sampled TRS poses in `<stem>.clip_samples` and tolerances in `<stem>.clip_precision`.
void verify_clip_samples(std::filesystem::path const& clip_samples_path);

} // namespace xray::maya_cook
