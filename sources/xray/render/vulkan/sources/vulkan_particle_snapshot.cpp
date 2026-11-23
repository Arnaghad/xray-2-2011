#include "pch.h"
#include <xray/render/vulkan/particle_resources.h>
#include <xray/render/vulkan/material_resources.h>
#include "texture_format.h"
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {
void finite(float value,float limit,char const* message) {
    if (!std::isfinite(value) || std::abs(value)>limit) throw std::runtime_error(message);
}
void rigid(math::float4x4 const& matrix) {
    for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) finite(matrix.elements[r][c],1e6f,"Particle matrix must be finite within +/-1e6");
    if (matrix.i.w!=0 || matrix.j.w!=0 || matrix.k.w!=0 || matrix.c.w!=1) throw std::runtime_error("Particle matrix must be affine");
    auto i=matrix.i.xyz(),j=matrix.j.xyz(),k=matrix.k.xyz();
    if (std::abs(i.squared_length()-1)>.0001f || std::abs(j.squared_length()-1)>.0001f || std::abs(k.squared_length()-1)>.0001f
        || std::abs(math::dot_product(i,j))>.0001f || std::abs(math::dot_product(i,k))>.0001f || std::abs(math::dot_product(j,k))>.0001f
        || math::dot_product(math::cross_product(i,j),k)<.9999f) throw std::runtime_error("Particle matrix requires a proper rigid basis; scale/shear/reflection unsupported");
}
void color(math::float4 const& value) {
    for (unsigned c=0;c<4;++c) if (!std::isfinite(value[c]) || value[c]<0 || value[c]>1) throw std::runtime_error("Particle linear RGBA requires [0,1]");
}
size_t texture_bytes(texture_data const& value) {
    auto const& d=value.description;
    if (d.type!=rhi::texture_type::type_2d || d.array_layers!=1 || d.samples!=1 || d.usage!=rhi::texture_usage::sampled
        || !d.width || !d.height || d.width>4096 || d.height>4096
        || (d.pixel_format!=rhi::format::r8g8b8a8_unorm && d.pixel_format!=rhi::format::r8g8b8a8_srgb))
        throw std::runtime_error("Particle billboard texture requires sampled RGBA8 2D up to 4096");
    u32 levels=1;for (u32 size=std::max(d.width,d.height);size>1;size>>=1) ++levels;
    if (!d.mip_levels || d.mip_levels>levels || value.mips.size()!=d.mip_levels) throw std::runtime_error("Particle texture mip count mismatch");
    size_t bytes=0;for (u32 mip=0;mip<d.mip_levels;++mip) {
        auto size=sampled_mip_layout(d.pixel_format,std::max(1u,d.width>>mip),std::max(1u,d.height>>mip)).size;
        if (value.mips[mip].size()!=size || size>particle_texture_bytes-bytes) throw std::runtime_error("Particle texture bytes/layout exceed 64 MiB");
        bytes+=size;
    }
    return bytes;
}
}
void validate_particle_texture(texture_data const& value) { texture_bytes(value); }
void validate_particle_transform(math::float4x4 const& value) { rigid(value); }
std::shared_ptr<particle_billboard_batch const> snapshot_billboards(std::span<particle::base_particle const> particles,
    particle_billboard_options const& options,math::float4x4 const& view,std::shared_ptr<texture_data const> texture) {
    if (particles.size()>particle_batch_limit) throw std::runtime_error("Particle billboard batch exceeds 4096 simulation particles");
    if (options.locked_axis!=particle::particle_locked_axis_none || options.subuv
        || (options.alignment!=particle::particle_screen_alignment_square && options.alignment!=particle::particle_screen_alignment_rectangle)
        || (options.blend!=particle_blend::alpha && options.blend!=particle_blend::additive))
        throw std::runtime_error("Particle profile supports camera-facing square/rectangle, no locked axis/subUV, alpha/additive");
    rigid(view);rigid(options.transform);color(options.color);
    if (!texture) throw std::runtime_error("Particle billboard texture is required");texture_bytes(*texture);
    for (auto const& p : particles) {
        color(p.color);finite(p.rotation,1e6f,"Particle rotation must be finite radians");
        finite(p.lifetime,1e6f,"Particle lifetime must be finite");finite(p.duration,1e6f,"Particle duration must be finite");
        if (p.lifetime<0 || p.duration<0) throw std::runtime_error("Particle lifetime/duration cannot be negative");
        for (unsigned c=0;c<3;++c) {finite(p.position[c],1e6f,"Particle position exceeds finite bounds");finite(p.size[c],1e4f,"Particle size exceeds finite bounds");
            if (p.size[c]<0) throw std::runtime_error("Particle sizes cannot be negative");}
    }
    struct item {particle::base_particle const* particle;math::float3 center;float depth;};
    std::vector<item> order;order.reserve(particles.size());
    for (auto const& p : particles) {
        if (p.is_dead() || !p.size.x || (options.alignment==particle::particle_screen_alignment_rectangle && !p.size.y) || !p.color.w || !options.color.w) continue;
        auto center=options.world_space ? p.position : options.transform.transform_position(p.position);
        auto depth=view.transform_position(center).z;order.push_back({&p,center,depth});
    }
    std::stable_sort(order.begin(),order.end(),[](auto const& a,auto const& b) {return a.depth>b.depth;});
    auto inverse=math::invert4x3(view);auto right=inverse.i.xyz(),up=inverse.j.xyz();
    auto result=std::make_shared<particle_billboard_batch>();result->texture=std::move(texture);result->blend=options.blend;
    result->vertices.reserve(order.size()*4);result->indices.reserve(order.size()*6);
    for (auto const& item : order) {
        auto const& p=*item.particle;float width=p.size.x,height=options.alignment==particle::particle_screen_alignment_square ? width : p.size.y;
        float cosine=std::cos(p.rotation),sine=std::sin(p.rotation);u16 base=u16(result->vertices.size());
        for (auto uv : {math::float2(0,0),math::float2(0,1),math::float2(1,1),math::float2(1,0)}) {
            float x=(uv.x-.5f)*width,y=(.5f-uv.y)*height;
            auto position=item.center+right*(cosine*x-sine*y)+up*(sine*x+cosine*y);
            for (unsigned c=0;c<3;++c) finite(position[c],2e6f,"Expanded particle position exceeds finite bounds");
            result->vertices.push_back({{position.x,position.y,position.z},{p.color.x*options.color.x,p.color.y*options.color.y,p.color.z*options.color.z,p.color.w*options.color.w},{uv.x,uv.y}});
        }
        for (unsigned offset : {0u,1u,2u,0u,2u,3u}) result->indices.push_back(u16(base+offset));
    }
    return result;
}
std::shared_ptr<particle_billboard_batch const> snapshot_billboards(particle::particle_list_type const& particles,
    particle_billboard_options const& options,math::float4x4 const& view,std::shared_ptr<texture_data const> texture) {
    std::vector<particle::base_particle> copy;
    for (auto* p=particles.front();p;p=particles.get_next_of_object(p)) {
        if (copy.size()==particle_batch_limit) throw std::runtime_error("Particle list exceeds 4096 entries or contains a cycle");
        copy.push_back(*p);
    }
    return snapshot_billboards(std::span<particle::base_particle const>(copy),options,view,std::move(texture));
}
void validate_particle_batches(std::span<std::shared_ptr<particle_billboard_batch const> const> batches) {
    if (batches.size()>particle_frame_batches) throw std::runtime_error("Particle frame exceeds 64 billboard batches");
    size_t bytes=0,textures=0;std::vector<texture_data const*> unique;
    for (auto const& batch : batches) {
        if (!batch || !batch->texture || batch->vertices.size()%4 || batch->vertices.size()>particle_batch_limit*4
            || batch->indices.size()!=batch->vertices.size()/4*6 || (batch->blend!=particle_blend::alpha && batch->blend!=particle_blend::additive))
            throw std::runtime_error("Invalid particle billboard batch");
        if (batch->bytes()>particle_frame_bytes-bytes) throw std::runtime_error("Particle frame exceeds 8 MiB copied geometry");bytes+=batch->bytes();
        for (auto const& v : batch->vertices) {
            for (float p : v.position) finite(p,2e6f,"Particle vertex position invalid");
            for (float c : v.color) if (!std::isfinite(c) || c<0 || c>1) throw std::runtime_error("Particle vertex RGBA invalid");
            for (float uv : v.uv) if (!std::isfinite(uv) || uv<0 || uv>1) throw std::runtime_error("Particle vertex UV invalid");
        }
        for (u16 index : batch->indices) if (index>=batch->vertices.size()) throw std::runtime_error("Particle index outside vertex buffer");
        auto count=texture_bytes(*batch->texture);
        if (std::find(unique.begin(),unique.end(),batch->texture.get())==unique.end()) {
            if (count>particle_texture_bytes-textures) throw std::runtime_error("Particle frame exceeds 64 MiB unique textures");
            textures+=count;unique.push_back(batch->texture.get());
        }
    }
}
} } }
