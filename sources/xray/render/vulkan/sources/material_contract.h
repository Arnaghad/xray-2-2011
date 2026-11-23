#ifndef XRAY_VULKAN_MATERIAL_CONTRACT_H_INCLUDED
#define XRAY_VULKAN_MATERIAL_CONTRACT_H_INCLUDED
#include <xray/render/vulkan/library_linkage.h>
#include <xray/render/core/sources/rhi/rhi_types.h>
#include <cstddef>
namespace xray { namespace render { namespace vulkan {
struct frame_constants { float view[16], projection[16]; };
struct object_constants { float world[16], tint[4], parameters[4]; };
struct postprocess_constants { float viewport[4],source_bounds[4],filter[4],bloom[4]; };
struct temporal_constants { float inverse_view_projection[16],previous_view_projection[16],viewport[4],source_bounds[4],filter[4],temporal[4]; };
struct decal_constants { float inverse_projection[16],view_to_decal[16],viewport[4],extents[4],tint[4],normal_parameters[4],reflection_parameters[4],bindless_indices[4]; };
static_assert(sizeof(decal_constants)==224 && offsetof(decal_constants,bindless_indices)==208);
constexpr u64 decal_object_abi=1ull<<31;
struct screen_effect_constants {float inverse_projection[16],viewport[4],source_bounds[4],ao_parameters[4],filter_parameters[4];};
static_assert(sizeof(screen_effect_constants)==128 && offsetof(screen_effect_constants,filter_parameters)==112);
constexpr u64 screen_effect_object_abi=1ull<<21;
constexpr u64 temporal_object_abi=1ull<<22;
constexpr u64 multisample_texture_binding(unsigned slot) { return 1ull<<(24+slot); }
struct surface_object_constants { object_constants object; float surface[4]; };
struct mapped_surface_object_constants { surface_object_constants scalar; float map_parameters[4]; };
struct material_object_constants { mapped_surface_object_constants mapped; float specular[4]; };
struct emissive_material_object_constants { material_object_constants material; float emissive[4]; };
struct detailed_surface_object_constants { mapped_surface_object_constants mapped; float detail_normal_parameters[4]; };
struct extended_gbuffer_object_constants {
    detailed_surface_object_constants detailed;
    float sequence_uv[4];
    float extra_parameters[4];
    float reflection_parameters[4];
    float bindless_indices[4];
    float sss_parameters[4];
};
struct extended_material_object_constants {
    emissive_material_object_constants emissive_material;
    float sequence_uv[4];
    float variation_parameters[4];
    float variation_color[4];
    float diffuse_attenuation[4];
    float detail_diffuse[4];
    float reflection_parameters[4];
    float bindless_indices[4];
    float bindless_indices_extra[4];
};
struct extended_forward_object_constants {
    float world[16];
    float tint[4];
    float parameters[4];
    float forward_params[4];
    float light_direction[4];
    float light_color[4];
    float ambient_color[4];
    float projection_params[4];
    float volume_params[4];
    float viewport_params[4];
    float extra_forward[4];
    float reflection_parameters[4];
    float bindless_indices[4];
};
struct lighting_constants {
    float inverse_projection[16],viewport[4],light_direction[4],light_color[4],light_factors[4];
    float light_position_range[4],spot_parameters[4];
    float ambient[4],fog_color_distance[4],fog_parameters[4];
};
struct shadow_lighting_constants {
    lighting_constants lighting;
    float matrices[4][16],splits[4],parameters[4];
};
constexpr u64 shadow_lighting_object_abi=262144;
constexpr u64 lighting_object_abi = 4096;
constexpr u64 local_lighting_object_abi = 8192;
constexpr u64 emissive_material_object_abi = 16384,environment_lighting_object_abi = 32768;
constexpr u64 surface_object_abi = 16;
constexpr u64 mapped_surface_object_abi = 32;
constexpr u64 normal_texture_binding = 64,roughness_texture_binding = 128;
constexpr u64 detailed_surface_object_abi = 1024,detail_normal_texture_binding = 2048;
constexpr u64 material_object_abi = 256,specular_texture_binding = 512;
constexpr u64 shadow_depth_binding=65536,comparison_sampler_binding=131072;
constexpr u64 cube_texture_binding=524288;
constexpr u64 array_texture_binding=1ull<<23; // Explicit Texture2DArray in texture slot 0.
constexpr u64 normal_array_binding=1ull<<29,specular_array_binding=1ull<<30;
constexpr u64 array_binding(unsigned slot) { return slot==0 ? array_texture_binding : slot==1 ? normal_array_binding : slot==3 ? specular_array_binding : 0; }
constexpr u64 postprocess_object_abi=1048576;
constexpr u64 extended_gbuffer_object_abi = 1ull << 32;
constexpr u64 extended_material_object_abi = 1ull << 33;
constexpr u64 extra_texture_binding = 1ull << 34;
constexpr u64 extra_texture_binding_1 = 1ull << 35;
constexpr u64 extra_texture_binding_2 = 1ull << 36;
constexpr u64 extended_forward_object_abi = 1ull << 37;
constexpr u64 bindless_2d_binding = 1ull << 38;
constexpr u64 bindless_cube_binding = 1ull << 39;
constexpr u64 compute_storage_binding = 1ull << 40;
constexpr u64 compute_constant_binding = 1ull << 41;
// Static material parameters: explicit mip LOD, alpha reference, U scale, V scale.
static_assert(sizeof(frame_constants) == 128 && sizeof(object_constants) == 96);
static_assert(sizeof(temporal_constants)==192 && offsetof(temporal_constants,previous_view_projection)==64
    && offsetof(temporal_constants,viewport)==128 && offsetof(temporal_constants,source_bounds)==144
    && offsetof(temporal_constants,filter)==160 && offsetof(temporal_constants,temporal)==176);
static_assert(sizeof(postprocess_constants)==64 && offsetof(postprocess_constants,source_bounds)==16
    && offsetof(postprocess_constants,filter)==32 && offsetof(postprocess_constants,bloom)==48);
static_assert(sizeof(surface_object_constants) == 112);
static_assert(sizeof(mapped_surface_object_constants) == 128);
static_assert(sizeof(detailed_surface_object_constants) == 144);
static_assert(sizeof(material_object_constants) == 144);
static_assert(sizeof(emissive_material_object_constants)==160 && offsetof(emissive_material_object_constants,emissive)==144);
static_assert(sizeof(extended_gbuffer_object_constants) == 224 && offsetof(extended_gbuffer_object_constants, sequence_uv) == 144 && offsetof(extended_gbuffer_object_constants, extra_parameters) == 160 && offsetof(extended_gbuffer_object_constants, reflection_parameters) == 176 && offsetof(extended_gbuffer_object_constants, bindless_indices) == 192 && offsetof(extended_gbuffer_object_constants, sss_parameters) == 208);
static_assert(sizeof(extended_material_object_constants) == 288 && offsetof(extended_material_object_constants, sequence_uv) == 160 && offsetof(extended_material_object_constants, variation_parameters) == 176 && offsetof(extended_material_object_constants, variation_color) == 192 && offsetof(extended_material_object_constants, diffuse_attenuation) == 208 && offsetof(extended_material_object_constants, detail_diffuse) == 224 && offsetof(extended_material_object_constants, reflection_parameters) == 240 && offsetof(extended_material_object_constants, bindless_indices) == 256 && offsetof(extended_material_object_constants, bindless_indices_extra) == 272);
static_assert(sizeof(extended_forward_object_constants) == 256 && offsetof(extended_forward_object_constants, extra_forward) == 208 && offsetof(extended_forward_object_constants, reflection_parameters) == 224 && offsetof(extended_forward_object_constants, bindless_indices) == 240);
static_assert(sizeof(lighting_constants) == 208);
static_assert(sizeof(shadow_lighting_constants)==496 && offsetof(shadow_lighting_constants,matrices)==208
    && offsetof(shadow_lighting_constants,splits)==464 && offsetof(shadow_lighting_constants,parameters)==480);
static_assert(offsetof(lighting_constants,light_position_range)==128 && offsetof(lighting_constants,spot_parameters)==144);
static_assert(offsetof(lighting_constants,ambient)==160 && offsetof(lighting_constants,fog_color_distance)==176 && offsetof(lighting_constants,fog_parameters)==192);
inline void identity(float* m) { for (int i = 0; i < 16; ++i) m[i] = i % 5 == 0 ? 1.f : 0.f; }
// Throws before module creation when a shader violates the fixed material ABI.
XRAY_RENDER_VULKAN_API u64 reflect_material_contract(std::vector<u32> const&, rhi::shader_stage, char const*, bool demote = false, bool multisample = false, bool bindless = false);
} } }
#endif
