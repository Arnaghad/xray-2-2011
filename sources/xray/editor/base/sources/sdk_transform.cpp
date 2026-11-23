#include "pch.h"
#include <xray/editor/base/sdk_transform.h>
#include "transform_control_translation.h"
#include "transform_control_rotation.h"
#include "transform_control_scaling.h"
#include "transform_control_object.h"
#include <stdexcept>

namespace xray::editor_base {
namespace {
struct helper final : transform_control_helper {
    using transform_control_helper::transform_control_helper;
    float4x4 get_inverted_view_matrix() override {return float4x4().identity();}
    float4x4 get_projection_matrix() override {return float4x4().identity();}
    void get_mouse_ray(float3& o,float3& d) override {o=float3(0,0,0);d=float3(0,0,1);}
};
struct operand final : transform_control_object {
    float4x4 anchor,result;
    void start_modify(transform_control_base*) override {}
    void end_modify(transform_control_base*) override {}
    void execute_preview(transform_control_base* control) override {result=control->calculate(anchor);}
    float4x4 get_ancor_transform() override {return anchor;}
    bool translation_ignore_rotation() override {return false;}
};
}
static void transform_in_frame(memory::mimalloc_allocator_type& allocator,render::scene_ptr const& scene,
    render::scene_view_ptr const& view,render::render_output_window_ptr const& output,editor::hosting::sdk_scene_transform& v,math::float4x4 const* frame) {
    if(v.mode<1 || v.mode>3 || v.axis<1 || v.axis>4 || (v.axis==4 && v.mode!=3) || v.local>1 || v.reserved || !std::isfinite(v.amount))
        throw std::runtime_error("Invalid native transform operand");
    if(g_allocator && g_allocator!=&allocator)throw std::runtime_error("Transform allocator changed");
    g_allocator=&allocator;
    helper context(scene.c_ptr(),view.c_ptr(),output.c_ptr());context.m_editor_renderer=nullptr;
    operand object;
    auto read=[](float const* f){return float3(f[0],f[1],f[2]);};
    auto xf=math::create_scale(read(v.scale))*math::create_rotation(read(v.rotation))*math::create_translation(read(v.position));
    auto p=math::create_translation(read(v.pivot))*xf;
    for(auto const& row:p.elements)for(float f:row)if(!std::isfinite(f))throw std::runtime_error("Native transform overflow");
    object.anchor=v.local?p:math::create_translation(p.c.xyz());
    if(frame) {
        auto const& m=*frame;for(auto const& row:m.elements)for(float f:row)if(!std::isfinite(f))throw std::runtime_error("Nonfinite control frame");
        if(m.e03 || m.e13 || m.e23 || m.e33!=1 || std::abs(m.i.xyz().length()-1)>1e-3f || std::abs(m.j.xyz().length()-1)>1e-3f || std::abs(m.k.xyz().length()-1)>1e-3f
            || std::abs(math::dot_product(m.i.xyz(),m.j.xyz()))>1e-3f || std::abs(math::dot_product(m.i.xyz(),m.k.xyz()))>1e-3f || std::abs(math::dot_product(m.j.xyz(),m.k.xyz()))>1e-3f)throw std::runtime_error("Control frame must be orthonormal");
        object.anchor=m;object.anchor.c.xyz()=p.c.xyz();
    }
    context.m_object=&object;
    auto execute=[&](auto& control,float amount){
        control.set_matrix(object.anchor);
        control.one_step_execute(static_cast<enum_transform_axis>(v.axis),amount);
    };
    if(v.mode==1){transform_control_translation c(&context);execute(c,v.amount/.1f);}
    else if(v.mode==2){transform_control_rotation c(&context);execute(c,-v.amount/(math::pi/18));}
    else {if(v.amount<=0)throw std::runtime_error("Invalid scale factor");transform_control_scaling c(&context);execute(c,(v.amount-1)/.1f);}
    math::float4x4 inverse;
    if(!inverse.try_invert(object.anchor))throw std::runtime_error("Transform anchor is numerically singular");
    auto result=xf*inverse*object.result;
    for(auto const& row:result.elements)for(float f:row)if(!std::isfinite(f))throw std::runtime_error("Native transform overflow");
    auto scale=result.get_scale();
    if(!std::isfinite(scale.x) || !std::isfinite(scale.y) || !std::isfinite(scale.z) || scale.x<1e-5f || scale.y<1e-5f || scale.z<1e-5f)
        throw std::runtime_error("Native transform scale is outside the editable range");
    scale.x=std::copysign(scale.x,v.scale[0]);scale.y=std::copysign(scale.y,v.scale[1]);scale.z=std::copysign(scale.z,v.scale[2]);
    auto orientation=result;orientation.i.xyz()/=scale.x;orientation.j.xyz()/=scale.y;orientation.k.xyz()/=scale.z;
    if(std::abs(math::dot_product(orientation.i.xyz(),orientation.j.xyz()))>1e-3f
        || std::abs(math::dot_product(orientation.i.xyz(),orientation.k.xyz()))>1e-3f
        || std::abs(math::dot_product(orientation.j.xyz(),orientation.k.xyz()))>1e-3f)
        throw std::runtime_error("This world-axis scale introduces shear; use Local space or uniform scale");
    auto rotation=orientation.get_angles_xyz();
    auto write=[](float* f,float3 const& x){f[0]=x.x;f[1]=x.y;f[2]=x.z;};
    write(v.position,result.c.xyz());write(v.rotation,rotation);write(v.scale,scale);
}
void sdk_transform(memory::mimalloc_allocator_type& allocator,render::scene_ptr const& scene,render::scene_view_ptr const& view,render::render_output_window_ptr const& output,editor::hosting::sdk_scene_transform& v){transform_in_frame(allocator,scene,view,output,v,nullptr);}
void sdk_transform_in_frame(memory::mimalloc_allocator_type& allocator,render::scene_ptr const& scene,render::scene_view_ptr const& view,render::render_output_window_ptr const& output,editor::hosting::sdk_scene_transform& v,math::float4x4 const& frame){transform_in_frame(allocator,scene,view,output,v,&frame);}
}
