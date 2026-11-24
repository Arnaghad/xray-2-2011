#include "skinned_model_cooker.hpp"

#include "lua_and_binary_config.hpp"
#include "model_cook_common.hpp"
#include "vfs_archive.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <map>
#include <set>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace xray::maya_cook::detail {

namespace {

struct bind_bone {
    std::string name;
    float3 translation;
    float3 rotation;
    float3 scale{1.0f, 1.0f, 1.0f};
};

struct input_skinned_surface {
    std::string name;
    std::string mesh_name;
    std::string shader_name;
    std::string texture_name;
    std::string material_name;
    std::string lod = "LOD0";
    std::vector<float3> positions;
    std::vector<float3> normals;
    std::vector<float2> uvs;
    std::vector<float3> tangents;
    std::vector<float3> binormals;
    std::vector<std::array<std::uint16_t, 4>> bone_indices;
    std::vector<std::array<float, 4>> bone_weights;
    std::vector<std::uint32_t> indices;
    bool has_tangents = false;
};

struct cooked_skinned_surface {
    surface_meta meta;
    std::vector<vert_boned_4w> vertices;
    std::vector<std::uint16_t> indices;
};

void normalize_vertex_weights(
    std::array<std::uint16_t, 4> const& raw_idx,
    std::array<float, 4> const& raw_w,
    std::size_t bone_count,
    std::array<std::uint16_t, 4>& out_idx,
    std::array<float, 3>& out_w3
) {
    struct inf_pair {
        std::uint16_t bone = 0xFFFFu;
        float weight = 0.0f;
    };
    std::array<inf_pair, 4> pairs{};
    std::size_t n = 0;
    for (std::size_t i = 0; i < 4; ++i) {
        std::uint16_t bi = raw_idx[i];
        float w = raw_w[i];
        if (!std::isfinite(w) || w < 0.0f) {
            throw std::runtime_error("Skinned vertex weight must be non-negative finite");
        }
        if (bi == 0xFFFFu || w <= 1e-6f) continue;
        if (bi >= bone_count) {
            throw std::runtime_error("Skinned vertex bone index exceeds bind pose bone count");
        }
        bool merged = false;
        for (std::size_t k = 0; k < n; ++k) {
            if (pairs[k].bone == bi) {
                pairs[k].weight += w;
                merged = true;
                break;
            }
        }
        if (!merged) {
            pairs[n++] = {bi, w};
        }
    }
    if (n == 0) {
        throw std::runtime_error("Skinned vertex has zero non-empty bone influences");
    }
    for (std::size_t i = 1; i < n && i < pairs.size(); ++i) {
        inf_pair key = pairs[i];
        std::size_t j = i;
        while (j > 0) {
            auto const& prev = pairs[j - 1];
            bool const key_before_prev =
                (key.weight != prev.weight) ? (key.weight > prev.weight) : (key.bone < prev.bone);
            if (!key_before_prev) break;
            pairs[j] = prev;
            --j;
        }
        pairs[j] = key;
    }

    double sum = 0.0;
    for (std::size_t i = 0; i < n; ++i) sum += pairs[i].weight;
    if (!(sum > 1e-6)) {
        throw std::runtime_error("Skinned vertex total weight too small");
    }

    out_idx = {0xFFFFu, 0xFFFFu, 0xFFFFu, 0xFFFFu};
    out_w3 = {0.0f, 0.0f, 0.0f};
    double w_accum = 0.0;
    for (std::size_t i = 0; i < n; ++i) {
        out_idx[i] = pairs[i].bone;
        if (i < 3) {
            float nw = static_cast<float>(pairs[i].weight / sum);
            if (w_accum + nw > 1.0) {
                nw = static_cast<float>(std::max(0.0, 1.0 - w_accum));
            }
            out_w3[i] = nw;
            w_accum += nw;
        }
    }
}

class skinned_dedup_table {
public:
    std::vector<vert_boned_4w> verts;
    std::vector<std::uint16_t> indices;

