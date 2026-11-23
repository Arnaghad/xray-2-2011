#include "animation_cooker.hpp"

#include "bspline_fit.hpp"
#include "lua_and_binary_config.hpp"
#include "model_cook_common.hpp"
#include "vfs_archive.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <fstream>
#include <iostream>
#include <numbers>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace xray::maya_cook::detail {

namespace {

constexpr std::array<char const*, 9> k_channel_names = {
    "tx", "ty", "tz", "rx", "ry", "rz", "sx", "sy", "sz"
};

struct anim_frame {
    float3 t;
    float3 r;
    float3 s{1.0f, 1.0f, 1.0f};
};

struct mat44 {
    std::array<std::array<double, 4>, 4> m{};

    static mat44 identity() {
        mat44 r;
        r.m[0][0] = r.m[1][1] = r.m[2][2] = r.m[3][3] = 1.0;
        return r;
    }

    static mat44 from_frame(anim_frame const& f) {
        double cx = std::cos(f.r.x), sx = std::sin(f.r.x);
        double cy = std::cos(f.r.y), sy = std::sin(f.r.y);
        double cz = std::cos(f.r.z), sz = std::sin(f.r.z);

        mat44 r = identity();
        // Match xray::math::create_scale(s) * create_rotation(r) * create_translation(t)
        r.m[0][0] = f.s.x * (cy * cz);
        r.m[0][1] = f.s.x * (-cy * sz);
        r.m[0][2] = f.s.x * (sy);

        r.m[1][0] = f.s.y * (sy * sx * cz + cx * sz);
        r.m[1][1] = f.s.y * (-sx * sy * sz + cx * cz);
        r.m[1][2] = f.s.y * (-sx * cy);

        r.m[2][0] = f.s.z * (-cx * sy * cz + sx * sz);
        r.m[2][1] = f.s.z * (cx * sy * sz + sx * cz);
        r.m[2][2] = f.s.z * (cx * cy);

        r.m[3][0] = f.t.x;
        r.m[3][1] = f.t.y;
        r.m[3][2] = f.t.z;
        return r;
    }

    mat44 mul(mat44 const& rhs) const {
        mat44 out;
        for (int i = 0; i < 4; ++i) {
            for (int j = 0; j < 4; ++j) {
                double sum = 0.0;
                for (int k = 0; k < 4; ++k) sum += m[i][k] * rhs.m[k][j];
                out.m[i][j] = sum;
            }
        }
        return out;
    }

    mat44 invert_affine() const {
        double a00 = m[0][0], a01 = m[0][1], a02 = m[0][2];
        double a10 = m[1][0], a11 = m[1][1], a12 = m[1][2];
        double a20 = m[2][0], a21 = m[2][1], a22 = m[2][2];
        double det = a00 * (a11 * a22 - a12 * a21)
                   - a01 * (a10 * a22 - a12 * a20)
                   + a02 * (a10 * a21 - a11 * a20);
        if (std::abs(det) < 1e-12) {
            throw std::runtime_error("Singular root transform in animation set_difference");
        }
        double inv = 1.0 / det;
        mat44 out = identity();
        out.m[0][0] =  inv * (a11 * a22 - a12 * a21);
        out.m[0][1] = -inv * (a01 * a22 - a02 * a21);
        out.m[0][2] =  inv * (a01 * a12 - a02 * a11);
        out.m[1][0] = -inv * (a10 * a22 - a12 * a20);
        out.m[1][1] =  inv * (a00 * a22 - a02 * a20);
        out.m[1][2] = -inv * (a00 * a12 - a02 * a10);
        out.m[2][0] =  inv * (a10 * a21 - a11 * a20);
        out.m[2][1] = -inv * (a00 * a21 - a01 * a20);
        out.m[2][2] =  inv * (a00 * a11 - a01 * a10);
        out.m[3][0] = -(m[3][0] * out.m[0][0] + m[3][1] * out.m[1][0] + m[3][2] * out.m[2][0]);
        out.m[3][1] = -(m[3][0] * out.m[0][1] + m[3][1] * out.m[1][1] + m[3][2] * out.m[2][1]);
        out.m[3][2] = -(m[3][0] * out.m[0][2] + m[3][1] * out.m[1][2] + m[3][2] * out.m[2][2]);
        return out;
    }

