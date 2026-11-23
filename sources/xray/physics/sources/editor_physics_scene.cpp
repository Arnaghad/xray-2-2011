#include "pch.h"
#include <xray/physics/editor_physics_scene.h>
#include "bullet_include.h"
#include <xray/physics/bullet_utils.h>
#include <thread>
namespace xray::physics {
struct editor_physics_scene::implementation {
    std::thread::id owner=std::this_thread::get_id();
    btDefaultCollisionConfiguration config;
    btCollisionDispatcher dispatcher{&config};
    btDbvtBroadphase broadphase;
    btSequentialImpulseConstraintSolver solver;
    btDiscreteDynamicsWorld world{&dispatcher,&broadphase,&solver,&config};
    struct body {
        struct triangle_mesh_storage {
            std::vector<float> vertices;
            std::vector<int> indices;
            std::unique_ptr<btTriangleIndexVertexArray> mesh_interface;
        };
        std::unique_ptr<triangle_mesh_storage> mesh_storage;
        std::vector<std::unique_ptr<btCollisionShape>> shapes;
        std::unique_ptr<btCompoundShape> compound;
        std::unique_ptr<btDefaultMotionState> motion;
        std::unique_ptr<btRigidBody> rigid;
        math::float3 center;
        float scale=1;
    };
    std::vector<body> bodies;
    implementation(){world.setGravity(btVector3(0,-9.81f,0));}
    ~implementation(){for(auto& body:bodies)world.removeRigidBody(body.rigid.get());}
    void check() const {if(owner!=std::this_thread::get_id())throw std::runtime_error("Physics world requires its owner thread");}
};
editor_physics_scene::editor_physics_scene():state(std::make_unique<implementation>()){}
editor_physics_scene::~editor_physics_scene()=default;
unsigned editor_physics_scene::add(model_physics_parameters const& source,math::float4x4 const& matrix) {
    state->check();source.validate();if(source.primitives.empty() || state->bodies.size()>=4096)throw std::runtime_error("Physics body requires bounded collision geometry");
    for(auto const& row:matrix.elements)for(float value:row)if(!std::isfinite(value))throw std::runtime_error("Nonfinite physics placement");
    if(matrix.e03 || matrix.e13 || matrix.e23 || matrix.e33!=1)throw std::runtime_error("Physics placement must be affine");
    float scale=matrix.i.xyz().length();auto rigid=matrix;
    if(scale<1e-5f || scale>1e4f || std::abs(matrix.j.xyz().length()-scale)>scale*1e-4f || std::abs(matrix.k.xyz().length()-scale)>scale*1e-4f)throw std::runtime_error("Physics placement requires uniform positive scale");
    rigid.i.xyz()/=scale;rigid.j.xyz()/=scale;rigid.k.xyz()/=scale;
    if(math::dot_product(math::cross_product(rigid.i.xyz(),rigid.j.xyz()),rigid.k.xyz())<.999f || std::abs(math::dot_product(rigid.i.xyz(),rigid.j.xyz()))>1e-4f || std::abs(math::dot_product(rigid.i.xyz(),rigid.k.xyz()))>1e-4f || std::abs(math::dot_product(rigid.j.xyz(),rigid.k.xyz()))>1e-4f)throw std::runtime_error("Physics placement must be a rigid rotation and uniform positive scale");
    implementation::body body;body.center=source.mass_center*scale;body.scale=scale;body.compound=std::make_unique<btCompoundShape>();
    for(auto const& p:source.primitives){std::unique_ptr<btCollisionShape> shape;
        if(p.type==0)shape=std::make_unique<btSphereShape>(p.scale.x*scale);
        else if(p.type==1)shape=std::make_unique<btBoxShape>(btVector3(p.scale.x*scale,p.scale.y*scale,p.scale.z*scale));
        else shape=std::make_unique<btCylinderShape>(btVector3(p.scale.y*scale,p.scale.x*scale,p.scale.y*scale));
        auto local=math::create_rotation(p.rotation)*math::create_translation(p.position*scale);body.compound->addChildShape(from_xray(local),shape.get());body.shapes.push_back(std::move(shape));
    }
    auto center=math::create_translation(body.center)*rigid;auto transform=from_xray(center);btVector3 inertia(0,0,0);if(source.mass>0)body.compound->calculateLocalInertia(source.mass,inertia);
    body.motion=std::make_unique<btDefaultMotionState>(transform);btRigidBody::btRigidBodyConstructionInfo info(source.mass,body.motion.get(),body.compound.get(),inertia);info.m_friction=source.friction;info.m_restitution=source.restitution;info.m_linearDamping=source.linear_damping;info.m_angularDamping=source.angular_damping;
    info.m_linearSleepingThreshold=source.linear_sleeping_threshold;info.m_angularSleepingThreshold=source.angular_sleeping_threshold;info.m_additionalDamping=source.additional_damping;info.m_additionalDampingFactor=source.additional_damping_factor;info.m_additionalLinearDampingThresholdSqr=source.additional_linear_threshold;info.m_additionalAngularDampingThresholdSqr=source.additional_angular_threshold;info.m_additionalAngularDampingFactor=source.additional_angular_factor;
    body.rigid=std::make_unique<btRigidBody>(info);state->bodies.push_back(std::move(body));state->world.addRigidBody(state->bodies.back().rigid.get());return unsigned(state->bodies.size()-1);
}
unsigned editor_physics_scene::add_static_triangles(std::span<math::float3 const> vertices,std::span<u32 const> indices,math::float4x4 const& matrix,float friction,float restitution) {
    state->check();
    if(vertices.size()<3 || indices.size()<3 || (indices.size()%3)!=0 || state->bodies.size()>=4096)
        throw std::runtime_error("Static triangle collider requires valid non-empty triangle soup");
    if(!std::isfinite(friction) || friction<0 || !std::isfinite(restitution) || restitution<0 || restitution>1)
        throw std::runtime_error("Invalid static triangle material parameters");
    for(auto const& row:matrix.elements)for(float value:row)if(!std::isfinite(value))throw std::runtime_error("Nonfinite static triangle transform");
    if(matrix.e03 || matrix.e13 || matrix.e23 || matrix.e33!=1)throw std::runtime_error("Static triangle placement must be affine");
    auto storage=std::make_unique<implementation::body::triangle_mesh_storage>();
    storage->vertices.reserve(vertices.size()*3);
    for(auto const& v:vertices){
        if(!std::isfinite(v.x) || !std::isfinite(v.y) || !std::isfinite(v.z))throw std::runtime_error("Nonfinite terrain triangle vertex");
        auto bv=from_xray(v);
        storage->vertices.push_back(bv.x());storage->vertices.push_back(bv.y());storage->vertices.push_back(bv.z());
    }
    storage->indices.reserve(indices.size());
    for(u32 idx:indices){
        if(idx>=vertices.size())throw std::runtime_error("Static triangle index out of range");
        storage->indices.push_back(int(idx));
    }
    int const tri_count=int(storage->indices.size()/3);
    int const vert_count=int(vertices.size());
    storage->mesh_interface=std::make_unique<btTriangleIndexVertexArray>(
        tri_count,storage->indices.data(),int(3*sizeof(int)),vert_count,storage->vertices.data(),int(3*sizeof(float)));
    auto shape=std::make_unique<btBvhTriangleMeshShape>(storage->mesh_interface.get(),true);
    implementation::body body;
    body.center={0,0,0};body.scale=1;
    btCollisionShape* shape_ptr=shape.get();
    body.shapes.push_back(std::move(shape));
    body.mesh_storage=std::move(storage);
    body.motion=std::make_unique<btDefaultMotionState>(from_xray(matrix));
    btRigidBody::btRigidBodyConstructionInfo info(0.f,body.motion.get(),shape_ptr,btVector3(0,0,0));
    info.m_friction=friction;info.m_restitution=restitution;
    body.rigid=std::make_unique<btRigidBody>(info);
    state->bodies.push_back(std::move(body));
    state->world.addRigidBody(state->bodies.back().rigid.get());
    return unsigned(state->bodies.size()-1);
}
void editor_physics_scene::step(float seconds){state->check();if(!std::isfinite(seconds) || seconds<0 || seconds>.1f)throw std::runtime_error("Invalid physics timestep");state->world.stepSimulation(seconds,12,1.f/120);}
math::float4x4 editor_physics_scene::transform(unsigned index) const {state->check();if(index>=state->bodies.size())throw std::runtime_error("Unknown physics body");auto const& body=state->bodies[index];return math::create_scale(math::float3(body.scale,body.scale,body.scale))*math::create_translation(-body.center)*from_bullet(body.rigid->getWorldTransform());}
math::aabb editor_physics_scene::bounds(unsigned index) const {state->check();if(index>=state->bodies.size())throw std::runtime_error("Unknown physics body");btVector3 low,high;state->bodies[index].rigid->getAabb(low,high);auto box=math::create_zero_aabb();auto a=from_bullet(low),b=from_bullet(high);box.min={std::min(a.x,b.x),std::min(a.y,b.y),std::min(a.z,b.z)};box.max={std::max(a.x,b.x),std::max(a.y,b.y),std::max(a.z,b.z)};return box;}
unsigned editor_physics_scene::size() const {state->check();return unsigned(state->bodies.size());}
}
