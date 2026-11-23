#include "pch.h"
#include "vulkan_scene_runtime.h"
#include <xray/render/facade/light_props.h>
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
scene_light describe_light(u32 id,light_props const& props) {
    if (props.type!=light_type_parallel && props.type!=light_type_point && props.type!=light_type_spot)
        throw std::runtime_error("Vulkan supports directional, point and spot lights only");
    if (props.shadower || props.use_with_lpv || props.m_use_auto_lacation)
        throw std::runtime_error("Shadowers, LPV and automatic sun location are not implemented");
    if (props.lighting_model<0 || props.lighting_model>3) throw std::runtime_error("Invalid legacy lighting model");
    for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c)
        if (!std::isfinite(props.transform.elements[r][c])) throw std::runtime_error("Non-finite light transform");
    if (props.transform.elements[0][3]!=0 || props.transform.elements[1][3]!=0 || props.transform.elements[2][3]!=0 || props.transform.elements[3][3]!=1)
        throw std::runtime_error("Light transform must be affine");
    auto bounded=[](float value,float maximum) {
        if (!std::isfinite(value) || value<0 || value>maximum) throw std::runtime_error("Invalid light intensity or influence");
        return value;
    };
    scene_light result; result.id=id;
    result.type=props.type==light_type_parallel ? scene_light_type::directional
        : props.type==light_type_point ? scene_light_type::point : scene_light_type::spot;
    result.intensity=bounded(props.intensity,16); result.diffuse=bounded(props.diffuse_influence_factor,1);
    result.specular=bounded(props.specular_influence_factor,1);
    if (props.does_cast_shadows) {
        result.casts_shadows=true;
        if (result.type==scene_light_type::directional) {
            result.shadow_resolution=props.sun_shadow_map_size ? props.sun_shadow_map_size : 512;
            result.shadow_cascades=props.num_sun_cascades ? props.num_sun_cascades : 4;
        } else {
            if (props.shadow_map_size_index>2) throw std::runtime_error("Local shadow size index must be 0..2");
            // The legacy renderer uses the index, not shadow_map_size, for local lights.
            result.shadow_resolution=1024u>>props.shadow_map_size_index;
            result.shadow_cascades=result.type==scene_light_type::point ? 6 : 1;
            if (result.type==scene_light_type::point) for (unsigned i=0;i<6;++i)
                if (props.shadow_distribution_sides[i]) result.shadow_face_mask|=1u<<i;
            result.shadow_depth_bias=.1f*bounded(props.local_light_z_bias,1);
        }
        if (result.shadow_resolution<64 || result.shadow_resolution>2048 || (result.shadow_resolution & (result.shadow_resolution-1))
            || (result.type==scene_light_type::directional && result.shadow_cascades>4))
            throw std::runtime_error("Directional shadows require power-of-two size 64..2048 and 1..4 cascades");
        result.shadow_transparency=bounded(props.shadow_transparency,1);
    }
    if (result.type!=scene_light_type::point) {
        // Legacy fill_light rotates spot -pi/2 around X: its forward axis is -Y.
        unsigned row=result.type==scene_light_type::spot ? 1 : 2;
        float sign=result.type==scene_light_type::spot ? -1.f : 1.f;
        double length=0;
        for (unsigned i=0;i<3;++i) { double value=props.transform.elements[row][i]; length+=value*value; }
        if (length<1e-12) throw std::runtime_error("Light has no direction");
        for (unsigned i=0;i<3;++i) result.direction[i]=float(sign*props.transform.elements[row][i]/std::sqrt(length));
    }
    if (result.type!=scene_light_type::directional) {
        result.range=bounded(props.range,65536);
        if (result.range<.001f) throw std::runtime_error("Local light range must be at least 0.001");
        if (result.casts_shadows && result.type==scene_light_type::point && result.range<=.025f+math::epsilon_5)
            throw std::runtime_error("Point shadow range must exceed near 0.025 plus projection epsilon");
        result.attenuation_power=bounded(props.attenuation_power,16);
        for (unsigned i=0;i<3;++i) {
            float value=props.transform.elements[3][i];
            if (std::abs(value)>1e6f) throw std::runtime_error("Local light position exceeds the supported bounds");
            result.position[i]=value;
        }
    }
    if (result.type==scene_light_type::spot) {
        float inner=bounded(props.spot_umbra_angle,math::pi),outer=bounded(props.spot_penumbra_angle,math::pi);
        if (outer<.001f || outer>=math::pi || inner>outer) throw std::runtime_error("Invalid spot inner/outer full angles");
        result.inner_cosine=std::cos(inner*.5f); result.outer_cosine=std::cos(outer*.5f); result.spot_outer_angle=outer;
        result.spot_falloff=bounded(props.spot_falloff,16);
    }
    math::color color(props.color); result.color={color.r/255.f,color.g/255.f,color.b/255.f};
    return result;
}
void scene_runtime::add_light(scene_ptr const& value,u32 id,light_props const& props) {
    auto& lights=scene(value).lights;
    if (std::any_of(lights.begin(),lights.end(),[&](auto const& light) { return light.id==id; })) throw std::runtime_error("Duplicate light id");
    if (lights.size()>=16) throw std::runtime_error("Lighting profile supports at most sixteen lights");
    lights.push_back(describe_light(id,props));
}
void scene_runtime::update_light(scene_ptr const& value,u32 id,light_props const& props) {
    auto& lights=scene(value).lights;
    auto it=std::find_if(lights.begin(),lights.end(),[&](auto const& light) { return light.id==id; });
    if (it==lights.end()) throw std::runtime_error("Cannot update a missing light");
    *it=describe_light(id,props);
}
void scene_runtime::remove_light(scene_ptr const& value,u32 id) {
    auto& lights=scene(value).lights;
    auto it=std::find_if(lights.begin(),lights.end(),[&](auto const& light) { return light.id==id; });
    if (it==lights.end()) throw std::runtime_error("Cannot remove a missing light");
    lights.erase(it);
}
} } }
