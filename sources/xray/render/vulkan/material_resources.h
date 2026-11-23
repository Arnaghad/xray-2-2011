#ifndef XRAY_VULKAN_MATERIAL_RESOURCES_H_INCLUDED
#define XRAY_VULKAN_MATERIAL_RESOURCES_H_INCLUDED
#include <xray/render/vulkan/scene_resources.h>
#include <xray/render/core/sources/rhi/rhi_types.h>
#include <xray/render/core/sources/rhi/rhi_enums.h>
#include <vector>
#include <string>

namespace xray { namespace render { namespace vulkan {
struct texture_data {
    rhi::texture_desc description;
    // Layer-major: +X,-X,+Y,-Y,+Z,-Z for cubes, each with its complete declared mip chain.
    std::vector<std::vector<unsigned char>> mips;
};
struct texture_configuration {
    bool srgb=false, legacy_cooked=false, reload_read=false;
    bool source_tga=false, generate_mips=false;
    bool force_linear=false;
    bool pack_tga_normal=false;
    bool source_cube=false;
    unsigned source_dimension=1; // 1=2D, 2=3D/volume strip, 3=cube strip, 4=2D array strip
    unsigned source_compression=0; // 0 RGBA, 1 BC1 RGB, 2 BC2, 3 BC3, 4 A8 in RGBA, 5 BC1a
    bool high_quality=false;
    bool require_rgba=false; // Adapt a cooked BC image for RGBA-only renderer consumers.
};
struct texture_view_request {std::shared_ptr<texture_data const> data;};
XRAY_RENDER_VULKAN_API std::shared_ptr<texture_data const> load_dds_texture(const_buffer, bool srgb=false);
XRAY_RENDER_VULKAN_API std::shared_ptr<texture_data const> load_tga_texture(const_buffer, bool srgb=false, bool generate_mips=false,bool pack_normal=false,bool cube=false,unsigned compression=0,bool high_quality=false,unsigned dimension=1);
XRAY_RENDER_VULKAN_API texture_configuration load_texture_options(const_buffer,texture_configuration defaults={});
XRAY_RENDER_VULKAN_API std::shared_ptr<texture_data const> rgba_texture(std::shared_ptr<texture_data const>);
struct texture_state {
    mutable std::mutex mutex;
    std::shared_ptr<texture_data const> data;
    std::string physical_path,virtual_path;
    texture_configuration configuration;
    bool pending=false;
    bool reload_failed=false;
};
class XRAY_RENDER_VULKAN_API texture_resource final : public resources::unmanaged_resource {
public:
    std::shared_ptr<scene_registry> const owner;
    std::shared_ptr<texture_state> const state;
    texture_resource(std::shared_ptr<scene_registry>, const_buffer, bool srgb, bool legacy_cooked=false,std::string physical_path={});
    texture_resource(std::shared_ptr<scene_registry>, const_buffer, texture_configuration const&,std::string physical_path={},std::string virtual_path={});
    texture_resource(std::shared_ptr<scene_registry>,std::shared_ptr<texture_data const>);
    std::shared_ptr<texture_data const> snapshot() const;
    std::shared_ptr<texture_data const> linear_snapshot() const;
    ~texture_resource() override;
};
struct material_configuration {
    resources::unmanaged_resource_ptr texture;
    std::array<float,4> tint = {1,1,1,1};
    rhi::sampler_desc sampler;
    rhi::cull_mode cull=rhi::cull_mode::none;
    bool alpha_test=false;
    bool cast_shadow=true;
    float alpha_ref=.25f;
    std::array<float,2> uv_scale={1,1};
};
static_assert(sizeof(material_configuration)<=8*sizeof(pcvoid));
enum class material_blend_mode : u8 { opaque,alpha,additive,modulate,subtractive };
enum class g_stage_effect_type : u8 { default_surface=0, tiled=1, burning_wood=2 };
enum class forward_effect_type : u8 { unlit=0, tiled=1, soft=2, fire_fresnel=3, smoke_lighted=4, volume_cone=5, volume_sphere=6, decal=7 };
enum class sss_profile_type : u8 { none=0, skin=1, organic=2 };
enum material_stage_bits : u8 {
    material_stage_gbuffer    = 1 << 0,
    material_stage_forward    = 1 << 1,
    material_stage_lighting   = 1 << 2,
    material_stage_shadow     = 1 << 3,
    material_stage_decal      = 1 << 4,
    material_stage_distortion = 1 << 5,
};
struct forward_material_request {
    std::shared_ptr<material_configuration const> base;
    material_blend_mode blend=material_blend_mode::alpha;
};
static_assert(sizeof(forward_material_request)<=8*sizeof(pcvoid));
struct surface_material_parameters {
    static constexpr float maximum_roughness_remap=16;
    std::array<float,2> roughness_uv={1,1};
    float fresnel_at_0_degree=.04f;
    // Legacy map remap coefficients; the UNORM GBuffer saturates the mapped result.
    std::array<float,2> roughness_range={0,1};
    std::array<float,3> specular_color={1,1,1};
};
struct detail_normal_parameters {
    float scale=1, tile=1;
};
struct sequence_parameters {
    bool enabled=false;
    float width=1, height=1, start_frame=0, play_speed=1;
};
struct variation_parameters {
    bool enabled=false;
    float position_divider=1, rotate=0, scale=1, multiply=1;
    std::array<float,4> color={1,1,1,1};
};
struct diffuse_modulation_parameters {
    bool attenuation_enabled=false;
    std::array<float,4> attenuation_color={1,1,1,1};
    float attenuation_multiplier=1;
    float diffuse_power=1;
};
struct parallax_parameters {
    bool enabled=false;
    float scale=0.f;
};
struct translucency_parameters {
    bool enabled=false;
    float constant_translucency=0.f;
    float alpha_model_parameter=0.f;
};
struct forward_effect_parameters {
    forward_effect_type effect=forward_effect_type::unlit;
    bool use_lighting=false, use_soft_test=false, use_fire_fresnel=false, use_color_power=false;
    float color_power=1.f, soft_distance=1.75f, attenuation_scale=0.25f, uv_tile=1.f, emissive_multiplier=1.f;
    std::array<float,3> move_direction={0.f,0.f,0.f};
};
struct reflection_parameters {
    bool enabled=false, write_diffuse=false, use_mask=false;
    float min_angle=0.f, power=0.1f;
    std::array<float,4> specular_color_multiplier={1.f,1.f,1.f,1.f};
};
struct sss_parameters {
    sss_profile_type profile=sss_profile_type::none;
    float scattering_depth=0.35f, scattering_min_depth=0.f, scattering_color_multiplier=1.f, subdermal_weight=1.f;
    std::array<float,4> blurring_weights={0.92f,0.05f,0.05f,1.f};
};
struct surface_material_textures {
    resources::unmanaged_resource_ptr normal,roughness,specular;
    resources::unmanaged_resource_ptr detail_normal;
    resources::unmanaged_resource_ptr emissive,detail_diffuse,variation_mask,fresnel,anisotropy_dir,diffuse_power;
    resources::unmanaged_resource_ptr bump,translucency,whirl,transparency;
    resources::unmanaged_resource_ptr cubemap,cubemap_mask,subdermal,ao,back_illumination,scattering_amount,scattering_depth;
    g_stage_effect_type g_effect=g_stage_effect_type::default_surface;
    detail_normal_parameters detail;
    sequence_parameters sequence;
    variation_parameters variation;
    diffuse_modulation_parameters diffuse_mod;
    parallax_parameters parallax;
    translucency_parameters translucency_params;
    forward_effect_parameters forward;
    reflection_parameters reflection;
    sss_parameters sss;
    bool blend_with_geometry_normals=true;
    u8 stages_mask=material_stage_gbuffer;
};
struct surface_material_request {
    std::shared_ptr<material_configuration const> base;
    surface_material_parameters surface;
    std::shared_ptr<surface_material_textures const> textures;
};
static_assert(sizeof(surface_material_request)<=8*sizeof(pcvoid));
struct material_contributions { std::array<float,3> emission={}; };
struct decal_material_parameters {
    bool enabled=false,blend_geometry_normals=true;
    std::array<float,3> normal_multiplier={1,1,1};
};
struct distortion_material_parameters {
    bool enabled=false;
    std::array<float,3> scale={10,10,1},panner={};
};
struct contributing_material_request {
    std::shared_ptr<surface_material_request const> material;
    material_contributions contributions;
};
static_assert(sizeof(contributing_material_request)<=8*sizeof(pcvoid));
struct material_asset_description {
    distortion_material_parameters distortion;
    decal_material_parameters decal;
    material_configuration configuration;
    surface_material_parameters surface;
    std::string texture_name;
    std::string normal_texture_name,roughness_texture_name;
    std::string specular_texture_name;
    std::string detail_normal_texture_name;
    std::string emissive_texture_name,detail_diffuse_texture_name,variation_mask_texture_name,fresnel_texture_name,anisotropy_dir_texture_name,diffuse_power_texture_name;
    std::string bump_texture_name,translucency_texture_name,whirl_texture_name,transparency_texture_name;
    std::string cubemap_texture_name,cubemap_mask_texture_name,subdermal_texture_name,ao_texture_name,back_illumination_texture_name,scattering_amount_texture_name,scattering_depth_texture_name;
    g_stage_effect_type g_effect=g_stage_effect_type::default_surface;
    detail_normal_parameters detail;
    sequence_parameters sequence;
    variation_parameters variation;
    diffuse_modulation_parameters diffuse_mod;
    parallax_parameters parallax;
    translucency_parameters translucency_params;
    forward_effect_parameters forward;
    reflection_parameters reflection;
    sss_parameters sss;
    bool blend_with_geometry_normals=true;
    u8 stages_mask=material_stage_gbuffer;
    material_contributions contributions;
    material_blend_mode blend=material_blend_mode::opaque;
};
XRAY_RENDER_VULKAN_API material_asset_description load_material_asset(const_buffer,memory::base_allocator&);
class XRAY_RENDER_VULKAN_API material_resource final : public resources::unmanaged_resource {
public:
    std::shared_ptr<scene_registry> const owner;
    std::shared_ptr<material_configuration const> const data;
    std::string const virtual_path;
    surface_material_parameters const surface;
    surface_material_textures const textures;
    material_contributions const contributions;
    material_blend_mode const blend;
    distortion_material_parameters const distortion;
    material_resource(std::shared_ptr<scene_registry>, material_configuration const&,surface_material_parameters const& = {},surface_material_textures const& = {},material_contributions const& = {},material_blend_mode = material_blend_mode::opaque,distortion_material_parameters const& = {},std::string identity={});
    ~material_resource() override;
};
} } }
#endif