    anim_frame to_frame() const {
        anim_frame f;
        f.t = {static_cast<float>(m[3][0]), static_cast<float>(m[3][1]), static_cast<float>(m[3][2])};
        double sx = std::sqrt(m[0][0] * m[0][0] + m[0][1] * m[0][1] + m[0][2] * m[0][2]);
        double sy = std::sqrt(m[1][0] * m[1][0] + m[1][1] * m[1][1] + m[1][2] * m[1][2]);
        double sz = std::sqrt(m[2][0] * m[2][0] + m[2][1] * m[2][1] + m[2][2] * m[2][2]);
        f.s = {static_cast<float>(sx), static_cast<float>(sy), static_cast<float>(sz)};
        double r00 = (sx > 1e-8) ? m[0][0] / sx : 1.0;
        double r01 = (sx > 1e-8) ? m[0][1] / sx : 0.0;
        double r02 = (sx > 1e-8) ? m[0][2] / sx : 0.0;
        double r10 = (sy > 1e-8) ? m[1][0] / sy : 0.0;
        double r11 = (sy > 1e-8) ? m[1][1] / sy : 1.0;
        double r12 = (sy > 1e-8) ? m[1][2] / sy : 0.0;
        double r22 = (sz > 1e-8) ? m[2][2] / sz : 1.0;
        // Keep the small cosine near gimbal lock. The old 0.99999 sine
        // threshold discarded real rotations by up to ~0.009 radians.
        double const cy = std::hypot(r00,r01);
        double ay = std::atan2(r02,cy);
        double ax = 0.0;
        double az = 0.0;
        if (cy > 1e-10) {
            ax = std::atan2(-r12, r22);
            az = std::atan2(-r01, r00);
        } else {
            ax = 0.0;
            az = std::atan2(r10, r11);
        }
        f.r = {static_cast<float>(ax), static_cast<float>(ay), static_cast<float>(az)};
        return f;
    }
};

float wrap_angle_near(float angle, float reference) {
    constexpr float two_pi = 2.0f * std::numbers::pi_v<float>;
    float diff = angle - reference;
    diff -= std::round(diff / two_pi) * two_pi;
    return reference + diff;
}

void unwrap_rotation_channels(
    std::vector<float>& rx,
    std::vector<float>& ry,
    std::vector<float>& rz
) {
    if (rx.size() <= 1) return;
    constexpr float pi = std::numbers::pi_v<float>;
    for (std::size_t i = 1; i < rx.size(); ++i) {
        // Direct 2*pi unwrap vs pi-equivalent Euler flip (x+pi, pi-y, z+pi): pick smaller step
        float cx0 = wrap_angle_near(rx[i], rx[i - 1]);
        float cy0 = wrap_angle_near(ry[i], ry[i - 1]);
        float cz0 = wrap_angle_near(rz[i], rz[i - 1]);
        float d0 = std::abs(cx0 - rx[i - 1]) + std::abs(cy0 - ry[i - 1]) + std::abs(cz0 - rz[i - 1]);

        float cx1 = wrap_angle_near(rx[i] + pi, rx[i - 1]);
        float cy1 = wrap_angle_near(pi - ry[i], ry[i - 1]);
        float cz1 = wrap_angle_near(rz[i] + pi, rz[i - 1]);
        float d1 = std::abs(cx1 - rx[i - 1]) + std::abs(cy1 - ry[i - 1]) + std::abs(cz1 - rz[i - 1]);

        if (d1 + 1e-4f < d0) {
            rx[i] = cx1;
            ry[i] = cy1;
            rz[i] = cz1;
        } else {
            rx[i] = cx0;
            ry[i] = cy0;
            rz[i] = cz0;
        }
    }
}

void subtract_root_initial_pose(std::array<std::vector<float>, 9>& ch) {
    std::size_t n = ch[0].size();
    if (n == 0) return;
    anim_frame f0{
        {ch[0][0], ch[1][0], ch[2][0]},
        {ch[3][0], ch[4][0], ch[5][0]},
        {ch[6][0], ch[7][0], ch[8][0]},
    };
    mat44 inv0 = mat44::from_frame(f0).invert_affine();
    for (std::size_t i = 0; i < n; ++i) {
        anim_frame fi{
            {ch[0][i], ch[1][i], ch[2][i]},
            {ch[3][i], ch[4][i], ch[5][i]},
            {ch[6][i], ch[7][i], ch[8][i]},
        };
        anim_frame rel = mat44::from_frame(fi).mul(inv0).to_frame();
        ch[0][i] = rel.t.x;
        ch[1][i] = rel.t.y;
        ch[2][i] = rel.t.z;
        ch[3][i] = rel.r.x;
        ch[4][i] = rel.r.y;
        ch[5][i] = rel.r.z;
        ch[6][i] = rel.s.x;
        ch[7][i] = rel.s.y;
        ch[8][i] = rel.s.z;
    }
    unwrap_rotation_channels(ch[3], ch[4], ch[5]);
}

} // namespace

