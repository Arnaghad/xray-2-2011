#include "pch.h"
#include "vulkan_material_cook.h"
#include <xray/render/vulkan/sky_resources.h>
#include <xray/render/vulkan/decal_resources.h>
#include "vulkan_binary_config.h"
#include "vulkan_original_static_assets.h"
#include "vulkan_diagnostics.h"
#include <xray/render/core/memory.h>
#include <xray/resources_fs.h>
#include <xray/resources_query_result.h>
#include <xray/resources_pinned_ptr.h>
#include <cmath>
#include <cstring>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {
using value=configs::binary_config_value;
value const& parameter(value const& effect,pcstr name,u16 type) {
    auto* v=binary::optional(effect,name);
    if (v && v->type==configs::t_table_named) v=binary::optional(*v,"value");
    if (!v || v->type!=type) throw std::runtime_error(std::string("Missing or mistyped material parameter: ")+name);
    return *v;
}
void failed(resources::query_result_for_cook& query,std::exception const& e) {
    diagnostic("error","Vulkan material query failed [%s]: %s",query.get_requested_path(),e.what()); query.finish_query(resources::cook_base::result_error);
}
void parse_reflection(value const& effect,material_asset_description& result) {
    bool use_reflect=binary::optional(effect,"use_reflection") && bool(parameter(effect,"use_reflection",configs::t_boolean));
    if (binary::optional(effect,"use_reflection_diffuse"))
        result.reflection.write_diffuse=use_reflect && bool(parameter(effect,"use_reflection_diffuse",configs::t_boolean));
    bool use_mask=binary::optional(effect,"use_reflection_mask") && bool(parameter(effect,"use_reflection_mask",configs::t_boolean));
    if (binary::optional(effect,"texture_cubemap")) parameter(effect,"texture_cubemap",configs::t_string);
    if (binary::optional(effect,"texture_cubemap_mask")) parameter(effect,"texture_cubemap_mask",configs::t_string);
    if (use_reflect && binary::optional(effect,"texture_cubemap")) {
        result.cubemap_texture_name=pcstr(parameter(effect,"texture_cubemap",configs::t_string));
        if (!result.cubemap_texture_name.empty()) binary::validate_path(result.cubemap_texture_name);
    }
    if (use_reflect && use_mask && binary::optional(effect,"texture_cubemap_mask")) {
        result.cubemap_mask_texture_name=pcstr(parameter(effect,"texture_cubemap_mask",configs::t_string));
        if (!result.cubemap_mask_texture_name.empty()) binary::validate_path(result.cubemap_mask_texture_name);
    }
    if (binary::optional(effect,"reflection_power")) {
        float rp=float(parameter(effect,"reflection_power",configs::t_float));
        if (!std::isfinite(rp) || rp<0 || rp>16) throw std::runtime_error("Invalid reflection_power");
        result.reflection.power=rp;
    }
    if (binary::optional(effect,"min_reflection_angle")) {
        float ma=float(parameter(effect,"min_reflection_angle",configs::t_float));
        if (!std::isfinite(ma) || ma<0 || ma>90) throw std::runtime_error("Invalid min_reflection_angle");
        result.reflection.min_angle=ma;
    }
    if (binary::optional(effect,"specular_color_multiplier")) {
        std::array<float,4> mult;
        std::memcpy(mult.data(),static_cast<pcvoid>(parameter(effect,"specular_color_multiplier",configs::t_float4).data),16);
        for (float c : mult) if (!std::isfinite(c) || c<0 || c>16) throw std::runtime_error("Invalid specular_color_multiplier");
        result.reflection.specular_color_multiplier=mult;
    }
    result.reflection.use_mask=use_mask && !result.cubemap_mask_texture_name.empty();
    result.reflection.enabled=use_reflect && !result.cubemap_texture_name.empty();
}
void forward_stage_into(value const& stage,material_asset_description& result,bool allow_opaque) {
    for (auto const& option : stage) if (std::strcmp(option.key(),"effect")) throw std::runtime_error("Unsupported f_stage parameter");
    auto const& effect=binary::field(stage,"effect",configs::t_table_named);
    if (std::strcmp(pcstr(parameter(effect,"effect_id",configs::t_string)),"f_stage_unlit")) throw std::runtime_error("Unsupported forward effect (expected f_stage_unlit)");
    s32 mode=parameter(effect,"blend_mode",configs::t_integer);
    if (mode<(allow_opaque?0:1) || mode>4) throw std::runtime_error("Forward blend_mode invalid");
    if (mode>0) result.blend=static_cast<material_blend_mode>(mode);
    if (binary::optional(effect,"forward_effect_mode")) {
        s32 fmode=parameter(effect,"forward_effect_mode",configs::t_integer);
        if (fmode<0 || fmode>7) throw std::runtime_error("Invalid forward_effect_mode");
        result.forward.effect=static_cast<forward_effect_type>(fmode);
    }
    if (result.forward.effect==forward_effect_type::decal && mode==0) result.blend=material_blend_mode::alpha;
    auto const& color=parameter(effect,"constant_diffuse",configs::t_float4);
    std::array<float,4> fwd_tint;
    std::memcpy(fwd_tint.data(),static_cast<pcvoid>(color.data),16);
    for (float channel : fwd_tint) if (!std::isfinite(channel) || channel<0 || channel>1) throw std::runtime_error("Invalid forward constant_diffuse");
    if (!(result.stages_mask & (material_stage_gbuffer|material_stage_lighting))) result.configuration.tint=fwd_tint;
    if (binary::optional(effect,"constant_emissive_multiplier")) {
        float m=float(parameter(effect,"constant_emissive_multiplier",configs::t_float));
        if (!std::isfinite(m) || m<0 || m>64) throw std::runtime_error("Invalid forward constant_emissive_multiplier");
        result.forward.emissive_multiplier=m;
    }
    bool textured=parameter(effect,"use_tdiffuse",configs::t_boolean);
    if (binary::optional(effect,"texture_diffuse")) parameter(effect,"texture_diffuse",configs::t_string);
    if (textured) {
        std::string tex=pcstr(parameter(effect,"texture_diffuse",configs::t_string));
        binary::validate_path(tex);
        if (result.texture_name.empty()) result.texture_name=std::move(tex);
    }
    if (binary::optional(effect,"use_ttransparency") && bool(parameter(effect,"use_ttransparency",configs::t_boolean))) {
        result.transparency_texture_name=pcstr(parameter(effect,"texture_transparency",configs::t_string));
        binary::validate_path(result.transparency_texture_name);
    } else if (binary::optional(effect,"texture_transparency")) {
        parameter(effect,"texture_transparency",configs::t_string);
    }
    if (binary::optional(effect,"blend_with_geometry_normals"))
        result.blend_with_geometry_normals=bool(parameter(effect,"blend_with_geometry_normals",configs::t_boolean));
    parse_reflection(effect,result);
    if (binary::optional(effect,"use_alpha_test"))
        result.configuration.alpha_test=bool(parameter(effect,"use_alpha_test",configs::t_boolean));
    if (binary::optional(effect,"alpha_ref")) {
        result.configuration.alpha_ref=float(parameter(effect,"alpha_ref",configs::t_float));
        if (!std::isfinite(result.configuration.alpha_ref) || result.configuration.alpha_ref<0 || result.configuration.alpha_ref>1)
            throw std::runtime_error("Invalid forward alpha_ref");
    }
    if (binary::optional(effect,"use_Color_Power") && bool(parameter(effect,"use_Color_Power",configs::t_boolean))) {
        result.forward.use_color_power=true;
        if (binary::optional(effect,"constant_color_power")) {
            float cp=float(parameter(effect,"constant_color_power",configs::t_float));
            if (!std::isfinite(cp) || cp<=0 || cp>16) throw std::runtime_error("Invalid forward constant_color_power");
            result.forward.color_power=cp;
        }
    }
    if (binary::optional(effect,"use_soft_test") && bool(parameter(effect,"use_soft_test",configs::t_boolean))) {
        result.forward.use_soft_test=true;
        if (binary::optional(effect,"constant_soft_distance")) {
            float sd=float(parameter(effect,"constant_soft_distance",configs::t_float));
            if (!std::isfinite(sd) || sd<=0 || sd>1024) throw std::runtime_error("Invalid forward constant_soft_distance");
            result.forward.soft_distance=sd;
        }
    }
    result.forward.use_fire_fresnel=binary::optional(effect,"use_fire_fresnel") && bool(parameter(effect,"use_fire_fresnel",configs::t_boolean));
    result.forward.use_lighting=binary::optional(effect,"use_lighting") && bool(parameter(effect,"use_lighting",configs::t_boolean));
    if (binary::optional(effect,"attenuation_scale")) {
        float as=float(parameter(effect,"attenuation_scale",configs::t_float));
        if (!std::isfinite(as) || as<0 || as>64) throw std::runtime_error("Invalid forward attenuation_scale");
        result.forward.attenuation_scale=as;
    }
    if (binary::optional(effect,"uv_tile")) {
        float ut=float(parameter(effect,"uv_tile",configs::t_float));
        if (!std::isfinite(ut) || std::abs(ut)>4096) throw std::runtime_error("Invalid forward uv_tile");
        result.forward.uv_tile=ut;
    }
    if (binary::optional(effect,"move_direction")) {
        std::memcpy(result.forward.move_direction.data(),static_cast<pcvoid>(parameter(effect,"move_direction",configs::t_float3).data),12);
        for (float mv : result.forward.move_direction) if (!std::isfinite(mv) || std::abs(mv)>4096) throw std::runtime_error("Invalid forward move_direction");
    }
    if (!(result.stages_mask & (material_stage_gbuffer|material_stage_lighting)))
        result.configuration.cull=binary::optional(effect,"two_sided") && bool(parameter(effect,"two_sided",configs::t_boolean)) ? rhi::cull_mode::none : rhi::cull_mode::back;
    bool u=binary::optional(effect,"constant_tile_u"),v=binary::optional(effect,"constant_tile_v");
    if (u!=v) throw std::runtime_error("Forward UV tiling requires U and V");
    if (u) result.configuration.uv_scale={float(parameter(effect,"constant_tile_u",configs::t_float)),float(parameter(effect,"constant_tile_v",configs::t_float))};
    for (float scale : result.configuration.uv_scale) if (!std::isfinite(scale) || std::abs(scale)>4096) throw std::runtime_error("Invalid forward UV tiling");
    if (binary::optional(effect,"use_sequence") && bool(parameter(effect,"use_sequence",configs::t_boolean))) {
        auto seq_scalar=[&](pcstr name,float fallback) {
            float val=binary::optional(effect,name) ? float(parameter(effect,name,configs::t_float)) : fallback;
            if (!std::isfinite(val)) throw std::runtime_error(std::string("Invalid forward sequence parameter: ")+name);
            return val;
        };
        result.sequence.enabled=true;
        result.sequence.width=seq_scalar("constant_sequence_array_width",1.f);
        result.sequence.height=seq_scalar("constant_sequence_array_height",1.f);
        result.sequence.start_frame=seq_scalar("constant_sequence_start_frame_index",0.f);
        result.sequence.play_speed=seq_scalar("constant_sequence_play_speed",1.f);
        if (result.sequence.width<1 || result.sequence.width>64 || result.sequence.height<1 || result.sequence.height>64)
            throw std::runtime_error("Forward sequence dimensions must be within [1,64]");
    }
    for (auto const& option : effect) {
        pcstr name=option.key();
        if (std::strcmp(name,"effect_id") && std::strcmp(name,"blend_mode") && std::strcmp(name,"forward_effect_mode")
            && std::strcmp(name,"constant_diffuse") && std::strcmp(name,"constant_emissive_multiplier") && std::strcmp(name,"use_tdiffuse")
            && std::strcmp(name,"texture_diffuse") && std::strcmp(name,"two_sided") && std::strcmp(name,"constant_tile_u") && std::strcmp(name,"constant_tile_v")
            && std::strcmp(name,"use_ttransparency") && std::strcmp(name,"texture_transparency")
            && std::strcmp(name,"blend_with_geometry_normals")
            && std::strcmp(name,"use_reflection") && std::strcmp(name,"texture_cubemap") && std::strcmp(name,"reflection_power") && std::strcmp(name,"min_reflection_angle")
            && std::strcmp(name,"use_alpha_test") && std::strcmp(name,"alpha_ref")
            && std::strcmp(name,"use_Color_Power") && std::strcmp(name,"constant_color_power")
            && std::strcmp(name,"use_soft_test") && std::strcmp(name,"constant_soft_distance")
            && std::strcmp(name,"use_fire_fresnel") && std::strcmp(name,"use_lighting")
            && std::strcmp(name,"attenuation_scale") && std::strcmp(name,"uv_tile") && std::strcmp(name,"move_direction")
            && std::strcmp(name,"use_sequence") && std::strcmp(name,"constant_sequence_array_width") && std::strcmp(name,"constant_sequence_array_height")
            && std::strcmp(name,"constant_sequence_start_frame_index") && std::strcmp(name,"constant_sequence_play_speed"))
            throw std::runtime_error(std::string("Unsupported forward material input: ")+name);
    }
}
void parse_lighting_stage(value const& stage,material_asset_description& result) {
    auto const& effect=binary::field(stage,"effect",configs::t_table_named);
    pcstr effect_id=pcstr(parameter(effect,"effect_id",configs::t_string));
    if (!std::strcmp(effect_id,"lighting_skin_base")) result.sss.profile=sss_profile_type::skin;
    else if (!std::strcmp(effect_id,"lighting_organic_base")) result.sss.profile=sss_profile_type::organic;
    else if (!std::strcmp(effect_id,"lighting_default")) result.sss.profile=sss_profile_type::none;
    auto scalar=[&](pcstr name,float fallback,float maximum) {
        float v=binary::optional(effect,name) ? float(parameter(effect,name,configs::t_float)) : fallback;
        if (!std::isfinite(v) || v<0 || v>maximum) throw std::runtime_error(std::string("Invalid lighting parameter: ")+name);
        return v;
    };
    result.sss.scattering_depth=scalar("scattering_depth",0.35f,16.f);
    result.sss.scattering_min_depth=scalar("scattering_min_depth",0.f,16.f);
    result.sss.scattering_color_multiplier=scalar("scattering_color_multiplier",1.f,16.f);
    result.sss.subdermal_weight=scalar("subdermal_weight",1.f,16.f);
    if (binary::optional(effect,"scattering_component_blurring_weights")) {
        std::memcpy(result.sss.blurring_weights.data(),static_cast<pcvoid>(parameter(effect,"scattering_component_blurring_weights",configs::t_float4).data),16);
        for (float w : result.sss.blurring_weights) if (!std::isfinite(w) || w<0 || w>16) throw std::runtime_error("Invalid SSS blurring weights");
    }
    auto opt_map=[&](pcstr flag,pcstr key,std::string& target) {
        bool on=binary::optional(effect,flag) && bool(parameter(effect,flag,configs::t_boolean));
        if (binary::optional(effect,key)) parameter(effect,key,configs::t_string);
        if (on && binary::optional(effect,key)) {
            std::string p=pcstr(parameter(effect,key,configs::t_string));
            if (!p.empty()) { binary::validate_path(p); target=std::move(p); }
        }
    };
    opt_map("use_subdermal_texture","texture_subdermal",result.subdermal_texture_name);
    opt_map("use_ao_texture","texture_ao",result.ao_texture_name);
    opt_map("use_back_illumination_texture","texture_back_illumination",result.back_illumination_texture_name);
    opt_map("use_scattering_amount_mask","texture_scattering_amount",result.scattering_amount_texture_name);
    opt_map("use_scattering_depth_texture","texture_scattering_depth",result.scattering_depth_texture_name);
    if (result.texture_name.empty()) opt_map("use_tdiffuse","texture_diffuse",result.texture_name);
    if (result.normal_texture_name.empty()) opt_map("use_nmap","texture_normal",result.normal_texture_name);
    if (result.roughness_texture_name.empty()) opt_map("use_tspecular_power","texture_specular_power",result.roughness_texture_name);
    if (result.specular_texture_name.empty()) opt_map("use_tspecular_inensity","texture_specular_intensity",result.specular_texture_name);
    if (result.fresnel_texture_name.empty()) opt_map("use_fresnel_at_0_degree_texture","texture_fresnel_at_0_degree",result.fresnel_texture_name);
    if (binary::optional(effect,"constant_specular_power") || binary::optional(effect,"constant_roughness_v_direction")) {
        float u=scalar("constant_specular_power",0,255);
        float v=scalar("constant_roughness_v_direction",0,255);
        bool aniso=binary::optional(effect,"is_anisotropic_material") && bool(parameter(effect,"is_anisotropic_material",configs::t_boolean));
        if (result.surface.roughness_uv==std::array<float,2>{1,1})
            result.surface.roughness_uv={u/255.f,aniso ? v/255.f : u/255.f};
    }
    if (binary::optional(effect,"fresnel_at_0_degree")) result.surface.fresnel_at_0_degree=scalar("fresnel_at_0_degree",.04f,1.f);
    if (binary::optional(effect,"min_roughness") && binary::optional(effect,"max_roughness"))
        result.surface.roughness_range={scalar("min_roughness",0.f,surface_material_parameters::maximum_roughness_remap),
            scalar("max_roughness",1.f,surface_material_parameters::maximum_roughness_remap)};
}
material_asset_description forward_asset(value const& material) {
    material_asset_description result;
    result.stages_mask=material_stage_forward;
    forward_stage_into(binary::field(material,"f_stage",configs::t_table_named),result,false);
    return result;
}
}
material_asset_description load_material_asset(const_buffer source,memory::base_allocator& allocator) {
    binary::checked_config config(source,allocator);
    auto const& material=binary::field(config.root(),"material",configs::t_table_named);
    if(binary::optional(material,"distortion")){
        if(material.size()!=1)throw std::runtime_error("Distortion requires an exclusive stage");
        auto const& stage=binary::field(material,"distortion",configs::t_table_named);
        if(stage.size()!=1)throw std::runtime_error("Unknown distortion stage field");
        auto const& effect=binary::field(stage,"effect",configs::t_table_named);
        if(effect.size()!=4 || std::strcmp(pcstr(parameter(effect,"effect_id",configs::t_string)),"distortion_panner"))throw std::runtime_error("Unsupported distortion effect");
        material_asset_description result;result.distortion.enabled=true;result.stages_mask=material_stage_distortion;
        std::memcpy(result.distortion.scale.data(),static_cast<pcvoid>(parameter(effect,"distortion_scale",configs::t_float3).data),12);
        std::memcpy(result.distortion.panner.data(),static_cast<pcvoid>(parameter(effect,"panner",configs::t_float3).data),12);
        for(float v:result.distortion.scale)if(!std::isfinite(v)||v<0||v>64)throw std::runtime_error("Invalid distortion scale");
        for(float v:result.distortion.panner)if(!std::isfinite(v)||std::abs(v)>16)throw std::runtime_error("Invalid distortion panner");
        result.texture_name=pcstr(parameter(effect,"texture_distortion",configs::t_string));binary::validate_path(result.texture_name);
        result.configuration.cull=rhi::cull_mode::none;result.configuration.sampler.max_lod=0;
        return result;
    }
    if (binary::optional(material,"decals")) {
        if(material.size()!=1)throw std::runtime_error("Decal material requires an exclusive decals stage");
        auto const& stage=binary::field(material,"decals",configs::t_table_named);
        if(stage.size()!=1)throw std::runtime_error("Unsupported decal stage field");
        auto const& effect=binary::field(stage,"effect",configs::t_table_named);
        if(std::strcmp(pcstr(parameter(effect,"effect_id",configs::t_string)),"decals_default")
            || !bool(parameter(effect,"use_tdiffuse",configs::t_boolean)) || !bool(parameter(effect,"use_nmap",configs::t_boolean)))
            throw std::runtime_error("Decals require diffuse/normal maps");
        material_asset_description result;result.decal.enabled=true;result.stages_mask=material_stage_decal;
        result.decal.blend_geometry_normals=parameter(effect,"blend_with_geometry_normals",configs::t_boolean);
        std::memcpy(result.configuration.tint.data(),static_cast<pcvoid>(parameter(effect,"constant_diffuse",configs::t_float4).data),16);
        std::memcpy(result.decal.normal_multiplier.data(),static_cast<pcvoid>(parameter(effect,"normal_multiplier",configs::t_float3).data),12);
        for(float v:result.configuration.tint)if(!std::isfinite(v)||v<0||v>1)throw std::runtime_error("Invalid decal tint");
        for(float v:result.decal.normal_multiplier)if(!std::isfinite(v)||v<0||v>4)throw std::runtime_error("Invalid decal normal multiplier");
        result.texture_name=pcstr(parameter(effect,"texture_diffuse",configs::t_string));
        result.normal_texture_name=pcstr(parameter(effect,"texture_normal",configs::t_string));
        binary::validate_path(result.texture_name);binary::validate_path(result.normal_texture_name);
        parse_reflection(effect,result);
        for(auto const& option:effect){std::string name=option.key();
            if(name!="effect_id" && name!="use_tdiffuse" && name!="use_nmap" && name!="use_reflection" && name!="blend_with_geometry_normals"
                && name!="constant_diffuse" && name!="normal_multiplier" && name!="texture_diffuse" && name!="texture_normal"
                && name!="texture_cubemap" && name!="reflection_power" && name!="min_reflection_angle")
                throw std::runtime_error("Unknown decal material field: "+name);}
        return result;
    }
    bool const has_g_stage=binary::optional(material,"g_stage")!=nullptr;
    bool const has_f_stage=binary::optional(material,"f_stage")!=nullptr;
    bool const has_lighting=binary::optional(material,"lighting")!=nullptr;
    bool const has_shadow=binary::optional(material,"shadow")!=nullptr;
    for (auto const& stage : material) {
        if (std::strcmp(stage.key(),"g_stage") && std::strcmp(stage.key(),"f_stage")
            && std::strcmp(stage.key(),"lighting") && std::strcmp(stage.key(),"shadow"))
            throw std::runtime_error(std::string("Unsupported material render stage or metadata: material.")+stage.key());
    }
    if (has_f_stage && !has_g_stage && !has_lighting && !has_shadow) return forward_asset(material);
    material_asset_description result;
    result.stages_mask=0;
    if (has_g_stage) {
        result.stages_mask|=material_stage_gbuffer;
        auto const& stage=binary::field(material,"g_stage",configs::t_table_named);
        for (auto const& option : stage) if (std::strcmp(option.key(),"effect") && std::strcmp(option.key(),"is_cast_shadow"))
            throw std::runtime_error(std::string("Unsupported g_stage parameter: material.g_stage.")+option.key());
        auto const& effect=binary::field(stage,"effect",configs::t_table_named);
        pcstr effect_id=pcstr(parameter(effect,"effect_id",configs::t_string));
        if (!std::strcmp(effect_id,"g_stage_default")) result.g_effect=g_stage_effect_type::default_surface;
        else if (!std::strcmp(effect_id,"g_stage_default_tiled")) result.g_effect=g_stage_effect_type::tiled;
        else if (!std::strcmp(effect_id,"g_stage_burning_wood")) result.g_effect=g_stage_effect_type::burning_wood;
        else throw std::runtime_error(std::string("Unsupported g_stage material profile: ")+effect_id);
        if (binary::optional(stage,"is_cast_shadow"))
            result.configuration.cast_shadow=bool(parameter(stage,"is_cast_shadow",configs::t_boolean));
        bool textured=parameter(effect,"use_tdiffuse",configs::t_boolean);
        auto const& color=parameter(effect,"constant_diffuse",configs::t_float4);
        std::memcpy(result.configuration.tint.data(),static_cast<pcvoid>(color.data),16);
        for (float channel : result.configuration.tint) if (!std::isfinite(channel) || channel<0 || channel>1)
            throw std::runtime_error("Invalid material constant_diffuse");
        result.configuration.alpha_test=binary::optional(effect,"use_alpha_test") && bool(parameter(effect,"use_alpha_test",configs::t_boolean));
        if (binary::optional(effect,"alpha_ref")) result.configuration.alpha_ref=float(parameter(effect,"alpha_ref",configs::t_float));
        if (!std::isfinite(result.configuration.alpha_ref) || result.configuration.alpha_ref<0 || result.configuration.alpha_ref>1)
            throw std::runtime_error("Invalid material alpha_ref");
        if (!result.configuration.alpha_test && result.configuration.tint[3]!=1)
            throw std::runtime_error("Non-opaque material alpha requires alpha testing; alpha blending is unsupported");
        bool tile_u=binary::optional(effect,"constant_tile_u"),tile_v=binary::optional(effect,"constant_tile_v");
        if (tile_u!=tile_v) throw std::runtime_error("Material UV tiling requires both constant_tile_u and constant_tile_v");
        if (tile_u) {
            result.configuration.uv_scale={float(parameter(effect,"constant_tile_u",configs::t_float)),float(parameter(effect,"constant_tile_v",configs::t_float))};
            for (float value : result.configuration.uv_scale) if (!std::isfinite(value) || std::abs(value)>4096)
                throw std::runtime_error("Invalid material UV tiling");
        }
        result.configuration.cull=binary::optional(effect,"two_sided") && bool(parameter(effect,"two_sided",configs::t_boolean))
            ? rhi::cull_mode::none : rhi::cull_mode::back;
        if (binary::optional(effect,"texture_diffuse")) parameter(effect,"texture_diffuse",configs::t_string);
        if (textured) {
            result.texture_name=pcstr(parameter(effect,"texture_diffuse",configs::t_string));
            binary::validate_path(result.texture_name);
        }
        auto scalar=[&](pcstr name,float fallback,float maximum) {
            float v=binary::optional(effect,name) ? float(parameter(effect,name,configs::t_float)) : fallback;
            if (!std::isfinite(v) || v<0 || v>maximum) throw std::runtime_error(std::string("Invalid material surface parameter: ")+name);
            return v;
        };
        float u=scalar("constant_specular_power",0,255);
        float v=scalar("constant_roughness_v_direction",0,255);
        bool anisotropic=binary::optional(effect,"is_anisotropic_material") && bool(parameter(effect,"is_anisotropic_material",configs::t_boolean));
        result.surface.roughness_uv={u/255,anisotropic ? v/255 : u/255};
        result.surface.fresnel_at_0_degree=scalar("fresnel_at_0_degree",.04f,1);
        result.surface.roughness_range={scalar("min_roughness",0,surface_material_parameters::maximum_roughness_remap),
            scalar("max_roughness",1,surface_material_parameters::maximum_roughness_remap)};
        if (result.surface.roughness_range[0]>result.surface.roughness_range[1]) throw std::runtime_error("Material roughness range is reversed");
        auto map=[&](pcstr flag,pcstr name,std::string& target) {
            bool enabled=binary::optional(effect,flag) && bool(parameter(effect,flag,configs::t_boolean));
            if (binary::optional(effect,name)) parameter(effect,name,configs::t_string);
            if (enabled) { target=pcstr(parameter(effect,name,configs::t_string)); binary::validate_path(target); }
        };
        map("use_nmap","texture_normal",result.normal_texture_name);
        map("use_detail_nmap","texture_detail_normal",result.detail_normal_texture_name);
        result.detail.scale=scalar("scale_detail_normal",1,16);
        result.detail.tile=scalar("tile_detail_normal",1,4096);
        if (!result.detail_normal_texture_name.empty() && result.normal_texture_name.empty())
            throw std::runtime_error("Detail normal requires a base normal map");
        map("use_tspecular_power","texture_specular_power",result.roughness_texture_name);
        map("use_tspecular_inensity","texture_specular_intensity",result.specular_texture_name);
        map("use_tditail_diffuse","texture_ditail_diffuse",result.detail_diffuse_texture_name);
        map("use_fresnel_at_0_degree_texture","texture_fresnel_at_0_degree",result.fresnel_texture_name);
        map("use_anisotropic_direction_texture","anisotropic_direction_texture",result.anisotropy_dir_texture_name);
        map("use_tdiffuse_power","texture_diffuse_power",result.diffuse_power_texture_name);
        auto map_optional=[&](pcstr flag,pcstr name,std::string& target) {
            bool enabled=binary::optional(effect,flag) && bool(parameter(effect,flag,configs::t_boolean));
            if (binary::optional(effect,name)) parameter(effect,name,configs::t_string);
            if (enabled) {
                target=pcstr(parameter(effect,name,configs::t_string));
                if (!target.empty()) binary::validate_path(target);
            }
            return enabled;
        };
        bool parallax_on=map_optional("use_parallax","texture_bump",result.bump_texture_name);
        result.parallax.scale=scalar("constant_parallax_scale",1.f,16.f);
        result.parallax.enabled=parallax_on && !result.bump_texture_name.empty();
        map_optional("use_ttranslucency","texture_translucency",result.translucency_texture_name);
        result.translucency_params.constant_translucency=scalar("constant_translucency",0.f,255.f);
        result.translucency_params.alpha_model_parameter=scalar("alpha_model_parameter",0.f,1.f);
        result.translucency_params.enabled=!result.translucency_texture_name.empty() || result.translucency_params.constant_translucency>0.f;
        if (binary::optional(effect,"texture_whirl")) {
            result.whirl_texture_name=pcstr(parameter(effect,"texture_whirl",configs::t_string));
            if (!result.whirl_texture_name.empty()) binary::validate_path(result.whirl_texture_name);
        }
        parse_reflection(effect,result);
        result.diffuse_mod.diffuse_power=scalar("constant_diffuse_power",1.f,16.f);
        if (result.diffuse_mod.diffuse_power<=0) throw std::runtime_error("Invalid constant_diffuse_power");
        if (binary::optional(effect,"use_diffuse_attenuation") && bool(parameter(effect,"use_diffuse_attenuation",configs::t_boolean))) {
            result.diffuse_mod.attenuation_enabled=true;
            if (binary::optional(effect,"constant_diffuse_attenuation_color")) {
                std::memcpy(result.diffuse_mod.attenuation_color.data(),static_cast<pcvoid>(parameter(effect,"constant_diffuse_attenuation_color",configs::t_float4).data),16);
                for (float c : result.diffuse_mod.attenuation_color) if (!std::isfinite(c) || c<0 || c>16) throw std::runtime_error("Invalid constant_diffuse_attenuation_color");
            }
            result.diffuse_mod.attenuation_multiplier=scalar("constant_diffuse_attenuation_color_multiplier",1.f,16.f);
        } else {
            if (binary::optional(effect,"constant_diffuse_attenuation_color")) parameter(effect,"constant_diffuse_attenuation_color",configs::t_float4);
            if (binary::optional(effect,"constant_diffuse_attenuation_color_multiplier")) scalar("constant_diffuse_attenuation_color_multiplier",1.f,16.f);
        }
        if (binary::optional(effect,"use_sequence") && bool(parameter(effect,"use_sequence",configs::t_boolean))) {
            result.sequence.enabled=true;
            result.sequence.width=scalar("constant_sequence_array_width",1.f,64.f);
            result.sequence.height=scalar("constant_sequence_array_height",1.f,64.f);
            if (result.sequence.width<1 || result.sequence.height<1) throw std::runtime_error("Invalid sequence grid dimensions");
            if (binary::optional(effect,"constant_sequence_start_frame_index")) {
                float v=float(parameter(effect,"constant_sequence_start_frame_index",configs::t_float));
                if (!std::isfinite(v)) throw std::runtime_error("Invalid constant_sequence_start_frame_index");
                result.sequence.start_frame=v;
            }
            if (binary::optional(effect,"constant_sequence_play_speed")) {
                float v=float(parameter(effect,"constant_sequence_play_speed",configs::t_float));
                if (!std::isfinite(v) || std::abs(v)>1024) throw std::runtime_error("Invalid constant_sequence_play_speed");
                result.sequence.play_speed=v;
            }
        }
        if (binary::optional(effect,"use_variation_mask") && bool(parameter(effect,"use_variation_mask",configs::t_boolean))) {
            result.variation.enabled=true;
            if (binary::optional(effect,"texture_variation_mask")) {
                result.variation_mask_texture_name=pcstr(parameter(effect,"texture_variation_mask",configs::t_string));
                if (!result.variation_mask_texture_name.empty()) binary::validate_path(result.variation_mask_texture_name);
            }
            if (binary::optional(effect,"constant_variation_position_devider")) {
                float v=float(parameter(effect,"constant_variation_position_devider",configs::t_float));
                if (!std::isfinite(v) || std::abs(v)<1e-4f || std::abs(v)>1e5f) throw std::runtime_error("Invalid constant_variation_position_devider");
                result.variation.position_divider=v;
            }
            if (binary::optional(effect,"constant_variation_rotate")) {
                float v=float(parameter(effect,"constant_variation_rotate",configs::t_float));
                if (!std::isfinite(v)) throw std::runtime_error("Invalid constant_variation_rotate");
                result.variation.rotate=v;
            }
            result.variation.scale=scalar("constant_variation_scale",1.f,4096.f);
            result.variation.multiply=scalar("constant_variation_multiply",1.f,16.f);
            if (binary::optional(effect,"constant_variation_color")) {
                std::memcpy(result.variation.color.data(),static_cast<pcvoid>(parameter(effect,"constant_variation_color",configs::t_float4).data),16);
                for (float c : result.variation.color) if (!std::isfinite(c) || c<0 || c>16) throw std::runtime_error("Invalid constant_variation_color");
            }
        } else if (binary::optional(effect,"texture_variation_mask")) {
            parameter(effect,"texture_variation_mask",configs::t_string);
        }
        bool specular_color=binary::optional(effect,"constant_specular_color"),specular_multiplier=binary::optional(effect,"constant_specular_color_multiplier");
        if (specular_color!=specular_multiplier) throw std::runtime_error("Specular color requires both color and multiplier");
        if (specular_color) {
            std::array<float,4> col; std::array<float,3> multiplier;
            std::memcpy(col.data(),static_cast<pcvoid>(parameter(effect,"constant_specular_color",configs::t_float4).data),sizeof(col));
            std::memcpy(multiplier.data(),static_cast<pcvoid>(parameter(effect,"constant_specular_color_multiplier",configs::t_float3).data),sizeof(multiplier));
            for (float channel : col) if (!std::isfinite(channel) || channel<0 || channel>1) throw std::runtime_error("Invalid specular color");
            for (unsigned i=0;i<3;++i) {
                if (!std::isfinite(multiplier[i]) || multiplier[i]<0 || multiplier[i]>16) throw std::runtime_error("Invalid specular color multiplier");
                result.surface.specular_color[i]=col[i]*multiplier[i];
            }
        }
        if (binary::optional(effect,"specular_color_multiplier")) {
            std::array<float,4> mult;
            std::memcpy(mult.data(),static_cast<pcvoid>(parameter(effect,"specular_color_multiplier",configs::t_float4).data),sizeof(mult));
            for (unsigned i=0;i<3;++i) {
                if (!std::isfinite(mult[i]) || mult[i]<0 || mult[i]>16) throw std::runtime_error("Invalid specular_color_multiplier");
                result.surface.specular_color[i]=std::min(16.f,result.surface.specular_color[i]*mult[i]);
            }
        }
        bool emission=binary::optional(effect,"use_emissive") && bool(parameter(effect,"use_emissive",configs::t_boolean));
        bool emission_map=binary::optional(effect,"use_emissive_map") && bool(parameter(effect,"use_emissive_map",configs::t_boolean));
        if (binary::optional(effect,"texture_emissive")) parameter(effect,"texture_emissive",configs::t_string);
        if (emission && emission_map && binary::optional(effect,"texture_emissive")) {
            result.emissive_texture_name=pcstr(parameter(effect,"texture_emissive",configs::t_string));
            if (!result.emissive_texture_name.empty()) binary::validate_path(result.emissive_texture_name);
        }
        bool emission_color=binary::optional(effect,"constant_emissive"),emission_multiplier=binary::optional(effect,"constant_emissive_multiplier");
        if (emission_color!=emission_multiplier || (emission && !emission_color)) throw std::runtime_error("Emission requires both color and multiplier");
        if (emission_color) {
            std::array<float,4> col;
            std::memcpy(col.data(),static_cast<pcvoid>(parameter(effect,"constant_emissive",configs::t_float4).data),sizeof(col));
            float multiplier=scalar("constant_emissive_multiplier",1,16);
            for (float channel : col) if (!std::isfinite(channel) || channel<0 || channel>1) throw std::runtime_error("Invalid constant_emissive");
            if (emission) for (unsigned i=0;i<3;++i) result.contributions.emission[i]=col[i]*multiplier;
        }
        bool use_type=!binary::optional(effect,"use_fresnel_material_type") || bool(parameter(effect,"use_fresnel_material_type",configs::t_boolean));
        if (binary::optional(effect,"fresnel_material_type")) {
            s32 type=parameter(effect,"fresnel_material_type",configs::t_integer);
            if (type<0 || type>2) throw std::runtime_error("Unsupported Fresnel material type");
            if (use_type) result.surface.fresnel_at_0_degree=type==0 ? .99f : type==1 ? .1f : .04f;
        }
        for (auto const& option : effect) {
            std::string name=option.key();
            if (name=="effect_id" || name=="use_tdiffuse" || name=="constant_diffuse" || name=="two_sided" || name=="texture_diffuse"
                || name=="use_alpha_test" || name=="alpha_ref" || name=="constant_tile_u" || name=="constant_tile_v"
                || name=="constant_specular_power" || name=="constant_roughness_v_direction" || name=="is_anisotropic_material"
                || name=="fresnel_at_0_degree" || name=="use_fresnel_material_type" || name=="fresnel_material_type"
                || name=="min_roughness" || name=="max_roughness" || name=="use_nmap" || name=="texture_normal"
                || name=="use_tspecular_power" || name=="texture_specular_power" || name=="use_tspecular_inensity"
                || name=="texture_specular_intensity" || name=="constant_specular_color" || name=="constant_specular_color_multiplier"
                || name=="specular_color_multiplier"
                || name=="use_detail_nmap" || name=="texture_detail_normal" || name=="scale_detail_normal" || name=="tile_detail_normal"
                || name=="use_tditail_diffuse" || name=="texture_ditail_diffuse"
                || name=="use_fresnel_at_0_degree_texture" || name=="texture_fresnel_at_0_degree"
                || name=="use_anisotropic_direction_texture" || name=="anisotropic_direction_texture"
                || name=="use_diffuse_attenuation" || name=="constant_diffuse_attenuation_color" || name=="constant_diffuse_attenuation_color_multiplier"
                || name=="use_tdiffuse_power" || name=="texture_diffuse_power" || name=="constant_diffuse_power"
                || name=="use_sequence" || name=="constant_sequence_array_width" || name=="constant_sequence_array_height"
                || name=="constant_sequence_start_frame_index" || name=="constant_sequence_play_speed"
                || name=="use_variation_mask" || name=="texture_variation_mask" || name=="constant_variation_position_devider"
                || name=="constant_variation_rotate" || name=="constant_variation_scale" || name=="constant_variation_multiply" || name=="constant_variation_color"
                || name=="use_parallax" || name=="texture_bump" || name=="constant_parallax_scale"
                || name=="use_ttranslucency" || name=="texture_translucency" || name=="constant_translucency" || name=="alpha_model_parameter"
                || name=="texture_whirl"
                || name=="use_reflection" || name=="use_reflection_diffuse" || name=="use_reflection_mask"
                || name=="texture_cubemap" || name=="texture_cubemap_mask" || name=="min_reflection_angle" || name=="reflection_power"
                || name=="use_emissive" || name=="use_emissive_map" || name=="texture_emissive" || name=="constant_emissive" || name=="constant_emissive_multiplier") continue;
            if (name.starts_with("use_") || name=="is_anisotropic_material") {
                if (bool(parameter(effect,name.c_str(),configs::t_boolean))) throw std::runtime_error("Unsupported active material feature: "+name);
            } else throw std::runtime_error("Unsupported material parameter: "+name);
        }
    }
    if (has_lighting) {
        result.stages_mask|=material_stage_lighting|material_stage_gbuffer;
        parse_lighting_stage(binary::field(material,"lighting",configs::t_table_named),result);
    }
    if (has_f_stage) {
        result.stages_mask|=material_stage_forward;
        forward_stage_into(binary::field(material,"f_stage",configs::t_table_named),result,true);
    }
    if (has_shadow) {
        result.stages_mask|=material_stage_shadow;
        auto const& sstage=binary::field(material,"shadow",configs::t_table_named);
        if (binary::optional(sstage,"is_cast_shadow"))
            result.configuration.cast_shadow=bool(parameter(sstage,"is_cast_shadow",configs::t_boolean));
    }
    return result;
}
material_cook::material_cook(std::shared_ptr<scene_registry> owner)
    : translate_query_cook(resources::material_class,reuse_false,use_current_thread_id),m_owner(std::move(owner)),m_allocator(render::g_allocator) {}
