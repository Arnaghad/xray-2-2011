#include "model_cook_common.hpp"

#include <cstring>
#include <fstream>
#include <set>
#include <stdexcept>

namespace xray::maya_cook::detail {

cook_options resolve_effective_options(json_value const& manifest, cook_options const& options) {
    cook_options eff = options;
    if (auto const* opts_node = manifest.find("options"); opts_node && opts_node->is_object()) {
        if (auto const* v = opts_node->find("optimize_uv")) eff.optimize_uv = eff.optimize_uv && v->as_bool();
        if (auto const* v = opts_node->find("optimize_mesh")) eff.optimize_mesh = eff.optimize_mesh && v->as_bool();
        if (auto const* v = opts_node->find("max_verts_per_surface")) eff.max_verts_per_surface = v->as_u32();
    }
    return eff;
}

void validate_safe_segment(std::string_view name, std::string_view label, std::size_t max_len) {
    if (name.empty() || name.size() > max_len || name == "." || name == "..") {
        throw std::runtime_error(std::string("Invalid identifier for ") + std::string(label));
    }
    for (unsigned char c : name) {
        if (c < 0x20 || c == '/' || c == '\\' || c == ':') {
            throw std::runtime_error(std::string("Forbidden character in ") + std::string(label));
        }
    }
}

void validate_span_object(
    json_value const& span,
    std::span<std::uint8_t const> payload,
    std::string_view expected_fmt,
    std::uint32_t expected_count,
    std::size_t elem_size
) {
    std::string const& fmt = span.require("format").as_string();
    if (!expected_fmt.empty() && fmt != expected_fmt) {
        throw std::runtime_error("Unexpected binary stream format: " + fmt);
    }
    std::uint64_t off = span.require("offset").as_u64();
    std::uint64_t bytes = span.require("bytes").as_u64();
    std::uint32_t cnt = span.require("count").as_u32();
    if (off % 4 != 0) throw std::runtime_error("Unaligned payload span offset");
    if (cnt != expected_count) throw std::runtime_error("Payload span count mismatch");
    if (bytes != std::uint64_t(cnt) * elem_size) throw std::runtime_error("Payload span byte size mismatch");
    if (off > payload.size() || bytes > payload.size() - off) {
        throw std::runtime_error("Payload span exceeds payload.bin bounds");
    }
}

std::vector<float> read_f32_span(json_value const& span, std::span<std::uint8_t const> payload, std::uint32_t count) {
    validate_span_object(span, payload, "f32", count, 4);
    std::size_t off = static_cast<std::size_t>(span.require("offset").as_u64());
    std::vector<float> out(count);
    for (std::uint32_t i = 0; i < count; ++i) {
        float v = read_le<float>(payload, off + std::size_t(i) * 4);
        if (!std::isfinite(v)) throw std::runtime_error("Non-finite float in payload");
        out[i] = v;
    }
    return out;
}

std::vector<float2> read_f32x2_span(json_value const& span, std::span<std::uint8_t const> payload, std::uint32_t count) {
    validate_span_object(span, payload, "f32x2", count, 8);
    std::size_t off = static_cast<std::size_t>(span.require("offset").as_u64());
    std::vector<float2> out(count);
    for (std::uint32_t i = 0; i < count; ++i) {
        float2 v{
            read_le<float>(payload, off + std::size_t(i) * 8 + 0),
            read_le<float>(payload, off + std::size_t(i) * 8 + 4),
        };
        if (!v.is_finite()) throw std::runtime_error("Non-finite float2 in payload");
        out[i] = v;
    }
    return out;
}

std::vector<float3> read_f32x3_span(json_value const& span, std::span<std::uint8_t const> payload, std::uint32_t count) {
    validate_span_object(span, payload, "f32x3", count, 12);
    std::size_t off = static_cast<std::size_t>(span.require("offset").as_u64());
    std::vector<float3> out(count);
    for (std::uint32_t i = 0; i < count; ++i) {
        float3 v{
            read_le<float>(payload, off + std::size_t(i) * 12 + 0),
            read_le<float>(payload, off + std::size_t(i) * 12 + 4),
            read_le<float>(payload, off + std::size_t(i) * 12 + 8),
        };
        if (!v.is_finite()) throw std::runtime_error("Non-finite float3 in payload");
        out[i] = v;
    }
    return out;
}

std::vector<std::array<float, 4>> read_f32x4_span(
    json_value const& span,
    std::span<std::uint8_t const> payload,
    std::uint32_t count
) {
    validate_span_object(span, payload, "f32x4", count, 16);
    std::size_t off = static_cast<std::size_t>(span.require("offset").as_u64());
    std::vector<std::array<float, 4>> out(count);
    for (std::uint32_t i = 0; i < count; ++i) {
        for (std::size_t j = 0; j < 4; ++j) {
            float v = read_le<float>(payload, off + std::size_t(i) * 16 + j * 4);
            if (!std::isfinite(v)) throw std::runtime_error("Non-finite float4 in payload");
            out[i][j] = v;
        }
    }
    return out;
}

std::vector<std::array<std::uint16_t, 4>> read_u16x4_span(
    json_value const& span,
    std::span<std::uint8_t const> payload,
    std::uint32_t count
) {
    validate_span_object(span, payload, "u16x4", count, 8);
    std::size_t off = static_cast<std::size_t>(span.require("offset").as_u64());
    std::vector<std::array<std::uint16_t, 4>> out(count);
    for (std::uint32_t i = 0; i < count; ++i) {
        for (std::size_t j = 0; j < 4; ++j) {
            out[i][j] = read_le<std::uint16_t>(payload, off + std::size_t(i) * 8 + j * 2);
        }
    }
    return out;
}

std::vector<std::uint32_t> read_index_span(
    json_value const& span,
    std::span<std::uint8_t const> payload,
    std::uint32_t count,
    std::uint32_t vertex_count
) {
    std::string const& fmt = span.require("format").as_string();
    if (fmt != "u32" && fmt != "u16") {
        throw std::runtime_error("Index stream must use u16 or u32");
    }
    std::size_t stride = (fmt == "u32") ? 4 : 2;
    validate_span_object(span, payload, fmt, count, stride);
    std::size_t off = static_cast<std::size_t>(span.require("offset").as_u64());
    std::vector<std::uint32_t> out(count);
    for (std::uint32_t i = 0; i < count; ++i) {
        std::uint32_t idx = (fmt == "u32")
            ? read_le<std::uint32_t>(payload, off + std::size_t(i) * 4)
            : read_le<std::uint16_t>(payload, off + std::size_t(i) * 2);
        if (idx >= vertex_count) {
            throw std::runtime_error("Triangle index exceeds vertex_count");
        }
        out[i] = idx;
    }
    return out;
}

float3 read_json_vec3(json_value const& arr_node, char const* label) {
    auto const& arr = arr_node.as_array();
    if (arr.size() != 3) {
        throw std::runtime_error(std::string(label) + " must be vec3");
    }
    float3 v{
        static_cast<float>(arr[0].as_double()),
        static_cast<float>(arr[1].as_double()),
        static_cast<float>(arr[2].as_double()),
    };
    if (!v.is_finite()) {
        throw std::runtime_error(std::string("Non-finite vec3 in ") + label);
    }
    return v;
}

std::string sanitize_surface_name(std::string_view raw, std::uint32_t split_index) {
    std::string base(raw.substr(0, std::min<std::size_t>(raw.size(), 60)));
    for (char& c : base) {
        if (c == ':' || c == '|' || c == '/' || c == '\\') c = '_';
    }
    if (split_index > 0) {
        base += "_" + std::to_string(split_index);
    }
    validate_safe_segment(base, "cooked surface name");
    return base;
}

std::vector<locator_entry> parse_locators(
    json_value const& manifest,
    coord_transform const& xform,
    std::size_t bone_count
) {
    std::vector<locator_entry> locators;
    if (auto const* locs = manifest.find("locators"); locs && !locs->is_null()) {
        auto const& arr = locs->as_array();
        if (arr.size() > 256) throw std::runtime_error("Locator count exceeds 256");
        std::set<std::string> seen_loc;
        for (auto const& lnode : arr) {
            locator_entry loc;
            loc.name = lnode.require("name").as_string();
            validate_safe_segment(loc.name, "locator.name", 31);
            if (!seen_loc.insert(loc.name).second) {
                throw std::runtime_error("Duplicate locator name: " + loc.name);
            }
            if (auto const* bn = lnode.find("bone_name")) loc.bone_name = bn->as_string();
            if (auto const* bi = lnode.find("bone_idx")) loc.bone_idx = static_cast<std::int32_t>(bi->as_i64());
            if (loc.bone_idx >= 0 && bone_count > 0 && static_cast<std::size_t>(loc.bone_idx) >= bone_count) {
                throw std::runtime_error("Locator bone_idx exceeds bind pose count: " + loc.name);
            }
            loc.position = xform.pos(read_json_vec3(lnode.require("position"), "locator.position"));
            loc.rotation = xform.euler(read_json_vec3(lnode.require("rotation"), "locator.rotation"));
            locators.push_back(std::move(loc));
        }
    }
    return locators;
}

config_value build_export_properties_tree(
    std::int32_t type,
    aabb const& box,
    std::vector<locator_entry> const* locators,
    surface_meta const* source_surface
) {
    aabb b = box.initialized ? box : aabb::identity();
    config_value root = config_value::make_table({}, false);

    config_value bb = config_value::make_table("bounding_box", false);
    bb.set(config_value::make_float3("max", b.max));
    bb.set(config_value::make_float3("min", b.min));
    root.set(std::move(bb));

    config_value bs = config_value::make_table("bounding_sphere", false);
    bs.set(config_value::make_float3("pos", b.center()));
    bs.set(config_value::make_float("radius", b.sphere_radius()));
    root.set(std::move(bs));

    if (source_surface) {
        config_value src = config_value::make_table("source", false);
        src.set(config_value::make_int("indices", static_cast<std::int32_t>(source_surface->index_count)));
        src.set(config_value::make_string("mesh_name", source_surface->mesh_name));
        src.set(config_value::make_int("primitives", static_cast<std::int32_t>(source_surface->index_count / 3)));
        src.set(config_value::make_string("texture_name", source_surface->texture_name));
        src.set(config_value::make_int("vertices", static_cast<std::int32_t>(source_surface->vertex_count)));
        root.set(std::move(src));
    }

    if (locators && !locators->empty()) {
        config_value locs = config_value::make_table("locators", true);
        for (auto const& loc : *locators) {
            config_value item = config_value::make_table({}, false);
            item.set(config_value::make_string("bone_name", loc.bone_name));
            item.set(config_value::make_int("bone_idx", loc.bone_idx));
            item.set(config_value::make_string("name", loc.name));
            item.set(config_value::make_float3("position", loc.position));
            item.set(config_value::make_float3("rotation", loc.rotation));
            locs.children.push_back(std::move(item));
        }
        root.set(std::move(locs));
    }

    root.set(config_value::make_int("type", type));
    root.set(config_value::make_int("version", 0));
    return root;
}

config_value make_default_rigid_body(json_value const* rb_json) {
    config_value rb = config_value::make_table("rigid_body", false);
    auto fval = [&](char const* k, float def) {
        if (rb_json) if (auto const* v = rb_json->find(k)) return static_cast<float>(v->as_double());
        return def;
    };
    auto bval = [&](char const* k, bool def) {
        if (rb_json) if (auto const* v = rb_json->find(k)) return v->as_bool();
        return def;
    };
    rb.set(config_value::make_float("additionalAngularDampingFactor", fval("additionalAngularDampingFactor", 0.01f)));
    rb.set(config_value::make_float("additionalAngularDampingThresholdSqr", fval("additionalAngularDampingThresholdSqr", 0.01f)));
    rb.set(config_value::make_bool("additionalDamping", bval("additionalDamping", false)));
    rb.set(config_value::make_float("additionalDampingFactor", fval("additionalDampingFactor", 0.005f)));
    rb.set(config_value::make_float("additionalLinearDampingThresholdSqr", fval("additionalLinearDampingThresholdSqr", 0.01f)));
    rb.set(config_value::make_float("angularDamping", fval("angularDamping", 0.0f)));
    rb.set(config_value::make_float("angularSleepingThreshold", fval("angularSleepingThreshold", 1.0f)));
    rb.set(config_value::make_float("friction", fval("friction", 0.5f)));
    rb.set(config_value::make_float("linearSleepingThreshold", fval("linearSleepingThreshold", 0.8f)));
    rb.set(config_value::make_float("m_linearDamping", fval("m_linearDamping", 0.0f)));
    rb.set(config_value::make_float("mass", fval("mass", 0.0f)));
    rb.set(config_value::make_float("restitution", fval("restitution", 0.0f)));
    return rb;
}

config_value build_or_merge_settings(
    std::filesystem::path const& settings_path,
    std::vector<surface_meta> const& surfaces,
    json_value const* rb_json,
    bool preserve_existing
) {
    config_value root = config_value::make_table({}, false);
    config_value existing_materials = config_value::make_table("material_settings", false);
    if (preserve_existing && std::filesystem::is_regular_file(settings_path)) {
        std::ifstream is(settings_path, std::ios::binary);
        std::string text((std::istreambuf_iterator<char>(is)), {});
        if (!text.empty()) {
            root = parse_lua_config(text);
            if (auto* prev_ms = root.find("material_settings")) {
                existing_materials = *prev_ms;
            }
        }
    }

    config_value lods = config_value::make_table("lod_hierrarchy", false);
    config_value lod0 = config_value::make_table("LOD0", false);
    config_value lod0_s = config_value::make_table("surfaces", false);
    config_value ms = config_value::make_table("material_settings", false);

    for (auto const& s : surfaces) {
        lod0_s.set(config_value::make_string(s.save_name, ""));

        std::string mat_name;
        if (auto const* prev = existing_materials.find(s.save_name)) {
            if (auto const* mn = prev->find("material_name")) {
                mat_name = mn->str;
            }
        }
        if (mat_name.empty()) {
            mat_name = !s.material_name.empty() ? s.material_name : s.texture_name;
        }
        config_value entry = config_value::make_table(s.save_name, false);
        entry.set(config_value::make_string("material_name", std::move(mat_name)));
        ms.set(std::move(entry));
    }

    lod0.set(std::move(lod0_s));
    lods.set(std::move(lod0));
    lods.set(config_value::make_table("LOD1", false));
    lods.set(config_value::make_table("LOD2", false));

    root.set(std::move(lods));
    root.set(std::move(ms));
    if (!root.find("rigid_body") || rb_json != nullptr) {
        root.set(make_default_rigid_body(rb_json));
    } else {
        // Lua writes integral-valued floats as 0/1. Restore known field types
        // after parsing so a re-cook retains both user values and binary types.
        auto* rigid_body = root.find("rigid_body");
        for (auto const& field : make_default_rigid_body(nullptr).children) {
            auto* existing = rigid_body->find(field.name);
            if (field.kind == config_value::kind_t::number && existing &&
                (existing->kind == config_value::kind_t::integer || existing->kind == config_value::kind_t::number))
                *existing = config_value::make_float(field.name, static_cast<float>(existing->number));
        }
    }
    return root;
}

std::vector<std::uint8_t> serialize_surface_indices(std::vector<std::uint16_t> const& indices) {
    std::vector<std::uint8_t> out;
    out.reserve(4 + indices.size() * sizeof(std::uint16_t));
    append_le<std::uint32_t>(out, static_cast<std::uint32_t>(indices.size()));
    auto const* raw = reinterpret_cast<std::uint8_t const*>(indices.data());
    out.insert(out.end(), raw, raw + indices.size() * sizeof(std::uint16_t));
    return out;
}

collision_buffers serialize_collision(std::vector<float3> const& pos, std::vector<std::uint32_t> const& idx) {
    if (pos.empty() || idx.empty() || idx.size() % 3 != 0) {
        throw std::runtime_error("Collision mesh must be non-empty triangles");
    }
    collision_buffers cb;
    cb.vertices_chunk.reserve(8 + pos.size() * 12);
    append_le<std::uint32_t>(cb.vertices_chunk, 25u); // model_chunk_collision_v
    append_le<std::uint32_t>(cb.vertices_chunk, static_cast<std::uint32_t>(pos.size() * 12));
    auto const* pv = reinterpret_cast<std::uint8_t const*>(pos.data());
    cb.vertices_chunk.insert(cb.vertices_chunk.end(), pv, pv + pos.size() * 12);

    cb.indices_chunk.reserve(8 + idx.size() * 4);
    append_le<std::uint32_t>(cb.indices_chunk, 26u); // model_chunk_collision_i
    append_le<std::uint32_t>(cb.indices_chunk, static_cast<std::uint32_t>(idx.size() * 4));
    auto const* pi = reinterpret_cast<std::uint8_t const*>(idx.data());
    cb.indices_chunk.insert(cb.indices_chunk.end(), pi, pi + idx.size() * 4);
    return cb;
}

void parse_collision_mesh(
    json_value const& node,
    std::span<std::uint8_t const> payload,
    coord_transform const& xform,
    std::vector<float3>& out_pos,
    std::vector<std::uint32_t>& out_idx
) {
    std::uint32_t vc = node.require("vertex_count").as_u32();
    std::uint32_t ic = node.require("index_count").as_u32();
    if (vc < 3 || vc > 2000000 || ic < 3 || ic % 3 != 0) {
        throw std::runtime_error("Collision counts out of valid triangle bounds");
    }
    auto pos = read_f32x3_span(node.require("positions"), payload, vc);
    auto idx = read_index_span(node.require("indices"), payload, ic, vc);
    out_pos.resize(vc);
    for (std::uint32_t i = 0; i < vc; ++i) {
        out_pos[i] = xform.pos(pos[i]);
    }
    out_idx = std::move(idx);
    if (xform.flip_winding) {
        for (std::size_t k = 0; k < out_idx.size(); k += 3) {
            std::swap(out_idx[k + 1], out_idx[k + 2]);
        }
    }
}

resolved_file_targets resolve_single_file_targets(
    std::filesystem::path const& output_dir,
    std::filesystem::path const& converted_dir,
    std::string_view logical_name,
    std::string_view extension
) {
    resolved_file_targets res;
    if (output_dir.extension() == extension) {
        res.raw_file = output_dir;
    } else {
        auto pos = logical_name.find_last_of('/');
        std::string_view leaf = (pos == std::string_view::npos) ? logical_name : logical_name.substr(pos + 1);
        std::string base = leaf.empty() ? std::string("asset") : std::string(leaf);
        validate_safe_segment(base, "output asset filename");
        res.raw_file = output_dir / (base + std::string(extension));
    }

    if (!converted_dir.empty()) {
        if (converted_dir.extension() == extension) {
            res.converted_file = converted_dir;
        } else {
            res.converted_file = converted_dir / res.raw_file.filename();
        }
    }
    return res;
}

} // namespace xray::maya_cook::detail