cook_result cook_animation(
    json_value const& manifest,
    std::span<std::uint8_t const> payload,
    std::filesystem::path const& output_dir,
    std::filesystem::path const& converted_dir
) {
    if (output_dir.empty()) {
        throw std::runtime_error("output_dir is required");
    }

    coord_transform const xform = parse_coord_transform(manifest);
    std::string const& logical_name = manifest.require("name").as_string();

    double fps = 30.0;
    if (auto const* v = manifest.find("fps")) fps = v->as_double();
    if (!(fps > 0.0) || !std::isfinite(fps)) {
        throw std::runtime_error("animation.fps must be positive finite");
    }

    std::uint32_t frame_count = manifest.require("frame_count").as_u32();
    if (frame_count < 1 || frame_count > 1000000) {
        throw std::runtime_error("animation.frame_count out of bounds");
    }

    bool is_additive = false;
    if (auto const* v = manifest.find("is_additive")) is_additive = v->as_bool();

    bool root_already_relative = !xform.right_handed();
    if (auto const* v = manifest.find("root_already_relative")) {
        root_already_relative = v->as_bool();
    }

    float prec_t = 0.005f;
    float prec_r = 0.005f;
    float prec_s = 0.005f;
    if (auto const* v = manifest.find("precision_t")) prec_t = static_cast<float>(v->as_double());
    if (auto const* v = manifest.find("precision_r")) prec_r = static_cast<float>(v->as_double());
    if (auto const* v = manifest.find("precision_s")) prec_s = static_cast<float>(v->as_double());
    if (!(prec_t > 0.0f) || !(prec_r > 0.0f) || !(prec_s > 0.0f)) {
        throw std::runtime_error("Animation precision tolerances must be positive");
    }

    auto const& tracks_arr = manifest.require("tracks").as_array();
    if (tracks_arr.empty() || tracks_arr.size() > 128) {
        throw std::runtime_error("animation.tracks must contain 1..128 bones");
    }

    float const dt = static_cast<float>(30.0 / fps);

    config_value root_cfg = config_value::make_table({}, false);
    config_value splines_cfg = config_value::make_table("splines", false);
    config_value bone_names_cfg = config_value::make_table("bone_names", true);
    config_value data_cfg = config_value::make_table("data", true);

    std::set<std::string> seen_bones;
    for (std::size_t ti = 0; ti < tracks_arr.size(); ++ti) {
        auto const& tr = tracks_arr[ti];
        std::string const& bname = tr.require("bone_name").as_string();
        validate_safe_segment(bname, "animation.bone_name", 127);
        if (!seen_bones.insert(bname).second) {
            throw std::runtime_error("Duplicate animation bone: " + bname);
        }
        bone_names_cfg.children.push_back(config_value::make_string({}, bname));

        auto const& ch_obj = tr.require("channels");
        std::array<std::vector<float>, 9> ch{};
        for (std::size_t ci = 0; ci < 9; ++ci) {
            ch[ci] = read_f32_span(ch_obj.require(k_channel_names[ci]), payload, frame_count);
        }

        // Convert complete TRS samples, including nonuniform scale axis ordering.
        for (std::uint32_t fi = 0; fi < frame_count; ++fi) {
            float3 t = xform.pos({ch[0][fi], ch[1][fi], ch[2][fi]});
            float3 r = xform.euler({ch[3][fi], ch[4][fi], ch[5][fi]});
            float3 s = xform.scaling({ch[6][fi], ch[7][fi], ch[8][fi]});
            ch[0][fi] = t.x;
            ch[1][fi] = t.y;
            ch[2][fi] = t.z;
            ch[3][fi] = r.x;
            ch[4][fi] = r.y;
            ch[5][fi] = r.z;
            ch[6][fi] = s.x;
            ch[7][fi] = s.y;
            ch[8][fi] = s.z;
        }

        if (xform.swap_yz && root_already_relative) {
            unwrap_rotation_channels(ch[3], ch[4], ch[5]);
        }
        if (!root_already_relative) {
            unwrap_rotation_channels(ch[3], ch[4], ch[5]);
            if (ti == 0 && bname == "object_movement") {
                subtract_root_initial_pose(ch);
            }
        }

        config_value bone_data_cfg = config_value::make_table({}, true);
        for (std::size_t ci = 0; ci < 9; ++ci) {
            float eps = (ci < 3) ? prec_t : (ci < 6 ? prec_r : prec_s);
            auto fitted = fit_cubic_bspline_channel(ch[ci], dt, eps);
            config_value ch_cfg = config_value::make_table({}, true);
            for (auto const& [knot, val] : fitted.clip_pairs) {
                config_value pt = config_value::make_table({}, true);
                pt.children.push_back(config_value::make_float({}, knot));
                pt.children.push_back(config_value::make_float({}, val));
                ch_cfg.children.push_back(std::move(pt));
            }
            bone_data_cfg.children.push_back(std::move(ch_cfg));
        }
        data_cfg.children.push_back(std::move(bone_data_cfg));
    }

    splines_cfg.set(std::move(bone_names_cfg));
    splines_cfg.set(std::move(data_cfg));
    splines_cfg.set(config_value::make_bool("is_additive", is_additive));
    root_cfg.set(std::move(splines_cfg));

    auto targets = resolve_single_file_targets(output_dir, converted_dir, logical_name, ".clip");
    std::string clip_lua = serialize_lua_config(root_cfg);
    write_file_text(targets.raw_file, clip_lua);
    if (!targets.converted_file.empty()) {
        write_file_bytes(targets.converted_file, serialize_binary_config(std::move(root_cfg)));
    }

    cook_result result;
    result.asset_kind = "animation";
    result.logical_name = logical_name;
    result.output_dir = targets.raw_file;
    result.converted_dir = targets.converted_file;
    result.bone_count = static_cast<std::uint32_t>(tracks_arr.size());
    result.frame_count = frame_count;
    return result;
}

} // namespace xray::maya_cook::detail

