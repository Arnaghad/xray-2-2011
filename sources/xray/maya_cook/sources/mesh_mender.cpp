#include "mesh_mender.hpp"

#include <algorithm>
#include <cmath>
#include <limits>
#include <map>
#include <vector>

namespace xray::maya_cook::detail {

namespace {

float3 make_perpendicular(float3 const& n) {
    float3 axis = (std::abs(n.y) < 0.99f) ? float3{0.0f, 1.0f, 0.0f} : float3{1.0f, 0.0f, 0.0f};
    return n.cross(axis).normalized_safe({1.0f, 0.0f, 0.0f});
}

struct triangle_rec {
    std::uint32_t indices[3]{};
    float3 face_normal;
    float3 tangent;
    float3 binormal;
    bool handled = false;
};

void compute_face_vectors(triangle_rec& tri, std::vector<mesh_mender::vertex> const& verts, bool calc_normals) {
    auto const& v0 = verts[tri.indices[0]];
    auto const& v1 = verts[tri.indices[1]];
    auto const& v2 = verts[tri.indices[2]];

    float3 const e1 = v1.pos - v0.pos;
    float3 const e2 = v2.pos - v0.pos;
    float3 fn = e2.cross(e1);
    tri.face_normal = fn.normalized_safe({0.0f, 1.0f, 0.0f});
    if (!calc_normals && v0.normal.squared_length() > 1e-12f) {
        // Keep triangle normal from vertex average when available.
        float3 avg = (v0.normal + v1.normal + v2.normal).normalized_safe(tri.face_normal);
        tri.face_normal = avg;
    }

    float const du1 = v1.s - v0.s;
    float const dv1 = v1.t - v0.t;
    float const du2 = v2.s - v0.s;
    float const dv2 = v2.t - v0.t;

    float const det = du1 * dv2 - du2 * dv1;
    if (std::abs(det) > 1e-12f && std::isfinite(det)) {
        float const inv = 1.0f / det;
        tri.tangent = (e1 * dv2 - e2 * dv1) * inv;
        tri.binormal = (e2 * du1 - e1 * du2) * inv;
        if (tri.tangent.squared_length() > 1e-20f) {
            tri.tangent = tri.tangent.normalized_safe();
        }
        if (tri.binormal.squared_length() > 1e-20f) {
            tri.binormal = tri.binormal.normalized_safe();
        }
    }

    if (tri.tangent.squared_length() <= 1e-12f) {
        tri.tangent = make_perpendicular(tri.face_normal);
    }
    if (tri.binormal.squared_length() <= 1e-12f) {
        tri.binormal = tri.face_normal.cross(tri.tangent).normalized_safe({0.0f, 0.0f, 1.0f});
    }
}

bool shares_vertex_set(triangle_rec const& a, triangle_rec const& b, std::vector<mesh_mender::vertex> const& verts, bool respect_splits) {
    int shared = 0;
    for (int i = 0; i < 3; ++i) {
        for (int j = 0; j < 3; ++j) {
            if (respect_splits) {
                if (a.indices[i] == b.indices[j]) ++shared;
            } else {
                if (verts[a.indices[i]].pos.is_similar(verts[b.indices[j]].pos, 1e-5f)) ++shared;
            }
        }
    }
    return shared >= 2;
}

template <typename SmoothPred>
bool process_basis_groups(
    std::vector<triangle_rec*>& adj,
    std::vector<mesh_mender::vertex>& verts,
    std::vector<std::uint16_t>& mapping,
    std::uint32_t original_count,
    std::uint32_t v_idx,
    SmoothPred&& can_smooth,
    bool is_tangent,
    bool weight_by_area
) {
    for (auto* t : adj) t->handled = false;

    std::vector<std::vector<triangle_rec*>> groups;
    for (auto* seed : adj) {
        if (seed->handled) continue;
        seed->handled = true;
        std::vector<triangle_rec*> group;
        group.push_back(seed);
        for (std::size_t q = 0; q < group.size(); ++q) {
            triangle_rec* cur = group[q];
            for (auto* cand : adj) {
                if (!cand->handled && shares_vertex_set(*cur, *cand, verts, true) && can_smooth(*cur, *cand)) {
                    cand->handled = true;
                    group.push_back(cand);
                }
            }
        }
        groups.push_back(std::move(group));
    }

    auto accumulate = [&](std::vector<triangle_rec*> const& group) {
        float3 sum{0.0f, 0.0f, 0.0f};
        for (auto* t : group) {
            float w = 1.0f;
            if (weight_by_area) {
                float3 const& p0 = verts[t->indices[0]].pos;
                float3 const& p1 = verts[t->indices[1]].pos;
                float3 const& p2 = verts[t->indices[2]].pos;
                w = std::max((p1 - p0).cross(p2 - p0).length(), 1e-6f);
            }
            sum += (is_tangent ? t->tangent : t->binormal) * w;
        }
        float3 fallback = make_perpendicular(verts[v_idx].normal);
        if (!is_tangent) {
            fallback = verts[v_idx].normal.cross(verts[v_idx].tangent).normalized_safe(fallback);
        }
        return sum.normalized_safe(fallback);
    };

    if (groups.empty()) return true;

    for (std::size_t gi = 0; gi < groups.size(); ++gi) {
        float3 vec = accumulate(groups[gi]);
        if (gi == 0) {
            if (is_tangent) verts[v_idx].tangent = vec;
            else verts[v_idx].binormal = vec;
        } else {
            if (verts.size() >= 65535u) {
                return false;
            }
            std::uint32_t const new_idx = static_cast<std::uint32_t>(verts.size());
            mesh_mender::vertex dup = verts[v_idx];
            if (is_tangent) dup.tangent = vec;
            else dup.binormal = vec;
            verts.push_back(dup);
            std::uint16_t orig = (v_idx < original_count) ? static_cast<std::uint16_t>(v_idx) : mapping[v_idx];
            mapping.push_back(orig);
            for (auto* tri : groups[gi]) {
                for (int k = 0; k < 3; ++k) {
                    if (tri->indices[k] == v_idx) tri->indices[k] = new_idx;
                }
            }
        }
    }
    return true;
}

} // namespace

bool mesh_mender::mend(
    std::vector<vertex>& vertices,
    std::vector<std::uint16_t>& indices,
    std::vector<std::uint16_t>& mapping_new_to_old,
    float min_normals_cos,
    float min_tangents_cos,
    float min_binormals_cos,
    float weight_normals_by_area,
    normal_calc_option calc_normals,
    existing_split_option split_opt
) {
    (void)min_normals_cos;
    (void)split_opt;

    if (vertices.empty() || indices.empty() || indices.size() % 3 != 0) {
        return false;
    }

    std::uint32_t const original_count = static_cast<std::uint32_t>(vertices.size());
    mapping_new_to_old.resize(original_count);
    for (std::uint32_t i = 0; i < original_count; ++i) {
        mapping_new_to_old[i] = static_cast<std::uint16_t>(i);
    }

    std::size_t const tri_count = indices.size() / 3;
    std::vector<triangle_rec> tris(tri_count);
    for (std::size_t ti = 0; ti < tri_count; ++ti) {
        tris[ti].indices[0] = indices[ti * 3 + 0];
        tris[ti].indices[1] = indices[ti * 3 + 1];
        tris[ti].indices[2] = indices[ti * 3 + 2];
        compute_face_vectors(tris[ti], vertices, calc_normals == normal_calc_option::calculate_normals);
    }

    bool const weight_by_area = weight_normals_by_area > 0.0f;

    if (calc_normals == normal_calc_option::calculate_normals) {
        std::vector<float3> accum(original_count, {0.0f, 0.0f, 0.0f});
        for (auto const& t : tris) {
            for (int k = 0; k < 3; ++k) accum[t.indices[k]] += t.face_normal;
        }
        for (std::uint32_t i = 0; i < original_count; ++i) {
            vertices[i].normal = accum[i].normalized_safe({0.0f, 1.0f, 0.0f});
        }
    } else {
        for (auto& v : vertices) {
            v.normal = v.normal.normalized_safe({0.0f, 1.0f, 0.0f});
        }
    }

    // Build adjacency by original vertex indices first, then track dynamic vertex count
    std::vector<std::vector<triangle_rec*>> vert_tris(original_count);
    for (auto& t : tris) {
        for (int k = 0; k < 3; ++k) {
            vert_tris[t.indices[k]].push_back(&t);
        }
    }

    for (std::uint32_t v = 0; v < original_count; ++v) {
        if (vert_tris[v].empty()) continue;
        bool ok = process_basis_groups(
            vert_tris[v],
            vertices,
            mapping_new_to_old,
            original_count,
            v,
            [&](triangle_rec const& a, triangle_rec const& b) {
                return a.tangent.dot(b.tangent) >= min_tangents_cos;
            },
            true,
            weight_by_area
        );
        if (!ok) return false;
    }

    // Rebuild per-vertex adjacency after tangent splits before processing binormals
    vert_tris.assign(vertices.size(), {});
    for (auto& t : tris) {
        for (int k = 0; k < 3; ++k) {
            vert_tris[t.indices[k]].push_back(&t);
        }
    }

    std::uint32_t const post_tan_count = static_cast<std::uint32_t>(vertices.size());
    for (std::uint32_t v = 0; v < post_tan_count; ++v) {
        if (vert_tris[v].empty()) continue;
        bool ok = process_basis_groups(
            vert_tris[v],
            vertices,
            mapping_new_to_old,
            original_count,
            v,
            [&](triangle_rec const& a, triangle_rec const& b) {
                return a.binormal.dot(b.binormal) >= min_binormals_cos;
            },
            false,
            weight_by_area
        );
        if (!ok) return false;
    }

    // Orthogonalize tangent & binormal against vertex normal (preserving handedness & unit length)
    for (auto& v : vertices) {
        v.normal = v.normal.normalized_safe({0.0f, 1.0f, 0.0f});
        float3 t = v.tangent - v.normal * v.normal.dot(v.tangent);
        t = t.normalized_safe(make_perpendicular(v.normal));

        float3 b_in = v.binormal - v.normal * v.normal.dot(v.binormal) - t * t.dot(v.binormal);
        float3 b_ortho = v.normal.cross(t).normalized_safe(make_perpendicular(t));
        if (b_in.squared_length() > 1e-12f && b_in.dot(b_ortho) < 0.0f) {
            b_ortho = -b_ortho;
        }
        v.tangent = t;
        v.binormal = b_ortho;
    }

    for (std::size_t ti = 0; ti < tri_count; ++ti) {
        indices[ti * 3 + 0] = static_cast<std::uint16_t>(tris[ti].indices[0]);
        indices[ti * 3 + 1] = static_cast<std::uint16_t>(tris[ti].indices[1]);
        indices[ti * 3 + 2] = static_cast<std::uint16_t>(tris[ti].indices[2]);
    }
    return true;
}

} // namespace xray::maya_cook::detail

