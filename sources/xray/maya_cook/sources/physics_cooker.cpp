#include "physics_cooker.hpp"

#include "collision_primitive_fitter.hpp"
#include "lua_and_binary_config.hpp"
#include "model_cook_common.hpp"
#include "exchange_schema.hpp"
#include "vfs_archive.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

namespace xray::maya_cook {

namespace {

constexpr double kMinExtentMeters = 0.0005; // 1 mm diameter / edge minimum, matches safety bounds

collision_shape_type parse_shape_type(std::string_view name) {
    if (name == "box") return collision_shape_type::box;
    if (name == "cylinder") return collision_shape_type::cylinder;
    if (name == "sphere") return collision_shape_type::sphere;
    throw std::runtime_error("Unsupported collision shape type: " + std::string(name));
}

char const* shape_type_name(collision_shape_type shape) {
    switch (shape) {
    case collision_shape_type::box: return "box";
    case collision_shape_type::cylinder: return "cylinder";
    case collision_shape_type::sphere: return "sphere";
    }
    return "box";
}

double vec_dot(detail::vec3d const& a, detail::vec3d const& b) {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

detail::vec3d vec_cross(detail::vec3d const& a, detail::vec3d const& b) {
    return {
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x,
    };
}

double vec_len(detail::vec3d const& v) {
    return std::sqrt(vec_dot(v, v));
}

detail::vec3d normalize_or(detail::vec3d const& v, detail::vec3d const& fallback) {
    double const len = vec_len(v);
    if (!(len > 1e-12) || !std::isfinite(len)) {
        return fallback;
    }
    return {v.x / len, v.y / len, v.z / len};
}

detail::vec3d vec_sub(detail::vec3d const& a, detail::vec3d const& b) {
    return {a.x - b.x, a.y - b.y, a.z - b.z};
}

detail::vec3d vec_mul(detail::vec3d const& a, double s) {
    return {a.x * s, a.y * s, a.z * s};
}

std::array<float, 3> xray_basis_to_euler(
    detail::vec3d u0,
    detail::vec3d u1,
    detail::vec3d u2
) {
    // Ensure orthonormal right-handed frame (i x j . k > 0) for X-Ray create_rotation
    u0 = normalize_or(u0, {1.0, 0.0, 0.0});
    u1 = normalize_or(vec_sub(u1, vec_mul(u0, vec_dot(u0, u1))), {0.0, 1.0, 0.0});
    if (std::abs(vec_dot(u0, u1)) > 0.1) {
        detail::vec3d const alt = std::abs(u0.y) < 0.9
            ? detail::vec3d{0.0, 1.0, 0.0}
            : detail::vec3d{0.0, 0.0, 1.0};
        u1 = normalize_or(vec_sub(alt, vec_mul(u0, vec_dot(u0, alt))), {0.0, 1.0, 0.0});
    }
    detail::vec3d right_z = vec_cross(u0, u1);
    if (vec_dot(right_z, u2) < 0.0) {
        u2 = vec_mul(right_z, -1.0);
    } else {
        u2 = normalize_or(right_z, right_z);
    }
    // Re-enforce u2 = u0 x u1 so det == +1 exactly
    u2 = vec_cross(u0, u1);

    double const sy_clamped = std::clamp(u0.z, -1.0, 1.0);
    double const ay = std::asin(sy_clamped);
    double ax = 0.0;
    double az = 0.0;
    if (std::abs(sy_clamped) < 0.99999) {
        ax = std::atan2(-u1.z, u2.z);
        az = std::atan2(-u0.y, u0.x);
    } else {
        ax = 0.0;
        az = std::atan2(u1.x, u1.y);
    }
    return {static_cast<float>(ax), static_cast<float>(ay), static_cast<float>(az)};
}

fitted_collision_primitive fit_xray_points_internal(
    collision_shape_type shape,
    std::vector<detail::vec3d> const& xray_pts,
    bool flip_z,
    float unit_scale,
    bool output_in_maya_space
) {
    fitted_collision_primitive out;
    out.shape = shape;

    std::array<float, 3> pos_xray{};
    std::array<float, 3> rot_xray{};
    std::array<float, 3> scale_xray{};

    if (shape == collision_shape_type::sphere) {
        detail::fitted_sphere const sphere = detail::fit_bounding_sphere(xray_pts);
        double const diam = std::max(2.0 * sphere.radius, 2.0 * kMinExtentMeters);
        pos_xray = {
            static_cast<float>(sphere.center.x),
            static_cast<float>(sphere.center.y),
            static_cast<float>(sphere.center.z),
        };
        rot_xray = {0.0f, 0.0f, 0.0f};
        scale_xray = {static_cast<float>(diam), static_cast<float>(diam), static_cast<float>(diam)};
    } else {
        detail::fitted_obb const box = detail::fit_oriented_box(xray_pts);
        pos_xray = {
            static_cast<float>(box.center.x),
            static_cast<float>(box.center.y),
            static_cast<float>(box.center.z),
        };
        if (shape == collision_shape_type::box) {
            detail::vec3d u0 = normalize_or(box.axis[0], {1.0, 0.0, 0.0});
            detail::vec3d u1 = normalize_or(box.axis[1], {0.0, 1.0, 0.0});
            detail::vec3d u2 = normalize_or(box.axis[2], {0.0, 0.0, 1.0});
            if (vec_dot(vec_cross(u0, u1), u2) < 0.0) {
                u2 = vec_mul(u2, -1.0);
            }
            rot_xray = xray_basis_to_euler(u0, u1, u2);
            scale_xray = {
                static_cast<float>(std::max(2.0 * std::abs(box.extent[0]), 2.0 * kMinExtentMeters)),
                static_cast<float>(std::max(2.0 * std::abs(box.extent[1]), 2.0 * kMinExtentMeters)),
                static_cast<float>(std::max(2.0 * std::abs(box.extent[2]), 2.0 * kMinExtentMeters)),
            };
        } else {
            // Cylinder local Y is long axis, diameter in XZ uses middle extent per legacy set_cylinder_matrix
            std::array<int, 3> order{0, 1, 2};
            std::sort(order.begin(), order.end(), [&](int a, int b) {
                return std::abs(box.extent[a]) < std::abs(box.extent[b]);
            });
            int const min_i = order[0];
            int const mid_i = order[1];
            int const max_i = order[2];

            detail::vec3d const u_y = normalize_or(box.axis[max_i], {0.0, 1.0, 0.0});
            detail::vec3d u_x = vec_sub(box.axis[min_i], vec_mul(u_y, vec_dot(u_y, box.axis[min_i])));
            u_x = normalize_or(u_x, {1.0, 0.0, 0.0});
            detail::vec3d const u_z = normalize_or(vec_cross(u_x, u_y), {0.0, 0.0, 1.0});

            rot_xray = xray_basis_to_euler(u_x, u_y, u_z);
            double const diam = std::max(2.0 * std::abs(box.extent[mid_i]), 2.0 * kMinExtentMeters);
            double const height = std::max(2.0 * std::abs(box.extent[max_i]), 2.0 * kMinExtentMeters);
            scale_xray = {static_cast<float>(diam), static_cast<float>(height), static_cast<float>(diam)};
        }
    }

    if (output_in_maya_space) {
        double const inv_scale = 1.0 / static_cast<double>(unit_scale);
        out.position[0] = static_cast<float>(pos_xray[0] * inv_scale);
        out.position[1] = static_cast<float>(pos_xray[1] * inv_scale);
        out.position[2] = static_cast<float>((flip_z ? -pos_xray[2] : pos_xray[2]) * inv_scale);

        out.rotation[0] = rot_xray[0];
        out.rotation[1] = rot_xray[1];
        out.rotation[2] = flip_z ? -rot_xray[2] : rot_xray[2];

        out.scale[0] = static_cast<float>(scale_xray[0] * inv_scale);
        out.scale[1] = static_cast<float>(scale_xray[1] * inv_scale);
        out.scale[2] = static_cast<float>(scale_xray[2] * inv_scale);
    } else {
        for (int i = 0; i < 3; ++i) {
            out.position[i] = pos_xray[i];
            out.rotation[i] = rot_xray[i];
            out.scale[i] = scale_xray[i];
        }
    }
    return out;
}

std::string json_escape(std::string_view s) {
    std::string out = "\"";
    for (char c : s) {
        switch (c) {
        case '\\': out += "\\\\"; break;
        case '"': out += "\\\""; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default: out.push_back(c); break;
        }
    }
    out.push_back('"');
    return out;
}

std::string read_text_file(std::filesystem::path const& path) {
    std::ifstream is(path, std::ios::binary);
    if (!is) throw std::runtime_error("Cannot open exchange manifest: " + path.string());
    return std::string((std::istreambuf_iterator<char>(is)), {});
}

std::vector<std::uint8_t> read_binary_file(std::filesystem::path const& path) {
    std::error_code ec;
    auto sz = std::filesystem::file_size(path, ec);
    constexpr std::size_t kMaxPayload = 64u * 1024u * 1024u;
    if (ec || sz > kMaxPayload) {
        throw std::runtime_error("Exchange payload missing or exceeds 64 MiB limit: " + path.string());
    }
    std::ifstream is(path, std::ios::binary);
    if (!is) throw std::runtime_error("Cannot open exchange payload: " + path.string());
    std::vector<std::uint8_t> buf(static_cast<std::size_t>(sz));
    if (!buf.empty()) {
        is.read(reinterpret_cast<char*>(buf.data()), static_cast<std::streamsize>(buf.size()));
    }
    if (!is) throw std::runtime_error("Failed reading exchange payload: " + path.string());
    return buf;
}

std::vector<detail::vec3d> convert_points_to_fitter(
    std::vector<detail::float3> const& pts,
    detail::coord_transform const& xform
) {
    std::vector<detail::vec3d> out;
    out.reserve(pts.size());
    for (auto const& raw : pts) {
        detail::float3 const p = xform.pos(raw);
        out.push_back({static_cast<double>(p.x), static_cast<double>(p.y), static_cast<double>(p.z)});
    }
    return out;
}

detail::config_value build_dof_table(std::string name, detail::json_value const& dof_json) {
    detail::config_value tbl = detail::config_value::make_table(std::move(name), false);
    tbl.set(detail::config_value::make_bool("enable_state", dof_json.require("enable_state").as_bool()));
    tbl.set(detail::config_value::make_bool("limits_enable_state", dof_json.require("limits_enable_state").as_bool()));
    tbl.set(detail::config_value::make_float("low_limit", static_cast<float>(dof_json.require("low_limit").as_double())));
    tbl.set(detail::config_value::make_float("hi_limit", static_cast<float>(dof_json.require("hi_limit").as_double())));
    return tbl;
}

} // namespace

fitted_collision_primitive fit_collision_primitive(
    collision_shape_type shape,
    std::span<float const> xyz_points,
    bool input_is_maya_right_handed,
    float unit_scale_to_meters,
    bool output_in_maya_space
) {
    if (xyz_points.size() < 3 || xyz_points.size() % 3 != 0) {
        throw std::runtime_error("fit_collision_primitive requires non-empty xyz_points divisible by 3");
    }
    if (!(unit_scale_to_meters > 0.0f) || !std::isfinite(unit_scale_to_meters)) {
        throw std::runtime_error("unit_scale_to_meters must be positive finite");
    }

    std::size_t const count = xyz_points.size() / 3;
    std::vector<detail::vec3d> pts;
    pts.reserve(count);
    for (std::size_t i = 0; i < count; ++i) {
        float const x = xyz_points[i * 3 + 0];
        float const y = xyz_points[i * 3 + 1];
        float const z = xyz_points[i * 3 + 2];
        if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(z)) {
            throw std::runtime_error("Non-finite point passed to fit_collision_primitive");
        }
        double const sx = static_cast<double>(x) * static_cast<double>(unit_scale_to_meters);
        double const sy = static_cast<double>(y) * static_cast<double>(unit_scale_to_meters);
        double const sz = (input_is_maya_right_handed ? -static_cast<double>(z) : static_cast<double>(z)) *
            static_cast<double>(unit_scale_to_meters);
        pts.push_back({sx, sy, sz});
    }

