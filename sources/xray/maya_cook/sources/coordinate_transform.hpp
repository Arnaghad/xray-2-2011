#pragma once

#include "json_value.hpp"
#include "math_types.hpp"

namespace xray::maya_cook::detail {

// Source -> engine. Maya/Y-up uses a Z reflection; Blender/Z-up swaps Y and Z.
// The flags are mutually exclusive. Legacy Y-up arithmetic stays unchanged.
struct coord_transform {
    bool flip_z = false;
    bool swap_yz = false;
    bool flip_uv_v = false;
    bool flip_winding = false;
    float scale = 1.0f;

    bool right_handed() const { return flip_z || swap_yz; }

    float3 pos(float3 p) const {
        p *= scale;
        return dir(p);
    }

    float3 dir(float3 v) const {
        if (flip_z) v.z = -v.z;
        if (swap_yz) std::swap(v.y, v.z);
        return v;
    }

    // Dimensionless TRS scale; physics extents additionally multiply by scale.
    float3 scaling(float3 v) const {
        if (swap_yz) std::swap(v.y, v.z);
        return v;
    }

    float3 euler(float3 r) const;
    float3 inverse_euler(float3 r) const;
    float3 inverse_pos(float3 p) const;
    float3 inverse_dimensions(float3 v) const;

    float2 uv(float2 t) const {
        if (flip_uv_v) t.y = 1.0f - t.y;
        return t;
    }
};

coord_transform parse_coord_transform(json_value const& manifest);

} // namespace xray::maya_cook::detail