    std::uint16_t add_vertex(vert_boned_4w const& cand) {
        float sqm = cand.P.squared_length();
        auto key_lo = std::lower_bound(
            m_lookup.begin(), m_lookup.end(), sqm - 0.1f,
            [](lookup_entry const& e, float val) { return e.sqm < val; }
        );
        for (auto it = key_lo; it != m_lookup.end(); ++it) {
            if (it->sqm > sqm + 0.1f) break;
            auto const& v = verts[it->vid];
            if (v.P.is_similar(cand.P) && v.N.is_similar(cand.N) && v.uv.is_similar(cand.uv) &&
                v.m == cand.m &&
                std::abs(v.w[0] - cand.w[0]) < 1e-4f &&
                std::abs(v.w[1] - cand.w[1]) < 1e-4f &&
                std::abs(v.w[2] - cand.w[2]) < 1e-4f) {
                return it->vid;
            }
        }
        if (verts.size() >= 65535u) {
            throw std::runtime_error("Skinned vertex bucket exceeded 16-bit index limit");
        }
        std::uint16_t vid = static_cast<std::uint16_t>(verts.size());
        verts.push_back(cand);
        lookup_entry entry{sqm, vid};
        auto pos = std::lower_bound(
            m_lookup.begin(), m_lookup.end(), entry,
            [](lookup_entry const& a, lookup_entry const& b) { return a.sqm < b.sqm; }
        );
        m_lookup.insert(pos, entry);
        return vid;
    }

private:
    struct lookup_entry {
        float sqm;
        std::uint16_t vid;
    };
    std::vector<lookup_entry> m_lookup;
};

std::vector<cooked_skinned_surface> cook_single_skinned_surface(
    input_skinned_surface const& in,
    std::size_t bone_count,
    coord_transform const& xform,
    cook_options const& options,
    std::uint32_t max_verts_limit
) {
    std::uint32_t limit = std::clamp<std::uint32_t>(max_verts_limit, 500u, 65530u);
    for (;;) {
        std::vector<cooked_skinned_surface> out;
        std::size_t tri_count = in.indices.size() / 3;
        std::size_t ti = 0;
        std::uint32_t split_idx = 0;
        bool overflow = false;

        while (ti < tri_count) {
            skinned_dedup_table chunk;
            while (ti < tri_count && chunk.verts.size() + 3 <= limit) {
                std::uint32_t raw_i[3] = {
                    in.indices[ti * 3 + 0],
                    in.indices[ti * 3 + (xform.flip_winding ? 2 : 1)],
                    in.indices[ti * 3 + (xform.flip_winding ? 1 : 2)],
                };
                for (int c = 0; c < 3; ++c) {
                    std::uint32_t vi = raw_i[c];
                    vert_boned_4w v{};
                    v.P = xform.pos(in.positions[vi]);
                    v.N = xform.dir(in.normals[vi]).normalized_safe({0.0f, 1.0f, 0.0f});
                    v.uv = xform.uv(in.uvs[vi]);
                    v.T = in.has_tangents ? xform.dir(in.tangents[vi]).normalized_safe({1.0f, 0.0f, 0.0f}) : float3{};
                    v.B = in.has_tangents ? xform.dir(in.binormals[vi]).normalized_safe({0.0f, 0.0f, 1.0f}) : float3{};
                    normalize_vertex_weights(in.bone_indices[vi], in.bone_weights[vi], bone_count, v.m, v.w);
                    std::uint16_t idx = chunk.add_vertex(v);
                    chunk.indices.push_back(idx);
                }
                ++ti;
            }
            if (!finalize_surface_chunk(chunk.verts, chunk.indices, options.preserve_input_tangents && in.has_tangents, options)) {
                overflow = true;
                break;
            }
            cooked_skinned_surface cs;
            cs.meta.save_name = sanitize_surface_name(in.shader_name.empty() ? in.name : in.shader_name, split_idx++);
            cs.meta.mesh_name = in.mesh_name;
            cs.meta.texture_name = in.texture_name;
            cs.meta.material_name = in.material_name;
            cs.meta.lod = in.lod;
            cs.vertices = std::move(chunk.verts);
            cs.indices = std::move(chunk.indices);
            cs.meta.vertex_count = static_cast<std::uint32_t>(cs.vertices.size());
            cs.meta.index_count = static_cast<std::uint32_t>(cs.indices.size());
            for (auto const& v : cs.vertices) {
                cs.meta.bounds.modify(v.P);
            }
            out.push_back(std::move(cs));
        }

        if (!overflow) {
            return out;
        }
        if (limit <= 1000u) {
            throw std::runtime_error("Skinned surface exceeded mender limit even after backoff: " + in.name);
        }
        limit -= 500u;
    }
}

std::vector<std::uint8_t> serialize_bind_pose(std::vector<bind_bone> const& bones) {
    std::vector<std::uint8_t> out;
    append_le<std::uint16_t>(out, static_cast<std::uint16_t>(bones.size()));
    for (auto const& b : bones) {
        out.insert(out.end(), b.name.begin(), b.name.end());
        out.push_back(0);
        append_le(out, b.translation.x);
        append_le(out, b.translation.y);
        append_le(out, b.translation.z);
        append_le(out, b.rotation.x);
        append_le(out, b.rotation.y);
        append_le(out, b.rotation.z);
        append_le(out, b.scale.x);
        append_le(out, b.scale.y);
        append_le(out, b.scale.z);
    }
    return out;
}

std::vector<std::uint8_t> serialize_skinned_vertices(std::vector<vert_boned_4w> const& verts) {
    std::vector<std::uint8_t> out;
    out.reserve(4 + verts.size() * sizeof(vert_boned_4w));
    append_le<std::uint32_t>(out, static_cast<std::uint32_t>(verts.size()));
    auto const* raw = reinterpret_cast<std::uint8_t const*>(verts.data());
    out.insert(out.end(), raw, raw + verts.size() * sizeof(vert_boned_4w));
    return out;
}

} // namespace

