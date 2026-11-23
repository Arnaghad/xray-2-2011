#include "pch.h"
#include "vulkan_scene_shadows.h"
#include "vulkan_frustum.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <cmath>
#include <algorithm>
#include <limits>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {
using point=std::array<double,3>;
point transform(math::float4x4 const& matrix,point const& p) {
    std::array<double,4> q={};
    for (unsigned c=0;c<4;++c) {
        q[c]=matrix.elements[3][c];
        for (unsigned r=0;r<3;++r) q[c]+=p[r]*matrix.elements[r][c];
    }
    if (!std::isfinite(q[3]) || std::abs(q[3])<1e-12) throw std::runtime_error("Invalid directional shadow homogeneous point");
    point result;
    for (unsigned c=0;c<3;++c) {
        result[c]=q[c]/q[3];
        if (!std::isfinite(result[c]) || std::abs(result[c])>1e7) throw std::runtime_error("Directional shadow position exceeds +/-1e7");
    }
    return result;
}
point cross(point a,point b) { return {a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]}; }
point normalized(point p) {
    double length=std::sqrt(p[0]*p[0]+p[1]*p[1]+p[2]*p[2]);
    if (!std::isfinite(length) || length<1e-10) throw std::runtime_error("Invalid directional shadow basis");
    for (auto& c : p) c/=length;
    return p;
}
math::float4x4 shadow_inverse_view(math::float4x4 const& view) {
    validate_gbuffer_transform(view);
    for (unsigned c=0;c<3;++c) if (std::abs(view.elements[3][c])>1e7)
        throw std::runtime_error("Shadow camera translation exceeds +/-1e7");
    for (unsigned a=0;a<3;++a) for (unsigned b=0;b<3;++b) {
        double dot=0; for (unsigned c=0;c<3;++c) dot+=double(view.elements[a][c])*view.elements[b][c];
        if (std::abs(dot-(a==b ? 1.0 : 0.0))>1e-4) throw std::runtime_error("Scene shadows require a rigid camera view");
    }
    math::float4x4 inverse;
    if (!math::try_invert4x4(view,inverse)) throw std::runtime_error("Cannot invert shadow camera");
    return inverse;
}
}
scene_shadow_plan plan_directional_shadows(scene_light const& light,math::float4x4 const& view,
    math::float4x4 const& projection,std::vector<model_draw> const& casters) {
    if (!light.casts_shadows || light.type!=scene_light_type::directional || !light.shadow_cascades || light.shadow_cascades>4
        || light.shadow_resolution<64 || light.shadow_resolution>2048 || (light.shadow_resolution & (light.shadow_resolution-1))
        || !std::isfinite(light.shadow_transparency) || light.shadow_transparency<0 || light.shadow_transparency>1)
        throw std::runtime_error("Invalid directional shadow profile");
    auto inverse_view=shadow_inverse_view(view);
    auto camera=gbuffer_camera(projection);
    if (projection.elements[0][2]!=0 || projection.elements[1][2]!=0 || projection.elements[0][3]!=0 || projection.elements[1][3]!=0)
        throw std::runtime_error("Oblique depth/clip-W projections are not supported by directional shadows");
    math::float4x4 inverse_projection;
    if (!math::try_invert4x4(projection,inverse_projection))
        throw std::runtime_error("Cannot invert directional shadow camera");
    point direction=normalized({light.direction[0],light.direction[1],light.direction[2]});
    point right=normalized(cross(std::abs(direction[1])>.95 ? point{1,0,0} : point{0,1,0},direction));
    point up=cross(direction,right);
    math::float4x4 light_view; light_view.identity();
    for (unsigned r=0;r<3;++r) { light_view.elements[r][0]=float(right[r]); light_view.elements[r][1]=float(up[r]); light_view.elements[r][2]=float(direction[r]); }
    scene_shadow_plan plan; plan.count=light.shadow_cascades; plan.resolution=light.shadow_resolution; plan.transparency=light.shadow_transparency;
    float near_plane=camera.near_plane,far_plane=camera.far_plane;
    bool perspective=std::abs(projection.elements[2][3])>1e-8;
    for (unsigned i=0;i<plan.count;++i) {
        double fraction=double(i+1)/plan.count,uniform=near_plane+(far_plane-near_plane)*fraction;
        double logarithmic=near_plane>0 ? near_plane*std::pow(far_plane/near_plane,fraction) : uniform;
        plan.splits[i]=i+1==plan.count ? far_plane : float(perspective ? .5*(uniform+logarithmic) : uniform);
    }
    for (unsigned i=plan.count;i<4;++i) plan.splits[i]=far_plane;
    for (unsigned i=0;i<plan.count;++i) {
        float previous=i ? plan.splits[i-1] : near_plane;
        float start=i ? previous-.1f*(previous-(i>1 ? plan.splits[i-2] : near_plane)) : near_plane;
        std::array<point,8> corners; unsigned index=0;
        for (double x : {-1.,1.}) for (double y : {-1.,1.}) {
            auto a=transform(inverse_projection,{x,y,0}),b=transform(inverse_projection,{x,y,1});
            if (b[2]-a[2]<1e-10) throw std::runtime_error("Unsupported oblique directional shadow projection");
            for (double z : {double(start),double(plan.splits[i])}) {
                point p; for (unsigned c=0;c<3;++c) p[c]=a[c]+(b[c]-a[c])*(z-a[2])/(b[2]-a[2]);
                corners[index++]=transform(inverse_view,p);
            }
        }
        point center={}; for (auto const& p : corners) for (unsigned c=0;c<3;++c) center[c]+=p[c]/8;
        double radius=.01;
        for (auto const& p : corners) {
            double squared=0; for (unsigned c=0;c<3;++c) squared+=(p[c]-center[c])*(p[c]-center[c]);
            radius=std::max(radius,std::sqrt(squared));
        }
        radius=std::ceil(radius*16)/16;
        radius*=double(plan.resolution)/(plan.resolution-2);
        auto light_center=transform(light_view,center);
        double texel=2*radius/plan.resolution;
        double cx=std::round(light_center[0]/texel)*texel,cy=std::round(light_center[1]/texel)*texel;
        double minimum=std::numeric_limits<double>::infinity(),maximum=-minimum;
        for (auto const& p : corners) { auto q=transform(light_view,p); minimum=std::min(minimum,q[2]); maximum=std::max(maximum,q[2]); }
        // Extend light depth for off-camera casters overlapping the receiver's XY footprint.
        for (auto const& caster : casters) {
            point low={1e30,1e30,1e30},high={-1e30,-1e30,-1e30};
            for (unsigned mask=0;mask<8;++mask) {
                point p; for (unsigned c=0;c<3;++c) p[c]=(mask & (1u<<c)) ? caster.mesh->maximum[c] : caster.mesh->minimum[c];
                auto q=transform(light_view,transform(caster.transform,p));
                for (unsigned c=0;c<3;++c) { low[c]=std::min(low[c],q[c]); high[c]=std::max(high[c],q[c]); }
            }
            if (high[0]<cx-radius || low[0]>cx+radius || high[1]<cy-radius || low[1]>cy+radius) continue;
            minimum=std::min(minimum,low[2]); maximum=std::max(maximum,high[2]);
        }
        double margin=std::max(1.,(maximum-minimum)*.01); minimum-=margin; maximum+=margin;
        auto& cascade=plan.cascades[i]; cascade.view=light_view; cascade.projection.identity();
        cascade.projection.elements[0][0]=cascade.projection.elements[1][1]=float(1/radius);
        cascade.projection.elements[2][2]=float(1/(maximum-minimum));
        cascade.projection.elements[3][0]=float(-cx/radius); cascade.projection.elements[3][1]=float(-cy/radius);
        cascade.projection.elements[3][2]=float(-minimum/(maximum-minimum));
        auto receiver=inverse_view*light_view*cascade.projection;
        for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) cascade.receiver[r*4+c]=receiver.elements[c][r];
    }
    return plan;
}
scene_shadow_plan plan_spot_shadows(scene_light const& light,math::float4x4 const& view) {
    if (!light.casts_shadows || light.type!=scene_light_type::spot || light.shadow_cascades!=1
        || light.shadow_resolution<256 || light.shadow_resolution>1024 || (light.shadow_resolution & (light.shadow_resolution-1))
        || !std::isfinite(light.range) || light.range<.001f || light.range>65536
        || !std::isfinite(light.spot_outer_angle) || light.spot_outer_angle<.001f || light.spot_outer_angle>=math::pi
        || !std::isfinite(light.shadow_transparency) || light.shadow_transparency<0 || light.shadow_transparency>1
        || !std::isfinite(light.shadow_depth_bias) || light.shadow_depth_bias<0 || light.shadow_depth_bias>.1f)
        throw std::runtime_error("Invalid spot shadow profile");
    auto inverse_view=shadow_inverse_view(view);
    point direction=normalized({light.direction[0],light.direction[1],light.direction[2]});
    point right=normalized(cross(std::abs(direction[1])>.95 ? point{1,0,0} : point{0,1,0},direction));
    point up=cross(direction,right);
    scene_shadow_plan plan; plan.count=1; plan.resolution=light.shadow_resolution; plan.transparency=light.shadow_transparency;
    auto& map=plan.cascades[0];
    for (unsigned c=0;c<3;++c) {
        if (!std::isfinite(light.position[c]) || std::abs(light.position[c])>1e6) throw std::runtime_error("Invalid spot shadow position");
        map.view.elements[c][0]=float(right[c]); map.view.elements[c][1]=float(up[c]); map.view.elements[c][2]=float(direction[c]);
    }
    for (unsigned c=0;c<3;++c) {
        double translation=0; for (unsigned r=0;r<3;++r) translation-=double(light.position[r])*map.view.elements[r][c];
        map.view.elements[3][c]=float(translation);
    }
    map.projection=math::create_perspective_projection(light.spot_outer_angle,1,light.range/1000,light.range);
    auto receiver=math::mul4x4(inverse_view*map.view,map.projection);
    for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) map.receiver[r*4+c]=receiver.elements[c][r];
    return plan;
}
scene_shadow_plan plan_point_shadows(scene_light const& light,math::float4x4 const& view) {
    if (!light.casts_shadows || light.type!=scene_light_type::point || light.shadow_cascades!=6 || light.shadow_face_mask>63
        || light.shadow_resolution<256 || light.shadow_resolution>1024 || (light.shadow_resolution & (light.shadow_resolution-1))
        || !std::isfinite(light.range) || light.range<=.025f+math::epsilon_5 || light.range>65536
        || !std::isfinite(light.shadow_transparency) || light.shadow_transparency<0 || light.shadow_transparency>1
        || !std::isfinite(light.shadow_depth_bias) || light.shadow_depth_bias<0 || light.shadow_depth_bias>.1f)
        throw std::runtime_error("Invalid point shadow profile");
    auto inverse_view=shadow_inverse_view(view);
    math::float4x4 relative; relative.identity();
    for (unsigned c=0;c<3;++c) {
        if (!std::isfinite(light.position[c]) || std::abs(light.position[c])>1e6) throw std::runtime_error("Invalid point shadow position");
        relative.elements[3][c]=-light.position[c];
    }
    scene_shadow_plan plan; plan.count=6; plan.resolution=light.shadow_resolution;
    plan.transparency=light.shadow_transparency; plan.face_mask=light.shadow_face_mask;
    auto receiver=inverse_view*relative;
    for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) plan.point_receiver[r*4+c]=receiver.elements[c][r];
    // Match the reference world's +X/-X/+Y/-Y/+Z/-Z views, including their roll.
    std::array<point,6> directions={point{1,0,0},point{-1,0,0},point{0,1,0},point{0,-1,0},point{0,0,1},point{0,0,-1}};
    std::array<point,6> ups={point{0,1,0},point{0,1,0},point{0,0,-1},point{0,0,1},point{0,1,0},point{0,1,0}};
    for (unsigned i=0;i<6;++i) {
        auto& map=plan.cascades[i]; auto right=cross(ups[i],directions[i]);
        for (unsigned r=0;r<3;++r) {
            map.view.elements[r][0]=float(right[r]); map.view.elements[r][1]=float(ups[i][r]); map.view.elements[r][2]=float(directions[i][r]);
        }
        map.view=relative*map.view;
        map.projection=math::create_perspective_projection(math::pi/2,1,.025f,light.range);
        auto packed=math::mul4x4(inverse_view*map.view,map.projection);
        for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) map.receiver[r*4+c]=packed.elements[c][r];
    }
    // Point receivers reconstruct perspective depth from the dominant world axis.
    plan.splits={plan.cascades[0].projection.elements[2][2],plan.cascades[0].projection.elements[3][2],float(plan.face_mask),0};
    return plan;
}
scene_shadows::scene_shadows(rhi::rhi_backend& backend) : m_backend(&backend) {
    rhi::sampler_desc sampler; sampler.comparison=true; sampler.compare=rhi::comparison_func::less_equal;
    sampler.mip_filter=rhi::sampler_filter::nearest; sampler.address_u=sampler.address_v=rhi::sampler_address::clamp;
    m_sampler=backend.create_sampler(sampler); if (!m_sampler) throw std::runtime_error("Shadow comparison sampler unavailable");
}
scene_shadows::~scene_shadows() {
    if (rhi::rhi_device::ref().get_backend()==m_backend) {
        for(auto const& entry:m_atlases)m_backend->destroy_texture(entry.texture);
        m_backend->destroy_sampler(m_sampler);
    }
}
shadow_binding scene_shadows::render(scene_shadow_plan const& plan,std::vector<model_draw> const& casters,static_renderer* models) {
    auto& b=*m_backend; u32 grid=shadow_atlas_grid(plan.count),extent=grid*plan.resolution;
    auto slot=std::find_if(m_atlases.begin(),m_atlases.end(),[&](auto const& entry){return entry.texture && entry.extent==extent;});
    if(slot==m_atlases.end()) {
        slot=std::find_if(m_atlases.begin(),m_atlases.end(),[](auto const& entry){return !entry.texture;});
        if(slot==m_atlases.end())slot=std::min_element(m_atlases.begin(),m_atlases.end(),[](auto const& a,auto const& b){return a.last_use<b.last_use;});
        rhi::texture_desc desc; desc.width=desc.height=extent; desc.pixel_format=rhi::format::d32_float; desc.usage=rhi::texture_usage::depth_stencil;
        auto next=b.create_texture(desc,{}); if (!next) throw std::runtime_error("Scene shadow atlas unavailable");
        // Publish only a successful replacement; eviction uses normal fence retirement.
        b.destroy_texture(slot->texture);slot->texture=next;slot->extent=extent;
    }
    slot->last_use=++m_use_serial;auto atlas=slot->texture;
    b.transition_texture(atlas,rhi::resource_state::depth_write);
    rhi::render_pass_desc pass; pass.depth.texture=atlas; b.begin_render_pass(pass);
    b.set_depth_bias(1.25f,1.75f);
    for (unsigned i=0;i<plan.count;++i) {
        if (!(plan.face_mask & (1u<<i))) continue;
        auto const& cascade=plan.cascades[i];
        u32 x=(i%grid)*plan.resolution,y=(i/grid)*plan.resolution;
        b.set_viewport(float(x),float(y),float(plan.resolution),float(plan.resolution)); b.set_scissor(x,y,plan.resolution,plan.resolution);
        frame_constants camera;
        for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) {
            camera.view[r*4+c]=cascade.view.elements[c][r]; camera.projection[r*4+c]=cascade.projection.elements[c][r];
        }
        std::vector<model_draw> visible;
        for (auto const& item : casters) {
            local_frustum frustum(item.transform,cascade.view,cascade.projection);
            if (frustum.intersects(item.mesh->minimum,item.mesh->maximum)) visible.push_back(item);
        }
        if (!visible.empty()) models->draw(visible,camera,0,false,true);
    }
    b.set_depth_bias(0,0); b.end_render_pass(); b.transition_texture(atlas,rhi::resource_state::depth_read);
    return {atlas,m_sampler,&plan};
}
} } }
