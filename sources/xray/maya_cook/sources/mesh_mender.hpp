#pragma once

#include "math_types.hpp"

#include <cstdint>
#include <vector>

namespace xray::maya_cook::detail {

/// Cross-platform C++26 tangent-space builder & smoothing split mender,
/// matching NVIDIA NVMeshMender behavior used by `xray/maya` without D3D9/MSVC macros.
class mesh_mender {
public:
    struct vertex {
        float3 pos;
        float3 normal;
        float3 tangent;
        float3 binormal;
        float s = 0.0f;
        float t = 0.0f;
    };

    enum class normal_calc_option {
        dont_calculate_normals,
        calculate_normals,
    };

    enum class existing_split_option {
        dont_respect_splits,
        respect_splits,
    };

    bool mend(
        std::vector<vertex>& vertices,
        std::vector<std::uint16_t>& indices,
        std::vector<std::uint16_t>& mapping_new_to_old,
        float min_normals_cos = 1.0f,
        float min_tangents_cos = 0.5f,
        float min_binormals_cos = 0.5f,
        float weight_normals_by_area = 0.0f,
        normal_calc_option calc_normals = normal_calc_option::dont_calculate_normals,
        existing_split_option split_opt = existing_split_option::respect_splits
    );
};

} // namespace xray::maya_cook::detail