cook_result cook_skinned_model(
    json_value const& manifest,
    std::span<std::uint8_t const> payload,
    std::filesystem::path const& output_dir,
    std::filesystem::path const& converted_dir,
    std::filesystem::path const& archive_path,
    cook_options const& options
) {
    if (output_dir.empty()) {
        throw std::runtime_error("output_dir is required");
    }

    coord_transform const xform = parse_coord_transform(manifest);
    cook_options const eff = resolve_effective_options(manifest, options);

    std::string const& skeleton_name = manifest.require("skeleton_name").as_string();
    if (skeleton_name.empty() || skeleton_name.size() > 220 || skeleton_name.find("..") != std::string::npos) {
        throw std::runtime_error("Invalid skeleton_name in skinned_model manifest");
    }

    auto const& bones_arr = manifest.require("bones").as_array();
    if (bones_arr.empty() || bones_arr.size() > 128) {
        throw std::runtime_error("skinned_model bones must contain 1..128 entries");
    }

    std::vector<bind_bone> bones;
    bones.reserve(bones_arr.size());
    std::set<std::string> bone_names;
    for (auto const& bnode : bones_arr) {
        bind_bone b;
        b.name = bnode.require("name").as_string();
        validate_safe_segment(b.name, "bone.name", 127);
        if (!bone_names.insert(b.name).second) {
            throw std::runtime_error("Duplicate bind-pose bone name: " + b.name);
        }
        b.translation = xform.pos(read_json_vec3(bnode.require("bind_translation"), "bind_translation"));
        b.rotation = xform.euler(read_json_vec3(bnode.require("bind_rotation"), "bind_rotation"));
        b.scale = xform.scaling(read_json_vec3(bnode.require("bind_scale"), "bind_scale"));
        if (b.scale.x <= 0.0f || b.scale.y <= 0.0f || b.scale.z <= 0.0f) {
            throw std::runtime_error("Bind-pose bone scale must be positive: " + b.name);
        }
        bones.push_back(std::move(b));
    }

    auto const& surf_arr = manifest.require("surfaces").as_array();
    if (surf_arr.empty() || surf_arr.size() > 128) {
        throw std::runtime_error("surfaces must contain 1..128 items");
    }

    std::vector<cooked_skinned_surface> cooked_surfaces;
    std::vector<surface_meta> surface_metas;
    std::set<std::string> unique_surface_names;
    aabb model_bbox;

    for (auto const& snode : surf_arr) {
        input_skinned_surface in;
        in.name = snode.require("name").as_string();
        validate_safe_segment(in.name, "surface.name");
        if (auto const* v = snode.find("mesh_name")) in.mesh_name = v->as_string();
        if (auto const* v = snode.find("shader_name")) in.shader_name = v->as_string();
        if (auto const* v = snode.find("texture_name")) in.texture_name = v->as_string();
        if (auto const* v = snode.find("material_name")) in.material_name = v->as_string();
        if (auto const* v = snode.find("lod")) in.lod = v->as_string();

        std::uint32_t vc = snode.require("vertex_count").as_u32();
        std::uint32_t ic = snode.require("index_count").as_u32();
        if (vc < 3 || vc > 2000000 || ic < 3 || ic % 3 != 0) {
            throw std::runtime_error("Skinned surface counts out of valid triangle bounds: " + in.name);
        }
        auto const& streams = snode.require("streams");
        in.positions = read_f32x3_span(streams.require("positions"), payload, vc);
        in.normals = read_f32x3_span(streams.require("normals"), payload, vc);
        in.uvs = read_f32x2_span(streams.require("uvs"), payload, vc);
        in.indices = read_index_span(streams.require("indices"), payload, ic, vc);
        in.bone_indices = read_u16x4_span(streams.require("bone_indices"), payload, vc);
        in.bone_weights = read_f32x4_span(streams.require("bone_weights"), payload, vc);
        if (auto const* ts = streams.find("tangents")) {
            if (auto const* bs = streams.find("binormals")) {
                in.tangents = read_f32x3_span(*ts, payload, vc);
                in.binormals = read_f32x3_span(*bs, payload, vc);
                in.has_tangents = true;
            }
        }

        auto chunks = cook_single_skinned_surface(in, bones.size(), xform, eff, eff.max_verts_per_surface);
        for (auto& cs_item : chunks) {
            if (!unique_surface_names.insert(cs_item.meta.save_name).second) {
                throw std::runtime_error("Duplicate cooked surface name: " + cs_item.meta.save_name);
            }
            model_bbox.modify(cs_item.meta.bounds);
            surface_metas.push_back(cs_item.meta);
            cooked_surfaces.push_back(std::move(cs_item));
        }
    }

    auto locators = parse_locators(manifest, xform, bones.size());

    cook_result result;
    result.asset_kind = "skinned_model";
    result.logical_name = manifest.require("name").as_string();
    result.output_dir = output_dir;
    result.converted_dir = converted_dir;
    result.archive_path = archive_path;
    result.locator_count = static_cast<std::uint32_t>(locators.size());
    result.bone_count = static_cast<std::uint32_t>(bones.size());

    std::map<std::string, std::vector<std::uint8_t>> archive_files;

    // 1. settings & skeleton ref
    config_value settings_tree = build_or_merge_settings(
        output_dir / "settings", surface_metas, manifest.find("rigid_body"), options.preserve_existing_settings
    );
    std::string settings_lua = serialize_lua_config(settings_tree);
    write_file_text(output_dir / "settings", settings_lua);
    archive_files["settings"] = std::vector<std::uint8_t>(settings_lua.begin(), settings_lua.end());
    if (!converted_dir.empty()) {
        write_file_bytes(converted_dir / "settings", serialize_binary_config(settings_tree));
    }

    config_value skel_ref = config_value::make_table({}, false);
    skel_ref.set(config_value::make_string("skeleton", skeleton_name));
    std::string skel_lua = serialize_lua_config(skel_ref);
    write_file_text(output_dir / "skeleton", skel_lua);
    archive_files["skeleton"] = std::vector<std::uint8_t>(skel_lua.begin(), skel_lua.end());
    if (!converted_dir.empty()) {
        write_file_bytes(converted_dir / "skeleton", serialize_binary_config(skel_ref));
    }

    // 2. render/bind_pose & render/export_properties (mt_skeletal_mesh = 40)
    auto bind_pose_bytes = serialize_bind_pose(bones);
    write_file_bytes(output_dir / "render" / "bind_pose", bind_pose_bytes);
    archive_files["render/bind_pose"] = bind_pose_bytes;
    if (!converted_dir.empty()) {
        write_file_bytes(converted_dir / "render" / "bind_pose", bind_pose_bytes);
    }

    config_value root_props_tree = build_export_properties_tree(40, model_bbox, &locators);
    std::string root_props_lua = serialize_lua_config(root_props_tree);
    write_file_text(output_dir / "render" / "export_properties", root_props_lua);
    archive_files["render/export_properties"] = std::vector<std::uint8_t>(root_props_lua.begin(), root_props_lua.end());
    if (!converted_dir.empty()) {
        write_file_bytes(converted_dir / "render" / "export_properties", serialize_binary_config(root_props_tree));
    }

    // 3. Per-surface skeletal geometry (submesh type = 47)
    for (auto const& s : cooked_surfaces) {
        std::filesystem::path sdir = output_dir / "render" / s.meta.save_name;
        config_value sprops = build_export_properties_tree(47, s.meta.bounds, nullptr, &s.meta);
        std::string sprops_lua = serialize_lua_config(sprops);
        auto v_bytes = serialize_skinned_vertices(s.vertices);
        auto i_bytes = serialize_surface_indices(s.indices);

        write_file_text(sdir / "export_properties", sprops_lua);
        write_file_bytes(sdir / "vertices", v_bytes);
        write_file_bytes(sdir / "indices", i_bytes);

        archive_files["render/" + s.meta.save_name + "/export_properties"] =
            std::vector<std::uint8_t>(sprops_lua.begin(), sprops_lua.end());
        archive_files["render/" + s.meta.save_name + "/vertices"] = v_bytes;
        archive_files["render/" + s.meta.save_name + "/indices"] = i_bytes;

        if (!converted_dir.empty()) {
            std::filesystem::path cdir = converted_dir / "render" / s.meta.save_name;
            write_file_bytes(cdir / "export_properties", serialize_binary_config(sprops));
            write_file_bytes(cdir / "converted_model", convert_skeletal_surface_to_vulkan_blob(v_bytes, i_bytes));
        }

        result.surfaces.push_back(cook_surface_summary{
            s.meta.save_name,
            !s.meta.material_name.empty() ? s.meta.material_name : s.meta.texture_name,
            s.meta.texture_name,
            static_cast<std::uint32_t>(s.vertices.size()),
            static_cast<std::uint32_t>(s.indices.size()),
        });
    }

    if (!archive_path.empty()) {
        write_file_bytes(archive_path, build_vfs_archive(archive_files));
    }

    return result;
}

} // namespace xray::maya_cook::detail