namespace xray::maya_cook {

void verify_clip_samples(std::filesystem::path const& clip_samples_path) {
    using namespace detail;
    auto read_local_text = [](std::filesystem::path const& path) -> std::string {
        std::ifstream is(path, std::ios::binary);
        if (!is) throw std::runtime_error("Cannot open file: " + path.string());
        return std::string((std::istreambuf_iterator<char>(is)), {});
    };
    std::filesystem::path clip_path = clip_samples_path;
    clip_path.replace_extension(".clip");
    std::filesystem::path precision_path = clip_samples_path;
    precision_path.replace_extension(".clip_precision");

    std::string const samples_text = read_local_text(clip_samples_path);
    std::string const clip_text = read_local_text(clip_path);

    std::array<double, 9> prec{};
    prec.fill(0.005 + 2e-6);
    if (std::filesystem::is_regular_file(precision_path)) {
        std::istringstream ps(read_local_text(precision_path));
        for (double& v : prec) {
            if (!(ps >> v) || !(v > 0.0)) {
                throw std::runtime_error("Invalid tolerance in " + precision_path.string());
            }
        }
    }

    std::istringstream ss(samples_text);
    std::size_t exp_bones = 0;
    std::size_t exp_frames = 0;
    double dt = 0.0;
    int exp_additive = 0;
    if (!(ss >> exp_bones >> exp_frames >> dt >> exp_additive) || exp_bones == 0 || exp_frames == 0 || !(dt > 0.0)) {
        throw std::runtime_error("Invalid header in " + clip_samples_path.string());
    }

    config_value const root_cfg = parse_lua_config(clip_text);
    config_value const* splines = root_cfg.find("splines");
    if (!splines) {
        throw std::runtime_error("Missing splines table in " + clip_path.string());
    }
    config_value const* is_add_node = splines->find("is_additive");
    bool const clip_additive = is_add_node && is_add_node->number != 0.0;
    if (clip_additive != (exp_additive != 0)) {
        throw std::runtime_error("Clip additive flag disagrees with expected samples");
    }
    config_value const* bone_names_node = splines->find("bone_names");
    config_value const* data_node = splines->find("data");
    if (!bone_names_node || !data_node || bone_names_node->children.size() != data_node->children.size()) {
        throw std::runtime_error("Invalid bone_names/data tables in " + clip_path.string());
    }

    struct channel_spline {
        std::vector<float> knots;
        std::vector<float> coefs;
    };
    std::vector<std::string> clip_bones;
    std::vector<std::array<channel_spline, 9>> clip_splines;
    clip_bones.reserve(bone_names_node->children.size());
    clip_splines.reserve(data_node->children.size());

    for (std::size_t bi = 0; bi < bone_names_node->children.size(); ++bi) {
        clip_bones.push_back(bone_names_node->children[bi].str);
        auto const& bnode = data_node->children[bi];
        if (bnode.children.size() != 9) {
            throw std::runtime_error("Expected 9 channels for bone " + clip_bones.back());
        }
        std::array<channel_spline, 9> arr{};
        for (std::size_t ci = 0; ci < 9; ++ci) {
            auto const& pairs = bnode.children[ci].children;
            if (pairs.size() < 5) {
                throw std::runtime_error("Truncated spline in " + clip_bones.back());
            }
            arr[ci].knots.reserve(pairs.size() + 3);
            arr[ci].coefs.reserve(pairs.size() - 1);
            for (std::size_t pi = 0; pi < pairs.size(); ++pi) {
                auto const& pt = pairs[pi].children;
                if (pt.size() < 2) {
                    throw std::runtime_error("Malformed spline pair in " + clip_bones.back());
                }
                arr[ci].knots.push_back(static_cast<float>(pt[0].number));
                if (pi + 1 < pairs.size()) {
                    arr[ci].coefs.push_back(static_cast<float>(pt[1].number));
                }
            }
            float const last_knot = arr[ci].knots.back();
            arr[ci].knots.push_back(last_knot);
            arr[ci].knots.push_back(last_knot);
            arr[ci].knots.push_back(last_knot);
        }
        clip_splines.push_back(std::move(arr));
    }

    double max_t_err = 0.0;
    double max_r_err = 0.0;
    double max_s_err = 0.0;

    for (std::size_t bi = 0; bi < exp_bones; ++bi) {
        std::string bname;
        if (!(ss >> bname)) {
            throw std::runtime_error("Missing bone name in " + clip_samples_path.string());
        }
        auto it = std::find(clip_bones.begin(), clip_bones.end(), bname);
        if (it == clip_bones.end()) {
            throw std::runtime_error("Clip missing expected bone: " + bname);
        }
        std::size_t const clip_idx = static_cast<std::size_t>(std::distance(clip_bones.begin(), it));
        auto const& sp = clip_splines[clip_idx];

        for (std::size_t fi = 0; fi < exp_frames; ++fi) {
            double exp[9]{};
            for (double& v : exp) {
                if (!(ss >> v)) {
                    throw std::runtime_error("Truncated frame sample for " + bname);
                }
            }
            float const t_param = static_cast<float>(static_cast<double>(fi) * dt);
            float eval[9]{};
            for (std::size_t ci = 0; ci < 9; ++ci) {
                eval[ci] = xray::animation_math::evaluate_cubic_bspline(sp[ci].knots, sp[ci].coefs, t_param);
            }
            for (std::size_t k = 0; k < 3; ++k) {
                double const err = std::abs(static_cast<double>(eval[k]) - exp[k]);
                max_t_err = std::max(max_t_err, err);
                if (err > prec[k]) {
                    throw std::runtime_error("Translation mismatch on " + bname + " frame " + std::to_string(fi) +
                                             " err=" + std::to_string(err) + " tol=" + std::to_string(prec[k]));
                }
            }
            for (std::size_t k = 0; k < 3; ++k) {
                double const err = std::abs(static_cast<double>(eval[6 + k]) - exp[6 + k]);
                max_s_err = std::max(max_s_err, err);
                if (err > prec[6 + k]) {
                    throw std::runtime_error("Scale mismatch on " + bname + " frame " + std::to_string(fi) +
                                             " err=" + std::to_string(err) + " tol=" + std::to_string(prec[6 + k]));
                }
            }
            anim_frame fe{ {0, 0, 0}, {eval[3], eval[4], eval[5]}, {1, 1, 1} };
            anim_frame fx{ {0, 0, 0}, {static_cast<float>(exp[3]), static_cast<float>(exp[4]), static_cast<float>(exp[5])}, {1, 1, 1} };
            mat44 const re = mat44::from_frame(fe);
            mat44 const rx = mat44::from_frame(fx);
            mat44 rdiff = mat44::identity();
            for (int r = 0; r < 3; ++r) {
                for (int c = 0; c < 3; ++c) {
                    double sum = 0.0;
                    for (int k = 0; k < 3; ++k) sum += rx.m[r][k] * re.m[c][k];
                    rdiff.m[r][c] = sum;
                }
            }
            double const trace = rdiff.m[0][0] + rdiff.m[1][1] + rdiff.m[2][2];
            double const cos_ang = std::clamp((trace - 1.0) * 0.5, -1.0, 1.0);
            double const ang = std::acos(cos_ang);
            anim_frame const delta = rdiff.to_frame();
            double const r_errs[3] = {
                std::abs(static_cast<double>(delta.r.x)),
                std::abs(static_cast<double>(delta.r.y)),
                std::abs(static_cast<double>(delta.r.z)),
            };
            max_r_err = std::max(max_r_err, ang);
            if (r_errs[0] > prec[3] && r_errs[1] > prec[4] && r_errs[2] > prec[5] &&
                ang > std::max({prec[3], prec[4], prec[5]})) {
                throw std::runtime_error("Rotation mismatch on " + bname + " frame " + std::to_string(fi) +
                                         " angle=" + std::to_string(ang));
            }
            if (r_errs[0] > prec[3] || r_errs[1] > prec[4] || r_errs[2] > prec[5]) {
                throw std::runtime_error("Rotation axis mismatch on " + bname + " frame " + std::to_string(fi) +
                                         " angle=" + std::to_string(ang));
            }
        }
    }
    std::cout << "Clip playback verification PASS: " << exp_bones << " bones, " << exp_frames
              << " frames, max_t=" << max_t_err << ", max_r=" << max_r_err << ", max_s=" << max_s_err << '\n';
}

} // namespace xray::maya_cook