void material_cook::finish(resources::query_result_for_cook& query,material_configuration const& config,surface_material_parameters const& surface,surface_material_textures const& textures,material_contributions const& contributions,material_blend_mode blend,decal_material_parameters const& decal,distortion_material_parameters const& distortion,std::string identity) {
    if (!m_owner->accepting()) throw std::runtime_error("Material cook session is closed");
    m_owner->track_material(identity);
    if(decal.enabled){auto* result=XRAY_NEW_IMPL(*m_allocator,decal_material_resource)(m_owner,config,textures,decal,std::move(identity));
        query.set_unmanaged_resource(result,resources::nocache_memory,sizeof(*result));query.finish_query(result_success);return;}
    auto* result=XRAY_NEW_IMPL(*m_allocator,material_resource)(m_owner,config,surface,textures,contributions,blend,distortion,std::move(identity));
    query.set_unmanaged_resource(result,resources::nocache_memory,sizeof(*result)); query.finish_query(result_success);
}
void material_cook::load_bytes(resources::query_result_for_cook& query,const_buffer source,std::string identity) {
    if (!m_owner->accepting()) throw std::runtime_error("Material cook session is closed");
    auto asset=load_material_asset(source,*m_allocator);
    std::array<std::string,22> paths={
        asset.texture_name,asset.normal_texture_name,asset.roughness_texture_name,asset.specular_texture_name,asset.detail_normal_texture_name,
        asset.emissive_texture_name,asset.detail_diffuse_texture_name,asset.variation_mask_texture_name,
        asset.fresnel_texture_name,asset.anisotropy_dir_texture_name,asset.diffuse_power_texture_name,
        asset.bump_texture_name,asset.translucency_texture_name,asset.whirl_texture_name,asset.transparency_texture_name,
        asset.cubemap_texture_name,asset.cubemap_mask_texture_name,asset.subdermal_texture_name,asset.ao_texture_name,
        asset.back_illumination_texture_name,asset.scattering_amount_texture_name,asset.scattering_depth_texture_name};
    std::array<resources::user_data_variant,22> user;
    std::vector<resources::request> requests; std::vector<resources::user_data_variant const*> users; std::vector<unsigned> slots;
    for (unsigned i=0;i<paths.size();++i) if (!paths[i].empty()) {
        texture_configuration configuration;
        configuration.force_linear=i==1 || i==2 || i==4 || i==8 || i==9 || i==10 || i==11 || i==12 || i==14 || i==16 || i==18 || i==20 || i==21 || asset.distortion.enabled;
        configuration.require_rgba=(i!=15) && (asset.decal.enabled || asset.distortion.enabled || asset.blend!=material_blend_mode::opaque);
        user[i].set(configuration);
        requests.push_back({paths[i].c_str(),resources::texture_class}); users.push_back(&user[i]); slots.push_back(i);
    }
    auto apply_params=[&](surface_material_textures& t){
        t.g_effect=asset.g_effect;
        t.detail=asset.detail; t.sequence=asset.sequence; t.variation=asset.variation; t.diffuse_mod=asset.diffuse_mod;
        t.parallax=asset.parallax; t.translucency_params=asset.translucency_params; t.forward=asset.forward;
        t.reflection=asset.reflection; t.sss=asset.sss;
        t.blend_with_geometry_normals=asset.blend_with_geometry_normals; t.stages_mask=asset.stages_mask;
    };
    if (requests.empty()) {
        surface_material_textures textures;
        apply_params(textures);
        finish(query,asset.configuration,asset.surface,textures,asset.contributions,asset.blend,asset.decal,asset.distortion,identity); return;
    }
    resources::query_resources(requests.data(),u32(requests.size()),[this,&query,config=asset.configuration,surface=asset.surface,g_effect=asset.g_effect,detail=asset.detail,sequence=asset.sequence,variation=asset.variation,diffuse_mod=asset.diffuse_mod,parallax=asset.parallax,translucency_params=asset.translucency_params,forward=asset.forward,reflection=asset.reflection,sss=asset.sss,blend_geo=asset.blend_with_geometry_normals,stages_mask=asset.stages_mask,contributions=asset.contributions,blend=asset.blend,decal=asset.decal,distortion=asset.distortion,slots,identity](resources::queries_result& data) mutable {
        try {
            if (!data.is_successful()) { query.finish_query(result_error); return; }
            surface_material_textures textures;
            textures.g_effect=g_effect;
            textures.detail=detail; textures.sequence=sequence; textures.variation=variation; textures.diffuse_mod=diffuse_mod;
            textures.parallax=parallax; textures.translucency_params=translucency_params; textures.forward=forward;
            textures.reflection=reflection; textures.sss=sss;
            textures.blend_with_geometry_normals=blend_geo; textures.stages_mask=stages_mask;
            for (unsigned i=0;i<slots.size();++i) {
                auto resource=data[i].get_unmanaged_resource();
                switch (slots[i]) {
                case 0: config.texture=resource; break;
                case 1: textures.normal=resource; break;
                case 2: textures.roughness=resource; break;
                case 3: textures.specular=resource; break;
                case 4: textures.detail_normal=resource; break;
                case 5: textures.emissive=resource; break;
                case 6: textures.detail_diffuse=resource; break;
                case 7: textures.variation_mask=resource; break;
                case 8: textures.fresnel=resource; break;
                case 9: textures.anisotropy_dir=resource; break;
                case 10: textures.diffuse_power=resource; break;
                case 11: textures.bump=resource; break;
                case 12: textures.translucency=resource; break;
                case 13: textures.whirl=resource; break;
                case 14: textures.transparency=resource; break;
                case 15: textures.cubemap=resource; break;
                case 16: textures.cubemap_mask=resource; break;
                case 17: textures.subdermal=resource; break;
                case 18: textures.ao=resource; break;
                case 19: textures.back_illumination=resource; break;
                case 20: textures.scattering_amount=resource; break;
                default: textures.scattering_depth=resource; break;
                }
            }
            finish(query,config,surface,textures,contributions,blend,decal,distortion,identity);
        } catch (std::exception const& e) { failed(query,e); }
    },m_allocator,users.data(),&query,assert_on_fail_false);
}
void material_cook::load_file(resources::query_result_for_cook& query,std::string const& path) {
    if(auto replacement=m_owner->material_override(path)) {
        if(auto* decal=dynamic_cast<decal_material_resource*>(replacement.c_ptr())) {
            finish(query,decal->base,{},decal->textures,{},material_blend_mode::opaque,decal->parameters,{},path);return;
        }
        auto* material=static_cast<material_resource*>(replacement.c_ptr());
        finish(query,*material->data,material->surface,material->textures,material->contributions,material->blend,{},material->distortion,path);return;
    }
    auto cooked=binary::converted_path(path);
    resources::query_vfs_iterator(fs_new::virtual_path_string(cooked.c_str()),[this,&query,cooked,path](vfs::vfs_locked_iterator const& it) {
        bool source=it.is_end();auto name=source?path:cooked;
        resources::query_resource(name.c_str(),resources::raw_data_class_no_reuse,[this,&query,path,source](resources::queries_result& data) {
            try {
                if(!data.is_successful()){query.finish_query(result_error);return;}
                resources::pinned_ptr_const<u8> raw(data[0].get_managed_resource());
                if(!source){load_bytes(query,raw,path);return;}
                original::files files;files["entry"]={raw.c_ptr(),raw.c_ptr()+raw.size()};
                auto config=original::static_material(original::read_source_config(files,"entry"));auto bytes=original::binary_config(config);
                load_bytes(query,const_buffer(bytes.data(),bytes.size()),path);
            }catch(std::exception const& e){failed(query,e);}
        },m_allocator,nullptr,&query,assert_on_fail_false);
    },m_allocator,resources::recursive_false,&query);
}
void material_cook::translate_query(resources::query_result_for_cook& query) {
    try {
        if (!m_owner->accepting()) throw std::runtime_error("Material cook session is closed");
        if (query.user_data()) {
            if (auto* request=query.user_data()->get_if<forward_material_request>()) {
                if (!request->base || request->blend==material_blend_mode::opaque)
                    throw std::runtime_error("Forward request requires a base and alpha/additive blend mode");
                finish(query,*request->base,{},{},{},request->blend); return;
            }
            if (auto* config=query.user_data()->get_if<sky_material_configuration>()) {
                auto* result=XRAY_NEW_IMPL(*m_allocator,sky_material_resource)(m_owner,*config);
                query.set_unmanaged_resource(result,resources::nocache_memory,sizeof(*result)); query.finish_query(result_success); return;
            }
            if (auto* request=query.user_data()->get_if<contributing_material_request>()) {
                if (!request->material || !request->material->base) throw std::runtime_error("Contributing material requires a surface request and base configuration");
                auto const& material=*request->material;
                finish(query,*material.base,material.surface,material.textures ? *material.textures : surface_material_textures{},request->contributions); return;
            }
            if (auto* request=query.user_data()->get_if<surface_material_request>()) {
                if (!request->base) throw std::runtime_error("Surface material request requires a base configuration");
                finish(query,*request->base,request->surface,request->textures ? *request->textures : surface_material_textures{}); return;
            }
            auto* config=query.user_data()->get_if<material_configuration>();
            if (!config) throw std::runtime_error("Unknown material cook user data"); finish(query,*config); return;
        }
        if (auto bytes=query.creation_data_from_user()) { load_bytes(query,bytes); return; }
        std::string name=query.get_requested_path(); binary::validate_path(name);
        if (name.ends_with(".material")) { load_file(query,name); return; }
        auto instance="resources/material_instances/"+name+".material";
        if(m_owner->material_override(instance)){load_file(query,instance);return;}
        auto cooked_instance=binary::converted_path(instance);
        resources::query_vfs_iterator(fs_new::virtual_path_string(cooked_instance.c_str()),[this,&query,name,instance](vfs::vfs_locked_iterator const& it) {
            try {
                if (!m_owner->accepting()) throw std::runtime_error("Material cook session is closed");
                if (!it.is_end()) { load_file(query,instance); return; }
                resources::query_vfs_iterator(fs_new::virtual_path_string(instance.c_str()),[this,&query,name](vfs::vfs_locked_iterator const& source) {
                    try {
                        auto base="resources/materials/"+name+".material";
                        if(source.is_end()){load_file(query,base);return;}
                        // Keep the accepted prepared base binding for aliases whose original
                        // instance has no supported converted counterpart. Explicit instance
                        // source previews still pass through the strict source cook.
                        auto cooked_base=binary::converted_path(base);
                        resources::query_vfs_iterator(fs_new::virtual_path_string(cooked_base.c_str()),[this,&query,base,name](vfs::vfs_locked_iterator const& cooked){
                            try{load_file(query,cooked.is_end()?"resources/material_instances/"+name+".material":base);}catch(std::exception const& e){failed(query,e);}
                        },m_allocator,resources::recursive_false,&query);
                    } catch (std::exception const& e) { failed(query,e); }
                },m_allocator,resources::recursive_false,&query);
            } catch (std::exception const& e) { failed(query,e); }
        },m_allocator,resources::recursive_false,&query);
    } catch (std::exception const& e) { failed(query,e); }
}
void material_cook::delete_resource(resources::resource_base* resource) {
    if(auto* decal=dynamic_cast<decal_material_resource*>(resource)){XRAY_DELETE_IMPL(*m_allocator,decal);return;}
    if (auto* sky=dynamic_cast<sky_material_resource*>(resource)) { XRAY_DELETE_IMPL(*m_allocator,sky); return; }
    auto* result=static_cast<material_resource*>(resource); XRAY_DELETE_IMPL(*m_allocator,result);
}
} } }
