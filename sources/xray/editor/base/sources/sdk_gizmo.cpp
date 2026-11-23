#include "pch.h"
#include <xray/editor/base/sdk_gizmo.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <limits>
#include <stdexcept>

namespace xray::editor_base { namespace {
using namespace editor::hosting;
using math::float3;using math::float4x4;
float3 vec(float const* p){return {p[0],p[1],p[2]};}
float4x4 matrix(float const* p){float4x4 m;std::memcpy(&m,p,sizeof(m));return m;}
bool finite(float3 const& p){return std::isfinite(p.x)&&std::isfinite(p.y)&&std::isfinite(p.z);}
bool allowed(unsigned mode,unsigned axis,unsigned version) {
    if(!axis)return true;if(axis<=3)return mode!=0;if(axis==4)return mode==3;
    return version==2 && (((axis>=5 && axis<=8) && (mode==1 || mode==3)) || ((axis==9 || axis==10) && mode==2));
}
void affine(float4x4 const& m) {
    for(auto const& row:m.elements)for(float f:row)if(!std::isfinite(f))throw std::runtime_error("Nonfinite gizmo pose");
    if(m.e03 || m.e13 || m.e23 || m.e33!=1)throw std::runtime_error("Gizmo pose must be affine");
    std::array<float3,3> basis={m.i.xyz(),m.j.xyz(),m.k.xyz()};
    for(auto const& a:basis)if(std::abs(a.length()-1)>1e-3f)throw std::runtime_error("Gizmo basis must be normalized");
    for(unsigned i=0;i<3;++i)for(unsigned j=i+1;j<3;++j)if(std::abs(math::dot_product(basis[i],basis[j]))>1e-3f)throw std::runtime_error("Gizmo basis must be orthogonal");
}
struct context {
    float4x4 anchor,camera,projection;
    float scale=0;
    explicit context(sdk_gizmo_settings const& s) {
        if((s.version!=1 && s.version!=2) || s.size!=sizeof(s) || s.mode>3 || !allowed(s.mode,s.active_axis,s.version) || s.reserved
            || (!s.mode && (s.object || s.active_axis)) || (s.mode && !s.object))throw std::runtime_error("Invalid gizmo settings");
        if(!s.mode)return;
        if(!s.width || !s.height || s.width>16384 || s.height>16384 || !std::isfinite(s.pixels) || s.pixels<8 || s.pixels>512)
            throw std::runtime_error("Invalid gizmo viewport/size");
        anchor=matrix(s.anchor);camera=matrix(s.camera);projection=matrix(s.projection);affine(anchor);affine(camera);
        for(auto const& row:projection.elements)for(float f:row)if(!std::isfinite(f))throw std::runtime_error("Invalid gizmo projection");
        if(projection.e00<=0 || projection.e11<=0 || projection.e23!=1 || projection.e33!=0 || projection.e22<1 || projection.e32>=0)
            throw std::runtime_error("Gizmo requires a perspective projection");
        auto pivot=math::invert4x3(camera).transform_position(anchor.c.xyz());
        float near_plane=-projection.e32/projection.e22;
        if(pivot.z<=near_plane || (pivot.z*projection.e22+projection.e32)/pivot.z>1)return;
        scale=2*pivot.z*s.pixels/(s.height*projection.e11);
        if(!std::isfinite(scale) || scale<=0)throw std::runtime_error("Invalid gizmo scale");
    }
    float3 axis(unsigned i) const {return i==1?anchor.i.xyz():i==2?anchor.j.xyz():anchor.k.xyz();}
    float3 point(float3 const& p) const {return anchor.transform_direction(p*scale)+anchor.c.xyz();}
    bool visible(unsigned i) const {return std::abs(math::dot_product(axis(i),camera.k.xyz()))<.98f;}
};
void ray(sdk_scene_ray const& r) {
    auto o=vec(r.origin),d=vec(r.direction);
    if(!finite(o) || !finite(d) || std::abs(d.length()-1)>1e-3f || r.snap || r.reserved || r.exclude)
        throw std::runtime_error("Invalid gizmo ray");
}
bool intersection(float3 const& o,float3 const& d,sdk_gizmo_triangle const& t,float& value) {
    auto e=t.b-t.a,f=t.c-t.a,h=math::cross_product(d,f);float det=math::dot_product(e,h);
    if(std::abs(det)<1e-9f)return false;
    auto s=o-t.a;float u=math::dot_product(s,h)/det;if(u<0 || u>1)return false;
    auto q=math::cross_product(s,e);float v=math::dot_product(d,q)/det;if(v<0 || u+v>1)return false;
    value=math::dot_product(f,q)/det;return value>=0 && std::isfinite(value);
}
float3 local_axis(unsigned i,float along,float u,float v) {return i==1?float3(along,u,v):i==2?float3(v,along,u):float3(u,v,along);}
bool line_parameter(context const& c,unsigned axis,sdk_scene_ray const& ray,float& u) {
    auto a=c.axis(axis),d=vec(ray.direction),w=vec(ray.origin)-c.anchor.c.xyz();float b=math::dot_product(a,d),den=1-b*b;
    if(den<1e-4f)return false;
    u=(math::dot_product(a,w)-b*math::dot_product(d,w))/den;return std::isfinite(u);
}
bool plane_point(context const& c,float3 const& normal,sdk_scene_ray const& ray,float3& point) {
    auto o=vec(ray.origin),d=vec(ray.direction);float den=math::dot_product(d,normal);if(std::abs(den)<1e-5f)return false;
    float t=math::dot_product(c.anchor.c.xyz()-o,normal)/den;if(t<0 || !std::isfinite(t))return false;
    point=o+d*t;return finite(point);
}
}
void sdk_gizmo_build(sdk_gizmo_settings const& s,std::vector<sdk_gizmo_triangle>& out) {
    context c(s);std::vector<sdk_gizmo_triangle> triangles;
    if(!s.mode || !c.scale){out.clear();return;}
    auto tri=[&](float3 a,float3 b,float3 d,unsigned axis){triangles.push_back({c.point(a),c.point(b),c.point(d),axis});};
    auto tube=[&](unsigned axis,float from,float to,float radius,float end_radius) {
        constexpr unsigned sides=12;
        for(unsigned i=0;i<sides;++i) {
            float a=2*math::pi*i/sides,b=2*math::pi*(i+1)/sides;
            auto p=local_axis(axis,from,radius*std::cos(a),radius*std::sin(a)),q=local_axis(axis,from,radius*std::cos(b),radius*std::sin(b));
            auto r=local_axis(axis,to,end_radius*std::cos(a),end_radius*std::sin(a)),t=local_axis(axis,to,end_radius*std::cos(b),end_radius*std::sin(b));
            tri(p,q,r,axis);tri(q,t,r,axis);tri(local_axis(axis,from,0,0),q,p,axis);
            if(end_radius>0)tri(local_axis(axis,to,0,0),r,t,axis);
        }
    };
    auto box=[&](float3 center,float radius,unsigned axis) {
        std::array<float3,8> p;for(unsigned i=0;i<8;++i)p[i]=center+float3(i&1?radius:-radius,i&2?radius:-radius,i&4?radius:-radius);
        unsigned const faces[][4]={{0,1,3,2},{4,6,7,5},{0,4,5,1},{2,3,7,6},{0,2,6,4},{1,5,7,3}};
        for(auto const& f:faces){tri(p[f[0]],p[f[1]],p[f[2]],axis);tri(p[f[0]],p[f[2]],p[f[3]],axis);}
    };
    for(unsigned axis=1;axis<=3;++axis) {
        if(s.mode!=2 && !c.visible(axis))continue;
        if(s.mode==2) {
            // A solid torus replaces the legacy global clear-Z/color-write
            // sequence. The overlay never changes the scene's depth attachment.
            constexpr unsigned segments=96,sides=8;
            auto ring=[&](unsigned segment,unsigned side) {
                float a=2*math::pi*segment/segments,b=2*math::pi*side/sides;
                return local_axis(axis,.028f*std::sin(b),(.85f+.028f*std::cos(b))*std::cos(a),(.85f+.028f*std::cos(b))*std::sin(a));
            };
            for(unsigned i=0;i<segments;++i)for(unsigned j=0;j<sides;++j){auto a=ring(i,j),b=ring(i+1,j),d=ring(i,j+1),e=ring(i+1,j+1);tri(a,b,d,axis);tri(b,e,d,axis);}
        } else {
            tube(axis,.16f,.82f,.022f,.022f);
            if(s.mode==1)tube(axis,.8f,1,.085f,0);
            else box(local_axis(axis,.95f,0,0),.065f,axis);
        }
    }
    if(s.mode==3)box({0,0,0},.085f,4);
    if(s.version==2) {
        auto world_tri=[&](float3 a,float3 b,float3 d,unsigned axis){auto p=c.anchor.c.xyz();triangles.push_back({p+a*c.scale,p+b*c.scale,p+d*c.scale,axis});};
        auto quad=[&](float3 u,float3 v,float lo,float hi,unsigned axis){auto a=u*lo+v*lo,b=u*hi+v*lo,d=u*lo+v*hi,e=u*hi+v*hi;world_tri(a,b,d,axis);world_tri(b,e,d,axis);};
        if(s.mode!=2) {
            for(unsigned i=0;i<3;++i){unsigned a=i+1,b=(i+1)%3+1;auto normal=math::cross_product(c.axis(a),c.axis(b));if(std::abs(math::dot_product(normal,c.camera.k.xyz()))>.12f)quad(c.axis(a),c.axis(b),.25f,.44f,5+i);}
            quad(c.camera.i.xyz(),c.camera.j.xyz(),s.mode==3?-.15f:-.11f,s.mode==3?.15f:.11f,8);
        } else {
            auto right=c.camera.i.xyz(),up=c.camera.j.xyz(),forward=c.camera.k.xyz();
            for(unsigned i=0;i<96;++i)for(unsigned j=0;j<8;++j){
                auto p=[&](unsigned a,unsigned b){float angle=2*math::pi*a/96,side=2*math::pi*b/8;return (right*std::cos(angle)+up*std::sin(angle))*(1.12f+.025f*std::cos(side))+forward*.025f*std::sin(side);};
                auto a=p(i,j),b=p(i+1,j),d=p(i,j+1),e=p(i+1,j+1);world_tri(a,b,d,9);world_tri(b,e,d,9);
            }
            for(unsigned i=0;i<64;++i){float a=2*math::pi*i/64,b=2*math::pi*(i+1)/64;world_tri({0,0,0},(right*std::cos(a)+up*std::sin(a))*.62f,(right*std::cos(b)+up*std::sin(b))*.62f,10);}
        }
    }
    auto depth=[&](sdk_gizmo_triangle const& t){return math::dot_product((t.a+t.b+t.c)/3-c.camera.c.xyz(),c.camera.k.xyz());};
    std::stable_sort(triangles.begin(),triangles.end(),[&](auto const& a,auto const& b){return depth(a)>depth(b);});
    out=std::move(triangles);
}
sdk_gizmo_hit sdk_gizmo_pick(sdk_gizmo_settings const& s,std::vector<sdk_gizmo_triangle> const& triangles,sdk_scene_ray const& r) {
    context c(s);ray(r);sdk_gizmo_hit result{};
    if(!s.mode || !c.scale)return result;
    float nearest=std::numeric_limits<float>::max();auto o=vec(r.origin),d=vec(r.direction);bool arcball=false;
    for(auto const& triangle:triangles){float t;if(!intersection(o,d,triangle,t))continue;
        if(s.version==2 && s.mode==2 && triangle.axis==10){nearest=t;result.axis=10;arcball=true;continue;}
        if(arcball || t>nearest+1e-5f)continue;
        if(std::abs(t-nearest)<1e-5f && triangle.axis<result.axis)continue;
        nearest=t;result.axis=triangle.axis;
    }
    if(result.axis){result.mode=s.mode;result.object=s.object;result.distance=nearest;result.scale=c.scale;auto p=o+d*nearest;result.point[0]=p.x;result.point[1]=p.y;result.point[2]=p.z;}
    return result;
}
sdk_gizmo_delta sdk_gizmo_drag(editor::hosting::sdk_gizmo_drag const& input) {
    if(input.version!=1 || input.size!=sizeof(input) || input.reserved || input.axis<1 || input.axis>4 || (input.axis==4 && input.origin.mode!=3))throw std::runtime_error("Invalid gizmo drag");
    context c(input.origin);ray(input.from);ray(input.to);sdk_gizmo_delta result{1,sizeof(result),0,input.axis};
    if(!input.origin.mode || !c.scale)return result;
    float a=0,b=0;
    if(input.origin.mode==2) {
        float3 from,to;auto axis=c.axis(input.axis);
        if(input.origin.version==2 && std::abs(math::dot_product(axis,vec(input.from.direction)))<.08f) {
            // An edge-on ring has no stable ray/plane intersection. Project the
            // front hemisphere onto its axis plane, using the frozen drag origin.
            auto sphere=[&](sdk_scene_ray const& r,float3& value) {
                auto w=vec(r.origin)-c.anchor.c.xyz(),d=vec(r.direction);float radius=c.scale*.85f,b=math::dot_product(w,d),disc=b*b-w.squared_length()+radius*radius;
                if(disc>=0 && -b-std::sqrt(disc)>=0)value=w+d*(-b-std::sqrt(disc));
                else {float3 point;if(!plane_point(c,c.camera.k.xyz(),r,point))return false;value=point-c.anchor.c.xyz();}
                value-=axis*math::dot_product(value,axis);return true;
            };
            if(!sphere(input.from,from) || !sphere(input.to,to))return result;
        } else {
            if(!plane_point(c,axis,input.from,from) || !plane_point(c,axis,input.to,to))return result;
            from-=c.anchor.c.xyz();to-=c.anchor.c.xyz();
        }
        if(from.length()<c.scale*.05f || to.length()<c.scale*.05f)return result;
        from.normalize();to.normalize();result.amount=-std::atan2(math::dot_product(axis,math::cross_product(from,to)),math::dot_product(from,to));
    } else if(input.axis==4) {
        float3 from,to;if(!plane_point(c,c.camera.k.xyz(),input.from,from) || !plane_point(c,c.camera.k.xyz(),input.to,to))return result;
        auto delta=to-from;result.amount=std::exp(std::clamp((math::dot_product(delta,c.camera.i.xyz())+math::dot_product(delta,c.camera.j.xyz()))/c.scale,-4.6f,4.6f));
    } else {
        if(!c.visible(input.axis) || !line_parameter(c,input.axis,input.from,a) || !line_parameter(c,input.axis,input.to,b))return result;
        result.amount=input.origin.mode==1?b-a:std::max(.01f,1+(b-a)/c.scale);
    }
    if(!std::isfinite(result.amount))throw std::runtime_error("Nonfinite gizmo delta");
    result.valid=1;return result;
}
sdk_gizmo_delta_extended sdk_gizmo_drag_extended(editor::hosting::sdk_gizmo_drag const& input) {
    if(input.version!=2 || input.size!=sizeof(input) || input.reserved || input.origin.version!=2 || input.axis<5 || !allowed(input.origin.mode,input.axis,2))throw std::runtime_error("Invalid extended gizmo drag");
    context c(input.origin);ray(input.from);ray(input.to);sdk_gizmo_delta_extended result{2,sizeof(result),0,input.axis};for(float& f:result.scale)f=1;
    if(!c.scale)return result;
    auto write=[](float* p,float3 const& v){for(unsigned i=0;i<3;++i)p[i]=v[i];};
    if(input.origin.mode==2) {
        float3 from,to,axis=c.camera.k.xyz();
        if(input.axis==10) {
            auto sphere=[&](sdk_scene_ray const& r,float3& v) {
                auto w=vec(r.origin)-c.anchor.c.xyz(),d=vec(r.direction);float radius=c.scale*.62f,b=math::dot_product(w,d),disc=b*b-w.squared_length()+radius*radius;
                if(disc>=0 && -b-std::sqrt(disc)>=0)v=w+d*(-b-std::sqrt(disc));
                else {float3 point;if(!plane_point(c,c.camera.k.xyz(),r,point))return false;v=point-c.anchor.c.xyz();}
                if(v.length()<1e-6f)return false;v.normalize();return true;
            };
            if(!sphere(input.from,from) || !sphere(input.to,to))return result;
            axis=math::cross_product(from,to);float length=axis.length();if(length<1e-6f){result.valid=1;return result;}axis/=length;
            result.amount=-std::atan2(length,math::dot_product(from,to));
        } else {
            if(!plane_point(c,axis,input.from,from) || !plane_point(c,axis,input.to,to))return result;
            from-=c.anchor.c.xyz();to-=c.anchor.c.xyz();if(from.length()<c.scale*.05f || to.length()<c.scale*.05f)return result;from.normalize();to.normalize();
            result.amount=-std::atan2(math::dot_product(axis,math::cross_product(from,to)),math::dot_product(from,to));
        }
        write(result.rotation_axis,axis);
    } else {
        unsigned first=input.axis==6?2:input.axis==7?3:1,second=first%3+1;
        auto u=input.axis==8?c.camera.i.xyz():c.axis(first),v=input.axis==8?c.camera.j.xyz():c.axis(second),normal=math::cross_product(u,v);float3 from,to;
        if(!plane_point(c,normal,input.from,from) || !plane_point(c,normal,input.to,to))return result;
        auto delta=to-from;
        if(input.origin.mode==1)write(result.translation,delta);
        else {result.scale[first-1]=std::max(.01f,1+math::dot_product(delta,u)/c.scale);result.scale[second-1]=std::max(.01f,1+math::dot_product(delta,v)/c.scale);}
    }
    for(auto values:{result.translation,result.rotation_axis,result.scale})for(unsigned i=0;i<3;++i)if(!std::isfinite(values[i]))throw std::runtime_error("Nonfinite advanced gizmo delta");
    if(!std::isfinite(result.amount))throw std::runtime_error("Nonfinite advanced gizmo angle");
    result.valid=1;return result;
}
}
