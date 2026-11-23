#include "static_model_cooker.hpp"

#include "lua_and_binary_config.hpp"
#include "model_cook_common.hpp"
#include "vfs_archive.hpp"

#include <algorithm>
#include <cstring>
#include <map>
#include <set>
#include <stdexcept>
#include <string>
#include <vector>

namespace xray::maya_cook::detail {

namespace {

struct input_surface {
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
    std::vector<std::uint32_t> indices;
    bool has_tangents = false;
};

struct cooked_surface {
    surface_meta meta;
    std::vector<static_vertex> vertices;
    std::vector<std::uint16_t> indices;
};

class vertex_dedup_table {
public:
    std::vector<static_vertex> verts;
    std::vector<std::uint16_t> indices;

    std::uint16_t add_vertex(float3 const& p, float3 const& n, float2 const& uv, float3 const& t = {}, float3 const& b = {}) {
        float sqm = p.squared_length();
        auto key_lo = std::lower_bound(
            m_lookup.begin(), m_lookup.end(), sqm - 0.1f,
            [](lookup_entry const& e, float val) { return e.sqm < val; }
        );
        for (auto it = key_lo; it != m_lookup.end(); ++it) {
            if (it->sqm > sqm + 0.1f) break;
            auto const& v = verts[it->vid];
            if (v.P.is_similar(p) && v.N.is_similar(n) && v.uv.is_similar(uv)) {
                return it->vid;
            }
        }
        if (verts.size() >= 65535u) {
            throw std::runtime_error("Vertex bucket exceeded 16-bit index limit during deduplication");
        }
        std::uint16_t vid = static_cast<std::uint16_t>(verts.size());
        verts.push_back(static_vertex{p, n, t, b, uv});
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

bool prepare_surface_chunk(
    vertex_dedup_table& table,
    bool preserve_input_tangents,
    cook_options const& options
) {
    return finalize_surface_chunk(table.verts, table.indices, preserve_input_tangents, options);
}

std::vector<cooked_surface> cook_single_input_surface(
    input_surface const& in,
    coord_transform const& xform,
    cook_options const& options,
    std::uint32_t max_verts_limit
) {
    std::uint32_t limit = std::clamp<std::uint32_t>(max_verts_limit, 500u, 65530u);
    for (;;) {
        std::vector<cooked_surface> out;
        std::size_t tri_count = in.indices.size() / 3;
        std::size_t ti = 0;
        std::uint32_t split_idx = 0;
        bool overflow = false;

        while (ti < tri_count) {
            vertex_dedup_table chunk;
            while (ti < tri_count && chunk.verts.size() + 3 <= limit) {
                std::uint32_t raw_i[3] = {
                    in.indices[ti * 3 + 0],
                    in.indices[ti * 3 + (xform.flip_winding ? 2 : 1)],
                    in.indices[ti * 3 + (xform.flip_winding ? 1 : 2)],
                };
                for (int c = 0; c < 3; ++c) {
                    std::uint32_t vi = raw_i[c];
                    float3 p = xform.pos(in.positions[vi]);
                    float3 n = xform.dir(in.normals[vi]).normalized_safe({0.0f, 1.0f, 0.0f});
                    float2 uv = xform.uv(in.uvs[vi]);
                    float3 t = in.has_tangents ? xform.dir(in.tangents[vi]).normalized_safe({1.0f, 0.0f, 0.0f}) : float3{};
                    float3 b = in.has_tangents ? xform.dir(in.binormals[vi]).normalized_safe({0.0f, 0.0f, 1.0f}) : float3{};
                    std::uint16_t idx = chunk.add_vertex(p, n, uv, t, b);
                    chunk.indices.push_back(idx);
                }
                ++ti;
            }
            if (!prepare_surface_chunk(chunk, options.preserve_input_tangents && in.has_tangents, options)) {
                overflow = true;
                break;
            }
            cooked_surface cs;
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
            throw std::runtime_error("Surface split exceeded mender limit even after backoff: " + in.name);
        }
        limit -= 500u;
    }
}

std::vector<std::uint8_t> serialize_surface_vertices(std::vector<static_vertex> const& verts) {
    std::vector<std::uint8_t> out;
    out.reserve(4 + verts.size() * sizeof(static_vertex));
    append_le<std::uint32_t>(out, static_cast<std::uint32_t>(verts.size()));
    auto const* raw = reinterpret_cast<std::uint8_t const*>(verts.data());
    out.insert(out.end(), raw, raw + verts.size() * sizeof(static_vertex));
    return out;
}

void build_collision_from_surfaces(
    std::vector<cooked_surface> const& surfaces,
    std::vector<float3>& out_pos,
    std::vector<std::uint32_t>& out_idx
) {
    out_pos.clear();
    out_idx.clear();
    for (auto const& s : surfaces) {
        std::uint32_t base = static_cast<std::uint32_t>(out_pos.size());
        out_pos.reserve(out_pos.size() + s.vertices.size());
        for (auto const& v : s.vertices) out_pos.push_back(v.P);
        out_idx.reserve(out_idx.size() + s.indices.size());
        for (std::uint16_t i : s.indices) out_idx.push_back(base + i);
    }
}

} // namespace

cook_result cook_static_model(
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

    auto const& surf_arr = manifest.require("surfaces").as_array();
    if (surf_arr.empty() || surf_arr.size() > 128) {
        throw std::runtime_error("surfaces must contain 1..128 items");
    }

    std::vector<cooked_surface> cooked_surfaces;
    std::vector<surface_meta> surface_metas;
    std::set<std::string> unique_surface_names;
    aabb model_bbox;

    for (auto const& snode : surf_arr) {
        input_surface in;
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
            throw std::runtime_error("Surface counts out of valid triangle bounds: " + in.name);
        }
        auto const& streams = snode.require("streams");
        in.positions = read_f32x3_span(streams.require("positions"), payload, vc);
        in.normals = read_f32x3_span(streams.require("normals"), payload, vc);
        in.uvs = read_f32x2_span(streams.require("uvs"), payload, vc);
        in.indices = read_index_span(streams.require("indices"), payload, ic, vc);
        if (auto const* ts = streams.find("tangents")) {
            if (auto const* bs = streams.find("binormals")) {
                in.tangents = read_f32x3_span(*ts, payload, vc);
                in.binormals = read_f32x3_span(*bs, payload, vc);
                in.has_tangents = true;
            }
        }

        auto chunks = cook_single_input_surface(in, xform, eff, eff.max_verts_per_surface);
        for (auto& cs_item : chunks) {
            if (!unique_surface_names.insert(cs_item.meta.save_name).second) {
                throw std::runtime_error("Duplicate cooked surface name: " + cs_item.meta.save_name);
            }
            model_bbox.modify(cs_item.meta.bounds);
            surface_metas.push_back(cs_item.meta);
            cooked_surfaces.push_back(std::move(cs_item));
        }
    }

