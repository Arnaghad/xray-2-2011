#pragma once

#include "json_value.hpp"
#include "coordinate_transform.hpp"
#include "lua_and_binary_config.hpp"
#include "math_types.hpp"
#include "mesh_mender.hpp"
#include "meshoptimizer.h"
#include "xray/maya_cook/cook.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace xray::maya_cook::detail {

struct static_vertex {
    float3 P;
    float3 N;
    float3 T;
    float3 B;
    float2 uv;
};
static_assert(sizeof(static_vertex) == 56);

#pragma pack(push, 1)
struct vert_boned_4w {
    std::array<std::uint16_t, 4> m{0xFFFFu, 0xFFFFu, 0xFFFFu, 0xFFFFu};
    float3 P;
    float3 N;
    float3 T;
    float3 B;
    std::array<float, 3> w{1.0f, 0.0f, 0.0f};
    float2 uv;
};
#pragma pack(pop)
static_assert(sizeof(vert_boned_4w) == 76);

struct surface_meta {
    std::string save_name;
    std::string mesh_name;
    std::string texture_name;
    std::string material_name;
    std::string lod = "LOD0";
    aabb bounds;
    std::uint32_t vertex_count = 0;
    std::uint32_t index_count = 0;
};

struct locator_entry {
    std::string name;
    std::string bone_name;
    std::int32_t bone_idx = -1;
    float3 position;
    float3 rotation;
};

struct collision_buffers {
    std::vector<std::uint8_t> vertices_chunk;
    std::vector<std::uint8_t> indices_chunk;
};

cook_options resolve_effective_options(json_value const& manifest, cook_options const& options);

void validate_safe_segment(std::string_view name, std::string_view label, std::size_t max_len = 128);
void validate_span_object(
    json_value const& span,
    std::span<std::uint8_t const> payload,
    std::string_view expected_fmt,
    std::uint32_t expected_count,
    std::size_t elem_size
);

std::vector<float> read_f32_span(json_value const& span, std::span<std::uint8_t const> payload, std::uint32_t count);
std::vector<float2> read_f32x2_span(json_value const& span, std::span<std::uint8_t const> payload, std::uint32_t count);
std::vector<float3> read_f32x3_span(json_value const& span, std::span<std::uint8_t const> payload, std::uint32_t count);
std::vector<std::array<float, 4>> read_f32x4_span(json_value const& span, std::span<std::uint8_t const> payload, std::uint32_t count);
std::vector<std::array<std::uint16_t, 4>> read_u16x4_span(json_value const& span, std::span<std::uint8_t const> payload, std::uint32_t count);
std::vector<std::uint32_t> read_index_span(
    json_value const& span,
    std::span<std::uint8_t const> payload,
    std::uint32_t count,
    std::uint32_t vertex_count
);

float3 read_json_vec3(json_value const& arr_node, char const* label);
std::string sanitize_surface_name(std::string_view raw, std::uint32_t split_index);
std::vector<locator_entry> parse_locators(
    json_value const& manifest,
    coord_transform const& xform,
    std::size_t bone_count = 0
);

config_value build_export_properties_tree(
    std::int32_t type,
    aabb const& box,
    std::vector<locator_entry> const* locators = nullptr,
    surface_meta const* source_surface = nullptr
);
config_value make_default_rigid_body(json_value const* rb_json);
config_value build_or_merge_settings(
    std::filesystem::path const& settings_path,
    std::vector<surface_meta> const& surfaces,
    json_value const* rb_json,
    bool preserve_existing
);

std::vector<std::uint8_t> serialize_surface_indices(std::vector<std::uint16_t> const& indices);
collision_buffers serialize_collision(std::vector<float3> const& pos, std::vector<std::uint32_t> const& idx);
void parse_collision_mesh(
    json_value const& node,
    std::span<std::uint8_t const> payload,
    coord_transform const& xform,
    std::vector<float3>& out_pos,
    std::vector<std::uint32_t>& out_idx
);

struct resolved_file_targets {
    std::filesystem::path raw_file;
    std::filesystem::path converted_file;
};
resolved_file_targets resolve_single_file_targets(
    std::filesystem::path const& output_dir,
    std::filesystem::path const& converted_dir,
    std::string_view logical_name,
    std::string_view extension
);

