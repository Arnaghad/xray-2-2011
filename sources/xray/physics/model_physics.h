#pragma once
#include <xray/extensions.h>
#include <vector>
#include <stdexcept>
#include <cmath>
namespace xray::physics {
struct model_collision_primitive {
    u32 type=1;
    math::float3 position={0,0,0},rotation={0,0,0},scale={.5f,.5f,.5f};
};
struct model_physics_parameters {
    float mass=0,friction=.5f,restitution=0,linear_damping=0,angular_damping=0;
    float linear_sleeping_threshold=.8f,angular_sleeping_threshold=1;
    bool additional_damping=false;
    float additional_damping_factor=.005f,additional_linear_threshold=.01f,additional_angular_threshold=.01f,additional_angular_factor=.01f;
    math::float3 mass_center={0,0,0};
    std::vector<model_collision_primitive> primitives;
    void validate() const {
        auto finite=[](math::float3 const& v){for(unsigned i=0;i<3;++i)if(!std::isfinite(v[i]) || std::abs(v[i])>1e5f)throw std::runtime_error("Collision transform exceeds finite bounds");};
        if(!std::isfinite(mass) || mass<0 || mass>1e6f || !std::isfinite(friction) || friction<0 || friction>4 || !std::isfinite(restitution) || restitution<0 || restitution>1 || !std::isfinite(linear_damping) || linear_damping<0 || linear_damping>1 || !std::isfinite(angular_damping) || angular_damping<0 || angular_damping>1 || primitives.size()>128)throw std::runtime_error("Invalid authored rigid-body properties");
        finite(mass_center);
        for(float value:{linear_sleeping_threshold,angular_sleeping_threshold,additional_linear_threshold,additional_angular_threshold})if(!std::isfinite(value) || value<0 || value>1e8f)throw std::runtime_error("Invalid rigid-body sleeping or damping threshold");
        for(float value:{additional_damping_factor,additional_angular_factor})if(!std::isfinite(value) || value<0 || value>1)throw std::runtime_error("Invalid additional damping factor");
        for(auto const& primitive:primitives){finite(primitive.position);finite(primitive.rotation);finite(primitive.scale);if(primitive.type>2 || primitive.scale.x<=0 || (primitive.type==1 && (primitive.scale.y<=0 || primitive.scale.z<=0)) || (primitive.type==2 && primitive.scale.y<=0))throw std::runtime_error("Invalid sphere, box or cylinder collision primitive");}
        if(mass>0 && primitives.empty())throw std::runtime_error("Dynamic model requires collision primitives");
    }
};
}