    auto locators = parse_locators(manifest, xform);

    cook_result result;
    result.asset_kind = "static_model";
    result.logical_name = manifest.require("name").as_string();
    result.output_dir = output_dir;
    result.converted_dir = converted_dir;
    result.archive_path = archive_path;
    result.locator_count = static_cast<std::uint32_t>(locators.size());

    std::map<std::string, std::vector<std::uint8_t>> archive_files;

    config_value settings_tree = build_or_merge_settings(
        output_dir / "settings", surface_metas, manifest.find("rigid_body"), options.preserve_existing_settings
    );
    std::string settings_lua = serialize_lua_config(settings_tree);
    write_file_text(output_dir / "settings", settings_lua);
    if (!converted_dir.empty()) {
        write_file_bytes(converted_dir / "settings", serialize_binary_config(settings_tree));
    }

    config_value root_props_tree = build_export_properties_tree(1, model_bbox, &locators);
    std::string root_props_lua = serialize_lua_config(root_props_tree);
    write_file_text(output_dir / "render" / "export_properties", root_props_lua);
    archive_files["render/export_properties"] = std::vector<std::uint8_t>(root_props_lua.begin(), root_props_lua.end());
    if (!converted_dir.empty()) {
        write_file_bytes(converted_dir / "render" / "export_properties", serialize_binary_config(root_props_tree));
    }

    for (auto const& s : cooked_surfaces) {
        std::filesystem::path sdir = output_dir / "render" / s.meta.save_name;
        config_value sprops = build_export_properties_tree(0, s.meta.bounds, nullptr, &s.meta);
        std::string sprops_lua = serialize_lua_config(sprops);
        auto v_bytes = serialize_surface_vertices(s.vertices);
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
            write_file_bytes(cdir / "converted_model", convert_static_surface_to_vulkan_blob(v_bytes, i_bytes));
        }

        result.surfaces.push_back(cook_surface_summary{
            s.meta.save_name,
            !s.meta.material_name.empty() ? s.meta.material_name : s.meta.texture_name,
            s.meta.texture_name,
            static_cast<std::uint32_t>(s.vertices.size()),
            static_cast<std::uint32_t>(s.indices.size()),
        });
    }

    std::vector<float3> col_pos;
    std::vector<std::uint32_t> col_idx;
    if (auto const* col = manifest.find("collision"); col && !col->is_null()) {
        parse_collision_mesh(*col, payload, xform, col_pos, col_idx);
    } else {
        build_collision_from_surfaces(cooked_surfaces, col_pos, col_idx);
    }
    collision_buffers col_bufs = serialize_collision(col_pos, col_idx);
    write_file_bytes(output_dir / "collision" / "vertices", col_bufs.vertices_chunk);
    write_file_bytes(output_dir / "collision" / "indices", col_bufs.indices_chunk);
    archive_files["collision/vertices"] = col_bufs.vertices_chunk;
    archive_files["collision/indices"] = col_bufs.indices_chunk;
    result.has_collision = true;

    std::vector<float3> hq_pos;
    std::vector<std::uint32_t> hq_idx;
    if (auto const* hq = manifest.find("hq_collision"); hq && !hq->is_null()) {
        parse_collision_mesh(*hq, payload, xform, hq_pos, hq_idx);
    } else {
        build_collision_from_surfaces(cooked_surfaces, hq_pos, hq_idx);
    }
    collision_buffers hq_bufs = serialize_collision(hq_pos, hq_idx);
    write_file_bytes(output_dir / "hq_collision" / "vertices", hq_bufs.vertices_chunk);
    write_file_bytes(output_dir / "hq_collision" / "indices", hq_bufs.indices_chunk);
    archive_files["hq_collision/vertices"] = hq_bufs.vertices_chunk;
    archive_files["hq_collision/indices"] = hq_bufs.indices_chunk;
    result.has_hq_collision = true;

    if (!archive_path.empty()) {
        write_file_bytes(archive_path, build_vfs_archive(archive_files));
    }

    return result;
}

} // namespace xray::maya_cook::detail