template <typename Vertex>
void fix_tangents_and_binormals(std::vector<Vertex>& verts) {
    struct sort_item {
        float sqm;
        std::uint32_t idx;
    };
    std::vector<sort_item> items;
    items.reserve(verts.size());
    for (std::uint32_t i = 0; i < verts.size(); ++i) {
        items.push_back({verts[i].P.squared_length(), i});
    }
    std::sort(items.begin(), items.end(), [](auto const& a, auto const& b) {
        return a.sqm < b.sqm;
    });

    std::vector<bool> visited(verts.size(), false);
    for (std::size_t i = 0; i < items.size(); ++i) {
        std::uint32_t idx0 = items[i].idx;
        if (visited[idx0]) continue;
        visited[idx0] = true;

        std::vector<std::uint32_t> group{idx0};
        for (std::size_t j = i + 1; j < items.size(); ++j) {
            if (items[j].sqm > items[i].sqm + 0.1f) break;
            std::uint32_t idx1 = items[j].idx;
            if (visited[idx1]) continue;
            if (verts[idx0].P.is_similar(verts[idx1].P) &&
                verts[idx0].N.is_similar(verts[idx1].N) &&
                verts[idx0].uv.is_similar(verts[idx1].uv)) {
                visited[idx1] = true;
                group.push_back(idx1);
            }
        }
        if (group.size() <= 1) continue;

        float3 avg_t{0.0f, 0.0f, 0.0f};
        float3 avg_b{0.0f, 0.0f, 0.0f};
        for (std::uint32_t idx : group) {
            avg_t += verts[idx].T;
            avg_b += verts[idx].B;
        }
        avg_t = avg_t.normalized_safe(verts[idx0].T);
        avg_b = avg_b.normalized_safe(verts[idx0].B);
        for (std::uint32_t idx : group) {
            verts[idx].T = avg_t;
            verts[idx].B = avg_b;
        }
    }
}

template <typename Vertex>
void optimize_uv_tiles(std::vector<Vertex>& verts) {
    if (verts.empty()) return;
    float u_min = verts[0].uv.x, u_max = verts[0].uv.x;
    float v_min = verts[0].uv.y, v_max = verts[0].uv.y;
    for (auto const& v : verts) {
        u_min = std::min(u_min, v.uv.x);
        u_max = std::max(u_max, v.uv.x);
        v_min = std::min(v_min, v.uv.y);
        v_max = std::max(v_max, v.uv.y);
    }
    float u_delta = std::floor((u_max - u_min) * 0.5f + u_min);
    float v_delta = std::floor((v_max - v_min) * 0.5f + v_min);
    for (auto& v : verts) {
        v.uv.x -= u_delta;
        v.uv.y -= v_delta;
    }
}

template <typename Vertex>
void optimize_surface_mesh(std::vector<Vertex>& verts, std::vector<std::uint16_t>& indices, bool high_quality) {
    if (verts.empty() || indices.empty()) return;
    std::vector<unsigned int> u32_idx(indices.begin(), indices.end());
    meshopt_optimizeVertexCache(u32_idx.data(), u32_idx.data(), u32_idx.size(), verts.size());
    float threshold = high_quality ? 1.05f : 1.15f;
    meshopt_optimizeOverdraw(
        u32_idx.data(), u32_idx.data(), u32_idx.size(),
        &verts[0].P.x, verts.size(), sizeof(Vertex), threshold
    );
    std::size_t unique_verts = meshopt_optimizeVertexFetch(
        verts.data(), u32_idx.data(), u32_idx.size(), verts.data(), verts.size(), sizeof(Vertex)
    );
    verts.resize(unique_verts);
    for (std::size_t i = 0; i < indices.size(); ++i) {
        indices[i] = static_cast<std::uint16_t>(u32_idx[i]);
    }
}

template <typename Vertex>
bool finalize_surface_chunk(
    std::vector<Vertex>& verts,
    std::vector<std::uint16_t>& indices,
    bool preserve_input_tangents,
    cook_options const& options
) {
    if (verts.empty() || indices.empty()) {
        return true;
    }
    if (!preserve_input_tangents) {
        std::vector<mesh_mender::vertex> mv(verts.size());
        for (std::size_t i = 0; i < verts.size(); ++i) {
            mv[i].pos = verts[i].P;
            mv[i].normal = verts[i].N;
            mv[i].s = verts[i].uv.x;
            mv[i].t = verts[i].uv.y;
        }
        std::vector<std::uint16_t> mapping;
        mesh_mender mender;
        if (!mender.mend(
                mv, indices, mapping,
                1.0f, 0.5f, 0.5f, 0.0f,
                mesh_mender::normal_calc_option::dont_calculate_normals,
                mesh_mender::existing_split_option::respect_splits
            )) {
            return false;
        }
        if (mv.size() > 65535u) {
            return false;
        }
        std::vector<Vertex> remapped(mv.size());
        for (std::size_t i = 0; i < mv.size(); ++i) {
            Vertex base = verts[mapping[i]];
            base.P = mv[i].pos;
            base.N = mv[i].normal;
            base.T = mv[i].tangent;
            base.B = mv[i].binormal;
            base.uv = {mv[i].s, mv[i].t};
            remapped[i] = std::move(base);
        }
        verts = std::move(remapped);
        fix_tangents_and_binormals(verts);
    }

    if (options.optimize_uv) {
        optimize_uv_tiles(verts);
    }
    if (options.optimize_mesh) {
        optimize_surface_mesh(verts, indices, options.high_quality_overdraw);
    }
    return true;
}

} // namespace xray::maya_cook::detail
