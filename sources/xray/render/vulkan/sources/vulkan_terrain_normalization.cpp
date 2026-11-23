#include "pch.h"
#include <xray/render/vulkan/terrain_resources.h>
#include <xray/render/vulkan/material_resources.h>
#include <algorithm>
#include <cmath>
#include <stdexcept>
#include <thread>

namespace xray { namespace render { namespace vulkan {
namespace {
using images=std::vector<std::shared_ptr<texture_data const>>;
std::array<images const*,3> roles(terrain_texture_set const& set) { return {&set.diffuse,&set.normal,&set.specular}; }
std::array<images*,3> roles(terrain_texture_set& set) { return {&set.diffuse,&set.normal,&set.specular}; }
bool same(terrain_texture_set const& a,terrain_texture_set const& b) {
    return a.memory_profile==b.memory_profile && a.diffuse==b.diffuse && a.normal==b.normal && a.specular==b.specular;
}
u32 levels(u32 size) { u32 count=1;while (size>1) {size>>=1;++count;}return count; }
size_t rgba_bytes(u32 size,u32 count) {
    size_t bytes=0;for (u32 i=0;i<count;++i) {bytes+=size_t(size)*size*4;size=std::max(1u,size/2);}return bytes;
}
bool layout(texture_data const& image,u32 dimension,u32 count,rhi::format format) {
    return image.description.width==dimension && image.description.mip_levels==count && image.description.pixel_format==format;
}
std::shared_ptr<texture_data const> promote(texture_data const& source,u32 dimension,u32 count,bool normal) {
    auto result=std::make_shared<texture_data>();result->description=source.description;
    result->description.width=result->description.height=dimension;result->description.mip_levels=count;
    u32 size=source.description.width,added=0;for (u32 n=dimension;n>size;n/=2) ++added;
    bool const pow2=size && (size&(size-1))==0;
    int const mask=int(size)-1;
    bool srgb=source.description.pixel_format==rhi::format::r8g8b8a8_srgb;
    std::array<double,256> decode={};
    for (unsigned i=0;i<256;++i) {double x=i/255.;decode[i]=srgb ? (x<=.04045 ? x/12.92 : std::pow((x+.055)/1.055,2.4)) : x;}
    auto byte=[](double value) {return static_cast<unsigned char>(std::clamp(std::lround(value*255),0l,255l));};
    u32 hw=std::thread::hardware_concurrency();if(!hw)hw=4;
    u32 const max_threads=std::min<u32>(hw,16u);
    auto const& base=source.mips[0];result->mips.reserve(count);
    for (u32 level=0;level<count;++level) {
        // Preserve every authored mip exactly; only finer levels above the source base are new.
        if (level>=added) {result->mips.push_back(source.mips[level-added]);continue;}
        u32 width=dimension>>level;auto& output=result->mips.emplace_back(size_t(width)*width*4);
        auto run_rows=[&](u32 y_begin,u32 y_end) {
            for (u32 y=y_begin;y<y_end;++y) for (u32 x=0;x<width;++x) {
                double sx=(x+.5)*size/width-.5,sy=(y+.5)*size/width-.5;
                int ix=int(std::floor(sx)),iy=int(std::floor(sy));double fx=sx-ix,fy=sy-iy;
                std::array<double,4> sum={};
                for (unsigned dy=0;dy<2;++dy) for (unsigned dx=0;dx<2;++dx) {
                    u32 px=pow2 ? u32((ix+int(dx))&mask) : u32((ix+int(dx)+int(size))%int(size));
                    u32 py=pow2 ? u32((iy+int(dy))&mask) : u32((iy+int(dy)+int(size))%int(size));
                    auto* pixel=base.data()+(size_t(py)*size+px)*4;double weight=(dx ? fx : 1-fx)*(dy ? fy : 1-fy);
                    if (normal) {
                        double nx=pixel[3]/127.5-1,ny=pixel[1]/127.5-1;
                        double sq=nx*nx+ny*ny;
                        double nz=sq<1.0 ? std::sqrt(1.0-sq) : 0.0;
                        double inv_len=sq>1.0 ? 1.0/std::sqrt(sq) : 1.0;
                        sum[0]+=weight*nx*inv_len;sum[1]+=weight*ny*inv_len;sum[2]+=weight*nz*inv_len;
                    } else for (unsigned c=0;c<4;++c) sum[c]+=weight*(c==3 ? pixel[c]/255. : decode[pixel[c]]);
                }
                auto* pixel=output.data()+(size_t(y)*width+x)*4;
                if (normal) {
                    double length=std::sqrt(sum[0]*sum[0]+sum[1]*sum[1]+sum[2]*sum[2]);
                    if (length<1e-12) {sum[0]=sum[1]=0;length=1;}
                    pixel[0]=255;pixel[1]=byte(sum[1]/length*.5+.5);pixel[2]=0;pixel[3]=byte(sum[0]/length*.5+.5);
                } else for (unsigned c=0;c<4;++c) {
                    double value=sum[c];if (srgb && c<3) value=value<=.0031308 ? value*12.92 : 1.055*std::pow(value,1/2.4)-.055;
                    pixel[c]=byte(value);
                }
            }
        };
        if (width>=128 && max_threads>1) {
            u32 threads=std::min<u32>(max_threads,std::max<u32>(1u,width/16u));
            if (threads<=1) run_rows(0u,width);
            else {
                u32 const chunk_base=width/threads,rem=width%threads;
                std::vector<std::thread> workers;workers.reserve(threads-1);
                u32 offset=chunk_base+(0u<rem?1u:0u);u32 const first_end=offset;
                for (u32 t=1;t<threads;++t) {
                    u32 const len=chunk_base+(t<rem?1u:0u),begin=offset,end=offset+len;
                    offset=end;workers.emplace_back([&,begin,end]{run_rows(begin,end);});
                }
                run_rows(0u,first_end);
                for (auto& w:workers) w.join();
            }
        } else run_rows(0u,width);
    }
    return result;
}
}
terrain_texture_plan plan_terrain_textures(terrain_texture_set const& sources) {
    auto limit=terrain_limits(sources.memory_profile).cell_bytes;
    auto input=roles(sources);auto count=sources.diffuse.size();
    if (!count || count>16 || sources.normal.size()!=count || sources.specular.size()!=count)
        throw std::runtime_error("Terrain normalization requires 1..16 complete triplets");
    terrain_texture_set single;single.memory_profile=sources.memory_profile;single.diffuse.resize(1);single.normal.resize(1);single.specular.resize(1);
    for (size_t slot=0;slot<count;++slot) {
        single.diffuse[0]=sources.diffuse[slot];single.normal[0]=sources.normal[slot];single.specular[0]=sources.specular[slot];
        try {validate_terrain_textures(single);} catch (std::runtime_error const& e) {
            throw std::runtime_error("Terrain normalization source slot "+std::to_string(slot)+": "+e.what());
        }
    }
    terrain_texture_plan plan;
    for (unsigned role=0;role<3;++role) {
        auto const& first=(*input[role])[0]->description;
        auto prefix=std::string("Terrain ")+(role==0 ? "diffuse" : role==1 ? "normal" : "specular")+": ";
        u32 dimension=first.width;bool mixed=false;
        for (auto const& image : *input[role]) {
            auto const& d=image->description;
            if (d.pixel_format!=first.pixel_format) throw std::runtime_error(prefix+"mixed formats require unsupported transcoding");
            dimension=std::max(dimension,d.width);mixed|=d.width!=first.width || d.mip_levels!=first.mip_levels;
        }
        u32 mips=mixed ? levels(dimension) : first.mip_levels;
        if (mixed) for (auto const& image : *input[role]) {
            auto const& d=image->description;
            if ((d.pixel_format!=rhi::format::r8g8b8a8_unorm && d.pixel_format!=rhi::format::r8g8b8a8_srgb)
                || (d.width&(d.width-1)) || d.mip_levels!=levels(d.width))
                throw std::runtime_error(prefix+"mixed arrays require power-of-two RGBA8 with authored full mip chains; no BC transcoding or missing-mip synthesis");
        }
        plan.dimensions[role]=dimension;plan.mip_levels[role]=mips;plan.normalized[role]=mixed;
        // Count array layers, including aliases, before any promoted pixel allocation.
        for (size_t slot=0;slot<count;++slot) {
            auto const& image=(*input[role])[slot];size_t bytes=0;
            if (mixed) bytes=rgba_bytes(dimension,mips);else for (auto const& mip : image->mips) bytes+=mip.size();
            if (bytes>limit-plan.array_bytes)
                throw std::runtime_error("Terrain normalized arrays exceed "+std::to_string(limit/(1024*1024))+" MiB; no automatic downsampling");
            plan.array_bytes+=bytes;
            if (mixed && image->description.width<dimension
                && std::find(input[role]->begin(),input[role]->begin()+slot,image)==input[role]->begin()+slot)
                plan.new_texture_bytes+=bytes;
        }
    }
    return plan;
}
std::shared_ptr<terrain_texture_set const> normalize_terrain_textures(terrain_texture_set const& sources,
    terrain_texture_set const* previous_sources,std::shared_ptr<terrain_texture_set const> previous) {
    auto plan=plan_terrain_textures(sources);
    if (previous_sources && previous && same(sources,*previous_sources)) return previous;
    auto result=std::make_shared<terrain_texture_set>();result->memory_profile=sources.memory_profile;auto input=roles(sources);auto output=roles(*result);
    for (unsigned role=0;role<3;++role) {
        auto const& list=*input[role];auto& target=*output[role];target.reserve(list.size());
        for (size_t slot=0;slot<list.size();++slot) {
            auto const& image=list[slot];
            if (layout(*image,plan.dimensions[role],plan.mip_levels[role],image->description.pixel_format)) {target.push_back(image);continue;}
            auto alias=std::find(list.begin(),list.begin()+slot,image);
            if (alias!=list.begin()+slot) {target.push_back(target[size_t(alias-list.begin())]);continue;}
            std::shared_ptr<texture_data const> reuse;
            if (previous_sources && previous) {
                auto old_sources=roles(*previous_sources),old=roles(*previous);
                for (size_t i=0;i<old_sources[role]->size() && i<old[role]->size();++i)
                    if ((*old_sources[role])[i]==image && layout(*(*old[role])[i],plan.dimensions[role],plan.mip_levels[role],image->description.pixel_format))
                        {reuse=(*old[role])[i];break;}
            }
            target.push_back(reuse ? reuse : promote(*image,plan.dimensions[role],plan.mip_levels[role],role==1));
        }
    }
    validate_terrain_textures(*result);
    return previous && same(*result,*previous) ? previous : result;
}
} } }
