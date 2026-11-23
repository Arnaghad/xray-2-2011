#pragma once

#include "math_types.hpp"

#include <cstdint>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace xray::maya_cook::detail {

struct config_value {
    enum class kind_t : std::uint16_t {
        boolean = 0,
        integer = 1,
        number = 2,
        table_named = 3,
        table_indexed = 4,
        text = 5,
        vec2 = 6,
        vec3 = 7,
        vec4 = 8,
    };

    kind_t kind = kind_t::table_named;
    std::string name;
    double number = 0.0;
    std::string str;
    std::array<float, 4> vec{};
    std::vector<config_value> children;

    static config_value make_bool(std::string key, bool v);
    static config_value make_int(std::string key, std::int32_t v);
    static config_value make_float(std::string key, float v);
    static config_value make_string(std::string key, std::string v);
    static config_value make_float2(std::string key, float2 v);
    static config_value make_float3(std::string key, float3 v);
    static config_value make_float4(std::string key, float x, float y, float z, float w);
    static config_value make_table(std::string key = {}, bool indexed = false);

    config_value& set(config_value child);
    config_value* find(std::string_view key);
    config_value const* find(std::string_view key) const;
};

/// Serialize root named table to X-Ray Lua text config format.
std::string serialize_lua_config(config_value const& root);

/// Parse X-Ray Lua text config (`settings`, `export_properties`, `.skeleton`, etc.).
config_value parse_lua_config(std::string_view text);

/// Serialize config tree into X-Ray 64-bit FAT binary config bytes (`configs::checked`).
std::vector<std::uint8_t> serialize_binary_config(config_value root);

/// Write a static-mesh `converted_model` blob (`model_chunk_vertices` 3 + `model_chunk_indices` 4)
/// matching `xray::render::vulkan::original::convert_static_surface` bit-for-bit.
std::vector<std::uint8_t> convert_static_surface_to_vulkan_blob(
    std::vector<std::uint8_t> const& raw_vertices_chunk,
    std::vector<std::uint8_t> const& raw_indices_chunk
);

/// Write a skeletal-mesh `converted_model` blob wrapping raw 4-weight `vertices` + `indices`
/// in chunks 3 and 4 for `xray::render::vulkan::load_skeletal_mesh`.
std::vector<std::uint8_t> convert_skeletal_surface_to_vulkan_blob(
    std::vector<std::uint8_t> const& raw_vertices,
    std::vector<std::uint8_t> const& raw_indices
);

} // namespace xray::maya_cook::detail