    return fit_xray_points_internal(
        shape, pts, input_is_maya_right_handed, unit_scale_to_meters, output_in_maya_space
    );
}

std::string fit_collision_bundle_to_json(std::filesystem::path const& exchange_dir) {
    auto manifest = detail::parse_json(read_text_file(exchange_dir / "manifest.json"));
    detail::validate_exchange_header(manifest);
    bool const v2 = manifest.require("schema").as_string() == "xray-dcc-exchange/v2";
    if (manifest.require("asset_kind").as_string() != "physics") {
        throw std::runtime_error("fit_collision_bundle_to_json requires asset_kind == 'physics'");
    }
    auto payload = read_binary_file(exchange_dir / "payload.bin");
    detail::coord_transform const xform = detail::parse_coord_transform(manifest);

    auto const& requests = manifest.require("fit_requests").as_array();
    std::ostringstream os;
    os << std::setprecision(9);
    os << "{\"results\":[";
    for (std::size_t ri = 0; ri < requests.size(); ++ri) {
        auto const& req = requests[ri];
        std::string const& name = req.require("name").as_string();
        collision_shape_type const shape = parse_shape_type(req.require("shape").as_string());
        auto const& pts_span = req.require("points");
        std::uint32_t const count = pts_span.require("count").as_u32();
        if (count == 0) {
            throw std::runtime_error("fit_request.points count must be > 0");
        }
        auto raw_pts = detail::read_f32x3_span(pts_span, payload, count);
        auto fit_pts = convert_points_to_fitter(raw_pts, xform);

        fitted_collision_primitive source_fit = fit_xray_points_internal(
            shape, fit_pts, xform.flip_z, xform.scale, true
        );
        fitted_collision_primitive const xray_fit = fit_xray_points_internal(
            shape, fit_pts, xform.flip_z, xform.scale, false
        );
        if (v2) {
            auto const p = xform.inverse_pos({xray_fit.position[0], xray_fit.position[1], xray_fit.position[2]});
            auto const r = xform.inverse_euler({xray_fit.rotation[0], xray_fit.rotation[1], xray_fit.rotation[2]});
            auto const s = xform.inverse_dimensions({xray_fit.scale[0], xray_fit.scale[1], xray_fit.scale[2]});
            std::ranges::copy(std::array{p.x, p.y, p.z}, source_fit.position);
            std::ranges::copy(std::array{r.x, r.y, r.z}, source_fit.rotation);
            std::ranges::copy(std::array{s.x, s.y, s.z}, source_fit.scale);
        }

        if (ri > 0) os << ',';
        os << "{\"name\":" << json_escape(name)
           << ",\"type\":" << json_escape(shape_type_name(shape))
           << (v2 ? ",\"source\":{" : ",\"maya\":{")
           << "\"position\":[" << source_fit.position[0] << ',' << source_fit.position[1] << ',' << source_fit.position[2] << "],"
           << "\"rotation\":[" << source_fit.rotation[0] << ',' << source_fit.rotation[1] << ',' << source_fit.rotation[2] << "],"
           << "\"scale\":[" << source_fit.scale[0] << ',' << source_fit.scale[1] << ',' << source_fit.scale[2] << "]}"
           << ",\"xray\":{"
           << "\"position\":[" << xray_fit.position[0] << ',' << xray_fit.position[1] << ',' << xray_fit.position[2] << "],"
           << "\"rotation\":[" << xray_fit.rotation[0] << ',' << xray_fit.rotation[1] << ',' << xray_fit.rotation[2] << "],"
           << "\"scale\":[" << xray_fit.scale[0] << ',' << xray_fit.scale[1] << ',' << xray_fit.scale[2] << "]}"
           << '}';
    }
    os << "]}\n";
    return os.str();
}

namespace detail {

cook_result cook_physics(
    json_value const& manifest,
    std::span<std::uint8_t const> payload,
    std::filesystem::path const& output_dir,
    std::filesystem::path const& converted_dir
) {
    std::string const& logical_name = manifest.require("name").as_string();
    validate_safe_segment(
        std::filesystem::path(logical_name).filename().string(),
        "physics.name"
    );
    coord_transform const xform = parse_coord_transform(manifest);

    config_value root = config_value::make_table({}, false);
    config_value bodies_tbl = config_value::make_table("bodies", true);
    config_value joints_tbl = config_value::make_table("joints", true);

    std::uint32_t body_count = 0;
    std::uint32_t joint_count = 0;

    if (auto const* bodies_node = manifest.find("bodies")) {
        auto const& bodies = bodies_node->as_array();
        if (bodies.size() > 256) {
            throw std::runtime_error("Physics bodies exceed 256 limit");
        }
        body_count = static_cast<std::uint32_t>(bodies.size());

        for (auto const& b_json : bodies) {
            std::string const& b_name = b_json.require("name").as_string();
            validate_safe_segment(b_name, "physics.body.name");
            std::string const anim_joint = b_json.find("anim_joint")
                ? b_json.find("anim_joint")->as_string()
                : std::string{};
            float const mass = static_cast<float>(b_json.require("mass").as_double());
            if (!(mass >= 0.0f) || !std::isfinite(mass)) {
                throw std::runtime_error("Physics body mass must be non-negative finite");
            }
            float3 const mass_center = xform.pos(read_json_vec3(b_json.require("mass_center"), "mass_center"));
            float3 const pos = xform.pos(read_json_vec3(b_json.require("position"), "body.position"));
            float3 const rot = xform.euler(read_json_vec3(b_json.require("rotation"), "body.rotation"));
            float3 const scale = xform.scaling(read_json_vec3(b_json.require("scale"), "body.scale")) * xform.scale;

            config_value b_tbl = config_value::make_table({}, false);
            b_tbl.set(config_value::make_string("anim_joint", anim_joint));
            b_tbl.set(config_value::make_float("mass", mass));
            b_tbl.set(config_value::make_float3("mass_center", mass_center));
            b_tbl.set(config_value::make_string("name", b_name));
            b_tbl.set(config_value::make_float3("position", pos));
            b_tbl.set(config_value::make_float3("rotation", rot));
            b_tbl.set(config_value::make_float3("scale", scale));

            config_value col_tbl = config_value::make_table("collision", true);
            auto const& shapes = b_json.require("collision").as_array();
            for (auto const& s_json : shapes) {
                std::string const& type_str = s_json.require("type").as_string();
                collision_shape_type const shape = parse_shape_type(type_str);

                float3 c_pos{};
                float3 c_rot{};
                float3 c_scale{};
                if (auto const* pts_span = s_json.find("points")) {
                    std::uint32_t const count = pts_span->require("count").as_u32();
                    if (count == 0) {
                        throw std::runtime_error("Collision shape points count must be > 0");
                    }
                    auto raw_pts = read_f32x3_span(*pts_span, payload, count);
                    auto fit_pts = convert_points_to_fitter(raw_pts, xform);
                    fitted_collision_primitive const fit = fit_xray_points_internal(
                        shape, fit_pts, xform.flip_z, xform.scale, false
                    );
                    c_pos = {fit.position[0], fit.position[1], fit.position[2]};
                    c_rot = {fit.rotation[0], fit.rotation[1], fit.rotation[2]};
                    c_scale = {fit.scale[0], fit.scale[1], fit.scale[2]};
                } else {
                    c_pos = xform.pos(read_json_vec3(s_json.require("position"), "collision.position"));
                    c_rot = xform.euler(read_json_vec3(s_json.require("rotation"), "collision.rotation"));
                    c_scale = xform.scaling(read_json_vec3(s_json.require("scale"), "collision.scale")) * xform.scale;
                }

                config_value c_tbl = config_value::make_table({}, false);
                c_tbl.set(config_value::make_float3("position", c_pos));
                c_tbl.set(config_value::make_float3("rotation", c_rot));
                c_tbl.set(config_value::make_float3("scale", c_scale));
                c_tbl.set(config_value::make_string("type", type_str));
                col_tbl.children.push_back(std::move(c_tbl));
            }
            b_tbl.set(std::move(col_tbl));
            bodies_tbl.children.push_back(std::move(b_tbl));
        }
    }

    if (auto const* joints_node = manifest.find("joints")) {
        auto const& joints = joints_node->as_array();
        if (joints.size() > 512) {
            throw std::runtime_error("Physics joints exceed 512 limit");
        }
        joint_count = static_cast<std::uint32_t>(joints.size());

        static constexpr std::array<char const*, 6> kDofNames = {
            "x_translate", "y_translate", "z_translate",
            "x_rotate", "y_rotate", "z_rotate"
        };

        for (auto const& j_json : joints) {
            std::string const& j_name = j_json.require("name").as_string();
            validate_safe_segment(j_name, "physics.joint.name");
            std::string const& j_type = j_json.require("type").as_string();
            if (j_type != "fixed" && j_type != "ball" && j_type != "generic" &&
                j_type != "wheel" && j_type != "slider" && j_type != "rigid") {
                throw std::runtime_error("Unsupported physics joint type: " + j_type);
            }

            float3 const j_pos = xform.pos(read_json_vec3(j_json.require("position"), "joint.position"));
            float3 const j_rot = xform.euler(read_json_vec3(j_json.require("rotation"), "joint.rotation"));
            float3 const j_scale = xform.scaling(read_json_vec3(j_json.require("scale"), "joint.scale")) * xform.scale;

            config_value j_tbl = config_value::make_table({}, false);
            if (auto const* b0 = j_json.find("body0"); b0 && b0->is_string() && !b0->as_string().empty()) {
                j_tbl.set(config_value::make_string("body0", b0->as_string()));
            }
            if (auto const* b1 = j_json.find("body1"); b1 && b1->is_string() && !b1->as_string().empty()) {
                j_tbl.set(config_value::make_string("body1", b1->as_string()));
            }
            j_tbl.set(config_value::make_string("name", j_name));
            j_tbl.set(config_value::make_float3("position", j_pos));
            j_tbl.set(config_value::make_float3("rotation", j_rot));
            j_tbl.set(config_value::make_float3("scale", j_scale));
            j_tbl.set(config_value::make_string("type", j_type));

            for (char const* dof : kDofNames) {
                j_tbl.set(build_dof_table(dof, j_json.require(dof)));
            }
            joints_tbl.children.push_back(std::move(j_tbl));
        }
    }

    root.set(std::move(bodies_tbl));
    root.set(std::move(joints_tbl));

    std::string const lua_text = serialize_lua_config(root);
    std::string_view const ext = (output_dir.extension() == ".ph") ? ".ph" : ".physics";
    resolved_file_targets const targets = resolve_single_file_targets(
        output_dir, converted_dir, logical_name, ext
    );
    write_file_text(targets.raw_file, lua_text);

    if (!converted_dir.empty()) {
        std::vector<std::uint8_t> const bin_bytes = serialize_binary_config(std::move(root));
        write_file_bytes(targets.converted_file, bin_bytes);
    }

    cook_result res;
    res.asset_kind = "physics";
    res.logical_name = logical_name;
    res.output_dir = targets.raw_file;
    res.converted_dir = targets.converted_file;
    res.has_collision = body_count > 0;
    res.body_count = body_count;
    res.joint_count = joint_count;
    return res;
}

} // namespace detail
} // namespace xray::maya_cook
