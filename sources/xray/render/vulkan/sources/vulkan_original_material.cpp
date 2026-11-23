#include "pch.h"
#include "vulkan_original_static_assets.h"
#include <algorithm>
#include <cmath>
#include <set>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan { namespace original {
namespace {
value unwrap(value v) {
    if(v.type!=3)return v;
    for(auto const& child:v.children)if(child.name!="value" && (child.name!="is_model_parameter" || child.type!=0))
        throw std::runtime_error("Unsupported original parameter metadata: "+v.name+"."+child.name);
    auto child=v.field("value");child.name=v.name;return child;
}
value make_value(std::string name,u16 type,double number=0,std::string text={},std::vector<float> vector={}) {
    value v;v.name=std::move(name);v.type=type;v.number=number;v.text=std::move(text);v.vector=std::move(vector);return v;
}
bool has_field(value const& table,pcstr name) {
    return std::any_of(table.children.begin(),table.children.end(),[&](value const& c){return c.name==name;});
}
bool flag_value(value const& table,pcstr name,bool fallback=false) {
    for(auto const& field:table.children)if(field.name==name) {
        if(field.type!=0)throw std::runtime_error(std::string("Mistyped original material flag: ")+name);
        return field.number!=0;
    }
    return fallback;
}
double number_value(value const& table,pcstr name,double fallback) {
    for(auto const& field:table.children)if(field.name==name) {
        if(field.type!=1 && field.type!=2)throw std::runtime_error(std::string("Mistyped original material number: ")+name);
        if(!std::isfinite(field.number))throw std::runtime_error(std::string("Non-finite original material number: ")+name);
        return field.number;
    }
    return fallback;
}
std::string text_value(value const& table,pcstr name,std::string fallback={}) {
    for(auto const& field:table.children)if(field.name==name) {
        if(field.type!=5)throw std::runtime_error(std::string("Mistyped original material text: ")+name);
        return field.text;
    }
    return fallback;
}
bool supported_stage_name(std::string const& name) {
    return name=="g_stage" || name=="forward" || name=="f_stage" || name=="decals" || name=="distortion" || name=="lighting" || name=="shadow";
}
value translate_g_stage(value stage) {
    value output_stage;output_stage.name="g_stage";
    for(auto field:stage.children)if(field.name!="effect") {
        field=unwrap(std::move(field));
        if(field.name=="is_cast_shadow") {
            if(field.type!=0)throw std::runtime_error("Unsupported original g_stage metadata: is_cast_shadow");
            output_stage.children.push_back(std::move(field));
            continue;
        }
        bool known=field.name=="is_diffuse_attenuation" || field.name=="is_emissive";
        if(!known || field.type!=0 || bool(field.number))
            throw std::runtime_error("Unsupported original g_stage metadata: "+field.name);
    }
    auto effect=stage.field("effect");for(auto& field:effect.children)field=unwrap(std::move(field));
    std::set<std::string> const floats={"alpha_ref","alpha_model_parameter","constant_parallax_scale","constant_translucency",
        "constant_tile_u","constant_tile_v","constant_specular_power",
        "constant_roughness_v_direction","fresnel_at_0_degree","min_roughness","max_roughness","scale_detail_normal",
        "tile_detail_normal","constant_emissive_multiplier","constant_diffuse_attenuation_color_multiplier",
        "constant_diffuse_power","constant_sequence_array_height","constant_sequence_array_width",
        "constant_sequence_play_speed","constant_sequence_start_frame_index","constant_variation_multiply",
        "constant_variation_position_devider","constant_variation_rotate","constant_variation_scale",
        "min_reflection_angle","reflection_power"};
    std::vector<value> kept;
    for(auto field:effect.children) {
        if(field.name=="specular_color_multiplier") {
            if(field.type!=8 || field.vector.size()!=4)throw std::runtime_error("Unsupported original specular_color_multiplier");
        }
        if(floats.contains(field.name) && field.type==1)field.type=2;
        kept.push_back(std::move(field));
    }
    effect.children=std::move(kept);
    output_stage.children.push_back(std::move(effect));
    return output_stage;
}
value translate_forward_stage(value stage,bool is_forward_palette,bool multi_stage=false) {
    for(auto const& field:stage.children)if(field.name!="effect")
        throw std::runtime_error("Unsupported original forward metadata: "+field.name);
    auto effect=stage.field("effect");for(auto& field:effect.children)field=unwrap(std::move(field));
    std::string effect_id=text_value(effect,"effect_id");
    std::set<std::string> const seq_floats={"constant_sequence_array_width","constant_sequence_array_height","constant_sequence_start_frame_index","constant_sequence_play_speed"};
    if(!is_forward_palette) {
        if(effect_id!="f_stage_unlit")throw std::runtime_error("Unsupported forward effect (expected f_stage_unlit)");
        for(auto& field:effect.children)if((field.name=="constant_tile_u" || field.name=="constant_tile_v" || seq_floats.contains(field.name)) && field.type==1)field.type=2;
        value output_stage;output_stage.name="f_stage";output_stage.children.push_back(std::move(effect));
        return output_stage;
    }
    int forward_mode=-1;
    if(effect_id=="forward_default" || effect_id=="forward_default_subuv")forward_mode=0;
    else if(effect_id=="forward_default_tiled")forward_mode=1;
    else if(effect_id=="forward_soft")forward_mode=2;
    else if(effect_id=="forward_fire_fresnel")forward_mode=3;
    else if(effect_id=="forward_smoke_custom_lighted")forward_mode=4;
    else if(effect_id=="forward_volume_cone_base")forward_mode=5;
    else if(effect_id=="forward_volume_sphere_base")forward_mode=6;
    else if(effect_id=="forward_decal")forward_mode=7;
    else throw std::runtime_error("Unsupported forward effect: "+effect_id);
    if(flag_value(effect,"use_tcube"))
        throw std::runtime_error("Unsupported active original material feature: use_tcube");
    double mode_num=number_value(effect,"blend_mode",1);
    s32 mode=static_cast<s32>(mode_num);
    if(mode_num!=mode || mode<0 || mode>4)
        throw std::runtime_error("Unsupported original forward blend_mode");
    bool const volume=(forward_mode==5 || forward_mode==6);
    bool const is_forward_decal=(forward_mode==7);
    std::vector<float> base_color{1.f,1.f,1.f,1.f};
    pcstr color_key=volume ? "constant_volume_color" : is_forward_decal ? "constant_diffuse" : "constant_emissive";
    if(has_field(effect,color_key)) {
        auto const& color=effect.field(color_key);
        if(color.type!=8 || color.vector.size()!=4)throw std::runtime_error(std::string("Mistyped original forward ")+color_key);
        base_color=color.vector;
        for(float c:base_color)if(!std::isfinite(c) || c<0)throw std::runtime_error(std::string("Invalid original forward ")+color_key);
    }
    pcstr mult_key=volume ? "constant_volume_color_multiplier" : "constant_emissive_multiplier";
    double multiplier=is_forward_decal ? 1.0 : number_value(effect,mult_key,1.0);
    if(multiplier<0 || multiplier>64.0)throw std::runtime_error(std::string("Invalid original forward ")+mult_key);
    double alpha=mode==0 ? 1.0 : number_value(effect,"constant_transparency",double(base_color[3]));
    if(alpha<0 || alpha>1.0)throw std::runtime_error("Invalid original forward constant_transparency");
    std::vector<float> diffuse{
        std::clamp(float(base_color[0]),0.f,1.f),
        std::clamp(float(base_color[1]),0.f,1.f),
        std::clamp(float(base_color[2]),0.f,1.f),
        float(alpha)};
    pcstr tex_flag=(volume || is_forward_decal) ? "use_tdiffuse" : "use_temissive";
    pcstr tex_key=(volume || is_forward_decal) ? "texture_diffuse" : "texture_emissive";
    std::string tex_path=text_value(effect,tex_key);
    bool use_diffuse=flag_value(effect,tex_flag) && !tex_path.empty();
    if(flag_value(effect,tex_flag) && tex_path.empty() && has_field(effect,tex_key))
        use_diffuse=false;
    value translated;translated.name="effect";
    translated.children.push_back(make_value("effect_id",5,0,"f_stage_unlit"));
    translated.children.push_back(make_value("blend_mode",1,(mode==0 && !multi_stage && !is_forward_decal)?1:mode));
    translated.children.push_back(make_value("forward_effect_mode",1,forward_mode));
    translated.children.push_back(make_value("constant_diffuse",8,0,{},std::move(diffuse)));
    translated.children.push_back(make_value("constant_emissive_multiplier",2,multiplier));
    translated.children.push_back(make_value("use_tdiffuse",0,use_diffuse?1:0));
    if(use_diffuse)translated.children.push_back(make_value("texture_diffuse",5,0,std::move(tex_path)));
    if(has_field(effect,"blend_with_geometry_normals"))
        translated.children.push_back(make_value("blend_with_geometry_normals",0,flag_value(effect,"blend_with_geometry_normals")?1:0));
    if(flag_value(effect,"use_reflection")) {
        translated.children.push_back(make_value("use_reflection",0,1));
        std::string cube_path=text_value(effect,"texture_cubemap");
        if(!cube_path.empty())translated.children.push_back(make_value("texture_cubemap",5,0,std::move(cube_path)));
        if(has_field(effect,"reflection_power"))translated.children.push_back(make_value("reflection_power",2,number_value(effect,"reflection_power",0.1)));
        if(has_field(effect,"min_reflection_angle"))translated.children.push_back(make_value("min_reflection_angle",2,number_value(effect,"min_reflection_angle",0.0)));
    }
    if(has_field(effect,"two_sided"))translated.children.push_back(make_value("two_sided",0,flag_value(effect,"two_sided")?1:0));
    if(has_field(effect,"constant_tile_u"))translated.children.push_back(make_value("constant_tile_u",2,number_value(effect,"constant_tile_u",1.0)));
    if(has_field(effect,"constant_tile_v"))translated.children.push_back(make_value("constant_tile_v",2,number_value(effect,"constant_tile_v",1.0)));
    std::string transparency_tex=text_value(effect,"texture_transparency");
    bool use_trans=flag_value(effect,"use_ttransparency") && !transparency_tex.empty();
    if(use_trans) {
        translated.children.push_back(make_value("use_ttransparency",0,1));
        translated.children.push_back(make_value("texture_transparency",5,0,std::move(transparency_tex)));
    }
    if(flag_value(effect,"use_alpha_test")) {
        translated.children.push_back(make_value("use_alpha_test",0,1));
        double aref=number_value(effect,"alpha_ref",0.25);
        if(aref<0 || aref>1.0)throw std::runtime_error("Invalid original forward alpha_ref");
        translated.children.push_back(make_value("alpha_ref",2,aref));
    }
    if(flag_value(effect,"use_Color_Power")) {
        translated.children.push_back(make_value("use_Color_Power",0,1));
        double cp=number_value(effect,"constant_color_power",1.0);
        if(cp<=0 || cp>16.0)throw std::runtime_error("Invalid original forward constant_color_power");
        translated.children.push_back(make_value("constant_color_power",2,cp));
    }
    if(flag_value(effect,"use_soft_test") || forward_mode==2) {
        translated.children.push_back(make_value("use_soft_test",0,1));
        double sd=number_value(effect,"constant_soft_distance",1.75);
        if(sd<=0 || sd>1024.0)throw std::runtime_error("Invalid original forward constant_soft_distance");
        translated.children.push_back(make_value("constant_soft_distance",2,sd));
    }
    if(flag_value(effect,"use_fire_fresnel") || forward_mode==3)
        translated.children.push_back(make_value("use_fire_fresnel",0,1));
    if(flag_value(effect,"use_lighting") || forward_mode==4)
        translated.children.push_back(make_value("use_lighting",0,1));
    if(volume) {
        double att=number_value(effect,"attenuation_scale",0.25);
        if(att<0 || att>64.0)throw std::runtime_error("Invalid original forward attenuation_scale");
        translated.children.push_back(make_value("attenuation_scale",2,att));
        if(forward_mode==6) {
            double tile=number_value(effect,"uv_tile",1.0);
            if(std::abs(tile)>4096.0)throw std::runtime_error("Invalid original forward uv_tile");
            translated.children.push_back(make_value("uv_tile",2,tile));
            if(has_field(effect,"move_direction")) {
                auto md=effect.field("move_direction");
                if(md.type!=7 || md.vector.size()!=3)throw std::runtime_error("Mistyped original forward move_direction");
                for(float v:md.vector)if(!std::isfinite(v)||std::abs(v)>4096.f)throw std::runtime_error("Invalid original forward move_direction");
                translated.children.push_back(make_value("move_direction",7,0,{},std::move(md.vector)));
            }
        }
    }
    bool use_seq=(effect_id=="forward_default_subuv") || flag_value(effect,"use_sequence") || flag_value(effect,"use_subuv");
    if(use_seq) {
        translated.children.push_back(make_value("use_sequence",0,1));
        for(auto const& name:seq_floats)if(has_field(effect,name.c_str()))
            translated.children.push_back(make_value(name,2,number_value(effect,name.c_str(),(name.ends_with("width")||name.ends_with("height")||name.ends_with("speed"))?1.0:0.0)));
    }
    value output_stage;output_stage.name="f_stage";output_stage.children.push_back(std::move(translated));
    return output_stage;
}
value translate_decals_stage(value stage) {
    for(auto const& field:stage.children)if(field.name!="effect")
        throw std::runtime_error("Unsupported original decals metadata: "+field.name);
    auto effect=stage.field("effect");for(auto& field:effect.children)field=unwrap(std::move(field));
    if(text_value(effect,"effect_id")!="decals_default")
        throw std::runtime_error("Unsupported original decals effect");
    std::set<std::string> const allowed={"effect_id","use_tdiffuse","use_nmap","use_reflection",
        "blend_with_geometry_normals","constant_diffuse","normal_multiplier","texture_diffuse",
        "texture_normal","texture_cubemap","reflection_power","min_reflection_angle"};
    std::vector<value> kept;
    for(auto& field:effect.children)if(allowed.contains(field.name)) {
        if((field.name=="reflection_power" || field.name=="min_reflection_angle") && field.type==1)field.type=2;
        kept.push_back(std::move(field));
    }
    effect.children=std::move(kept);
    value output_stage;output_stage.name="decals";output_stage.children.push_back(std::move(effect));
    return output_stage;
}
value translate_lighting_stage(value stage) {
    value output_stage;output_stage.name="lighting";
    for(auto field:stage.children) {
        if(field.name=="constant_clear_color") continue;
        if(field.name!="effect")
            throw std::runtime_error("Unsupported original lighting metadata: "+field.name);
        for(auto& param:field.children)param=unwrap(std::move(param));
        std::set<std::string> const floats={"alpha_ref","alpha_model_parameter","constant_tile_u","constant_tile_v",
            "constant_specular_power","constant_roughness_v_direction","fresnel_at_0_degree","min_roughness","max_roughness",
            "scattering_depth","scattering_min_depth","scattering_color_multiplier","subdermal_weight"};
        for(auto& param:field.children) {
            if(floats.contains(param.name) && param.type==1)param.type=2;
        }
        output_stage.children.push_back(std::move(field));
    }
    return output_stage;
}
value translate_shadow_stage(value stage) {
    value output_stage;output_stage.name="shadow";
    for(auto field:stage.children) {
        field=unwrap(std::move(field));
        output_stage.children.push_back(std::move(field));
    }
    return output_stage;
}
value translate_distortion_stage(value stage) {
    for(auto const& field:stage.children)if(field.name!="effect")
        throw std::runtime_error("Unsupported original distortion metadata: "+field.name);
    auto effect=stage.field("effect");for(auto& field:effect.children)field=unwrap(std::move(field));
    std::string effect_id=text_value(effect,"effect_id");
    if(effect_id!="distortion_default" && effect_id!="distortion_panner")
        throw std::runtime_error("Unsupported original distortion effect: "+effect_id);
    if(!has_field(effect,"distortion_scale") || !has_field(effect,"texture_distortion"))
        throw std::runtime_error("Distortion material requires distortion_scale and texture_distortion");
    value translated;translated.name="effect";
    translated.children.push_back(make_value("effect_id",5,0,"distortion_panner"));
    translated.children.push_back(effect.field("distortion_scale"));
    translated.children.push_back(has_field(effect,"panner")?effect.field("panner"):make_value("panner",7,0,{},{0.f,0.f,0.f}));
    translated.children.push_back(effect.field("texture_distortion"));
    value output_stage;output_stage.name="distortion";output_stage.children.push_back(std::move(translated));
    return output_stage;
}
}
value static_material(value source) {
    auto& material=source.field("material");
    std::set<std::string> declared_stages;
    std::vector<std::string> active_stages;
    for(auto const& entry:material.children) {
        if(entry.name=="common") {
            for(auto const& field:entry.children) {
                if(field.name=="description" && field.type==5)continue;
                if(field.name=="stages" && field.type==3) {
                    for(auto const& item:field.children) {
                        if(item.type!=0 && item.type!=1)throw std::runtime_error("Unsupported original stages flag: "+item.name);
                        if(item.number) {
                            if(!supported_stage_name(item.name) || item.number!=1)
                                throw std::runtime_error("Unsupported original material stage metadata: "+item.name);
                            declared_stages.insert(item.name);
                        }
                    }
                    continue;
                }
                throw std::runtime_error("Unsupported original material metadata: common."+field.name);
            }
        } else if(entry.name=="instance") {
            if(entry.children.size()!=1 || entry.field("base_material").type!=5)
                throw std::runtime_error("Unsupported original material instance metadata");
        } else if(supported_stage_name(entry.name)) {
            if(std::find(active_stages.begin(),active_stages.end(),entry.name)!=active_stages.end())
                throw std::runtime_error("Duplicate original material stage: "+entry.name);
            active_stages.push_back(entry.name);
        } else throw std::runtime_error("Unsupported original material stage: "+entry.name);
    }
    if(active_stages.empty())
        throw std::runtime_error("Original material has no active stages");
    for(auto const& declared:declared_stages)
        if(std::find(active_stages.begin(),active_stages.end(),declared)==active_stages.end())
            throw std::runtime_error("Original material declared stage missing: "+declared);
    bool const multi_stage=active_stages.size()>1;
    value result;value output_material;output_material.name="material";
    for(auto const& active_stage:active_stages) {
        if(!declared_stages.empty() && !declared_stages.contains(active_stage))continue;
        value const& stage=material.field(active_stage.c_str());
        value output_stage=active_stage=="g_stage"?translate_g_stage(stage)
            :active_stage=="forward"?translate_forward_stage(stage,true,multi_stage)
            :active_stage=="f_stage"?translate_forward_stage(stage,false,multi_stage)
            :active_stage=="decals"?translate_decals_stage(stage)
            :active_stage=="lighting"?translate_lighting_stage(stage)
            :active_stage=="shadow"?translate_shadow_stage(stage)
            :translate_distortion_stage(stage);
        output_material.children.push_back(std::move(output_stage));
    }
    result.children.push_back(std::move(output_material));
    return result;
}
} } } }
