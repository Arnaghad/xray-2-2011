#include "pch.h"
#include <xray/render/vulkan/material_resources.h>
#include <array>
#include <cmath>
#include <cstring>
#include <cstdlib>
#include <climits>
#include <stdexcept>
#include <thread>
#define STB_IMAGE_IMPLEMENTATION
#define STB_IMAGE_STATIC
#define STBI_ONLY_TGA
#define STBI_NO_STDIO
#define STBI_MAX_DIMENSIONS 12288
// Codec memory is bounded and owned/freed entirely inside this adapter.
#define STBI_MALLOC(size) (std::malloc)(size)
#define STBI_REALLOC(pointer,size) (std::realloc)(pointer,size)
#define STBI_FREE(pointer) (std::free)(pointer)
#ifdef __GNUC__
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wunused-function"
#endif
#include <third_party/stb/stb_image.h>
#define STB_DXT_IMPLEMENTATION
#define STB_DXT_STATIC
#include <third_party/stb/stb_dxt.h>
#ifdef __GNUC__
#pragma GCC diagnostic pop
#endif

namespace xray { namespace render { namespace vulkan {
namespace {
template <typename F>
void parallel_for_range(u32 count,u32 min_work_per_thread,F&& fn) {
    if (!count) return;
    u32 hw=std::thread::hardware_concurrency();
    if (!hw) hw=4;
    u32 max_threads=std::min<u32>(hw,16u);
    u32 threads=std::min<u32>(max_threads,std::max<u32>(1u,count/std::max<u32>(1u,min_work_per_thread)));
    threads=std::min<u32>(threads,count);
    if (threads<=1) { fn(0u,count); return; }
    u32 const base=count/threads,rem=count%threads;
    std::vector<std::thread> workers;
    workers.reserve(threads-1);
    u32 offset=base+(0u<rem?1u:0u);
    u32 const first_end=offset;
    for (u32 t=1;t<threads;++t) {
        u32 const len=base+(t<rem?1u:0u);
        u32 const begin=offset,end=offset+len;
        offset=end;
        workers.emplace_back([&,begin,end]{ fn(begin,end); });
    }
    fn(0u,first_end);
    for (auto& worker:workers) worker.join();
}
// BC1 uses 1-bit alpha; BC2 uses 4-bit alpha; BC3 keeps the original DXT5n swizzle.
void compress_texture(texture_data& texture,unsigned compression,bool high_quality) {
    if(!compression)return;if(compression>5)throw std::runtime_error("Unsupported source block compression");
    if(compression==4){texture.description.pixel_format=rhi::format::r8g8b8a8_unorm;for(auto& mip:texture.mips)for(size_t p=0;p<mip.size();p+=4)mip[p]=mip[p+1]=mip[p+2]=255;return;}
    bool binary_alpha=compression==5;if(binary_alpha)compression=1;
    bool srgb=texture.description.pixel_format==rhi::format::r8g8b8a8_srgb;
    texture.description.pixel_format=compression==1?(srgb?rhi::format::bc1_srgb:rhi::format::bc1_unorm):compression==2?(srgb?rhi::format::bc2_srgb:rhi::format::bc2_unorm):(srgb?rhi::format::bc3_srgb:rhi::format::bc3_unorm);
    int quality=high_quality?STB_DXT_HIGHQUAL:STB_DXT_NORMAL;
    for(size_t mip=0;mip<texture.mips.size();++mip) {
        unsigned level=unsigned(mip)%texture.description.mip_levels,w=std::max(1u,texture.description.width>>level),h=std::max(1u,texture.description.height>>level),size=compression==1?8:16;
        u32 bx=(w+3)/4,by=(h+3)/4;
        std::vector<unsigned char> encoded(size_t(bx)*by*size);auto const& pixels=texture.mips[mip];
        parallel_for_range(by,bx>=64?4u:64u,[&](u32 row_begin,u32 row_end) {
            for(u32 row_y=row_begin;row_y<row_end;++row_y) {
                unsigned y=row_y*4;
                for(u32 col_x=0;col_x<bx;++col_x) {
                    unsigned x=col_x*4;
                    unsigned char block[64];for(unsigned row=0;row<4;++row)for(unsigned col=0;col<4;++col)std::memcpy(block+(row*4+col)*4,pixels.data()+(size_t(std::min(y+row,h-1))*w+std::min(x+col,w-1))*4,4);
                    auto* output=encoded.data()+(size_t(row_y)*bx+col_x)*size;
                    bool alpha=false;for(unsigned i=0;i<16;++i)alpha|=block[i*4+3]<128;
                    if(compression==2){for(unsigned i=0;i<8;++i)output[i]=static_cast<unsigned char>((block[i*8+3]+8)/17 | (((block[i*8+7]+8)/17)<<4));stb_compress_dxt_block(output+8,block,0,quality);}
                    else {stb_compress_dxt_block(output,block,compression==3,quality);
                        if(compression==1 && binary_alpha && alpha) {
                            // Refit the legal three-color BC1 palette and reserve index 3 for alpha.
                            u16 a=output[0]|u16(output[1])<<8,b=output[2]|u16(output[3])<<8;if(a>b)std::swap(a,b);output[0]=u8(a);output[1]=u8(a>>8);output[2]=u8(b);output[3]=u8(b>>8);
                            int colors[3][3];auto unpack=[](u16 c,int* rgb){rgb[0]=((c>>11)&31)*255/31;rgb[1]=((c>>5)&63)*255/63;rgb[2]=(c&31)*255/31;};unpack(a,colors[0]);unpack(b,colors[1]);for(unsigned c=0;c<3;++c)colors[2][c]=(colors[0][c]+colors[1][c])/2;
                            u32 bits=0;for(unsigned i=0;i<16;++i){unsigned chosen=3;if(block[i*4+3]>=128){int best=INT_MAX;for(unsigned k=0;k<3;++k){int error=0;for(unsigned c=0;c<3;++c){int delta=int(block[i*4+c])-colors[k][c];error+=delta*delta;}if(error<best){best=error;chosen=k;}}}bits|=chosen<<(i*2);}std::memcpy(output+4,&bits,4);
                        }
                    }
                }
            }
        });
        texture.mips[mip]=std::move(encoded);
    }
}
}
std::shared_ptr<texture_data const> load_tga_texture(const_buffer source,bool srgb,bool generate_mips,bool pack_normal,bool cube,unsigned compression,bool high_quality,unsigned dimension) {
    if (cube && dimension==1) dimension=3;
    if (dimension<1 || dimension>4) throw std::runtime_error("Unsupported source texture dimension");
    if (pack_normal) srgb=false;
    if (dimension!=1 && pack_normal) throw std::runtime_error("Multi-layer/volume NORMAL_MAP packing is unsupported");
    if (!source.c_ptr() || source.size()<18 || source.size()>(dimension>1 ? 128u : 64u)*1024u*1024u)
        throw std::runtime_error("TGA is truncated or exceeds its 2D/multi-layer input limit");
    auto* bytes=static_cast<unsigned char const*>(source.c_ptr());
    auto word=[&](size_t offset) { return unsigned(bytes[offset])|(unsigned(bytes[offset+1])<<8); };
    u32 width=word(12),height=word(14),bits=bytes[16],descriptor=bytes[17];
    bool rle=bytes[2]==10 || bytes[2]==11,gray=bytes[2]==3 || bytes[2]==11;
    if (pack_normal && gray) throw std::runtime_error("NORMAL_MAP source requires RGB(A) TGA input");
    if (bytes[1] || word(3) || word(5) || bytes[7] || (descriptor&0xc0)
        || (bytes[2]!=2 && bytes[2]!=3 && bytes[2]!=10 && bytes[2]!=11)
        || (gray ? bits!=8 && bits!=16 : bits!=24 && bits!=32)
        || ((descriptor&15)!=0 && (descriptor&15)!=8) || (bits==24 && (descriptor&15)!=0))
        throw std::runtime_error("Unsupported TGA: expected non-paletted RGB(A)/gray, raw or RLE");
    u32 slice_width=width,slice_height=height,layers=1;
    rhi::texture_type texture_type=rhi::texture_type::type_2d;
    if (dimension==1) {
        if (!width || !height || width>4096 || height>4096)
            throw std::runtime_error("Expected TGA 2D dimensions <=4096");
    } else if (dimension==2) {
        if (height<2 || height>64 || width!=height*height)
            throw std::runtime_error("Expected TGA 3D volume strip height in [2,64] with width == height*height");
        slice_width=height; slice_height=height; layers=height; texture_type=rhi::texture_type::type_2d_array;
    } else if (dimension==3) {
        if (!height || height>2048 || width!=6*height)
            throw std::runtime_error("Expected TGA six-face horizontal cube strip <=12288x2048");
        slice_width=height; slice_height=height; layers=6; texture_type=rhi::texture_type::type_cube;
    } else {
        if (!height || height>2048 || width<=height || width>12288 || (width%height)!=0 || width/height>64)
            throw std::runtime_error("Expected TGA 2D array horizontal strip <=12288 with 2..64 square slices");
        slice_width=height; slice_height=height; layers=width/height; texture_type=rhi::texture_type::type_2d_array;
    }
    size_t offset=18u+bytes[0],pixels=size_t(width)*height,pixel_size=bits/8;
    if (offset>source.size()) throw std::runtime_error("Truncated TGA ID");
    // stb's TGA decoder can tolerate short reads; reject incomplete/oversized
    // packets here before handing the actual pixel decoding to the library.
    size_t remaining=pixels;
    while (remaining) {
        size_t count=remaining,stored=count;
        if (rle) {
            if (offset==source.size()) throw std::runtime_error("Truncated TGA RLE packet");
            unsigned packet=bytes[offset++]; count=(packet&127)+1; stored=(packet&128) ? 1 : count;
        }
        if (count>remaining || stored*pixel_size>source.size()-offset) throw std::runtime_error("Invalid TGA pixel payload");
        offset+=stored*pixel_size; remaining-=count;
    }
    if (offset!=source.size()) {
        if (source.size()<offset+26 || std::memcmp(bytes+source.size()-18,"TRUEVISION-XFILE.\0",18))
            throw std::runtime_error("Unsupported TGA trailing data");
        for (size_t field=source.size()-26;field<source.size()-18;field+=4) {
            u32 pointer=0; std::memcpy(&pointer,bytes+field,4);
            if (pointer && (pointer<offset || pointer>=source.size()-26)) throw std::runtime_error("Invalid TGA metadata offset");
        }
    }
    int decoded_width=0,decoded_height=0,channels=0;
    std::unique_ptr<stbi_uc,void(*)(void*)> decoded(stbi_load_from_memory(bytes,int(source.size()),&decoded_width,&decoded_height,&channels,4),stbi_image_free);
    if (!decoded || decoded_width!=int(width) || decoded_height!=int(height)) throw std::runtime_error("TGA decode failed");
    auto result=std::make_shared<texture_data>();
    result->description.width=slice_width; result->description.height=slice_height;
    result->description.type=texture_type;
    result->description.array_layers=layers;
    result->description.pixel_format=srgb ? rhi::format::r8g8b8a8_srgb : rhi::format::r8g8b8a8_unorm;
    std::vector<unsigned char> image(decoded.get(),decoded.get()+pixels*4);
    decoded.reset();
    if (descriptor&16) for (u32 y=0;y<height;++y) for (u32 x=0;x<width/2;++x)
        for (unsigned c=0;c<4;++c) std::swap(image[(size_t(y)*width+x)*4+c],image[(size_t(y)*width+width-1-x)*4+c]);
    static std::array<double,256> const srgb_decode=[] {
        std::array<double,256> lut{};
        for (unsigned i=0;i<256;++i) {
            double v=i/255.0;
            lut[i]=v<=.04045 ? v/12.92 : std::pow((v+.055)/1.055,2.4);
        }
        return lut;
    }();
    auto normalize_normals=[](std::vector<unsigned char>& pixels) {
        u32 const count=u32(pixels.size()/4);
        parallel_for_range(count,16384u,[&](u32 begin,u32 end) {
            for (u32 idx=begin;idx<end;++idx) {
                size_t i=size_t(idx)*4;
                double n[3]={pixels[i]*2.0/255-1,pixels[i+1]*2.0/255-1,pixels[i+2]*2.0/255-1};
                double length=std::sqrt(n[0]*n[0]+n[1]*n[1]+n[2]*n[2]);
                if (length<1e-12) { n[0]=n[1]=0; n[2]=1; length=1; }
                for (unsigned c=0;c<3;++c) pixels[i+c]=static_cast<unsigned char>(std::clamp(std::lround((n[c]/length*.5+.5)*255),0l,255l));
            }
        });
    };
    u32 image_width=width;
    for (u32 layer=0;layer<result->description.array_layers;++layer) {
        width=result->description.width; height=result->description.height;
        auto& base=result->mips.emplace_back(size_t(width)*height*4);
        for (u32 y=0;y<height;++y) std::memcpy(base.data()+size_t(y)*width*4,image.data()+(size_t(y)*image_width+layer*width)*4,size_t(width)*4);
        if (pack_normal) normalize_normals(base);
        u32 levels=1;
        while (generate_mips && (width>1 || height>1)) {
            u32 next_width=std::max(1u,width/2),next_height=std::max(1u,height/2);
            std::vector<unsigned char> mip(size_t(next_width)*next_height*4);
            auto const& previous=result->mips.back();
            parallel_for_range(next_height,next_width>=128?8u:128u,[&](u32 y0_row,u32 y1_row) {
                for (u32 y=y0_row;y<y1_row;++y) for (u32 x=0;x<next_width;++x) {
                    u32 x0=x*width/next_width,x1=(x+1)*width/next_width,y0=y*height/next_height,y1=(y+1)*height/next_height;
                    for (unsigned c=0;c<4;++c) {
                        double sum=0;
                        for (u32 sy=y0;sy<y1;++sy) for (u32 sx=x0;sx<x1;++sx) {
                            unsigned char raw=previous[(size_t(sy)*width+sx)*4+c];
                            sum+=(srgb && c<3)?srgb_decode[raw]:(raw/255.0);
                        }
                        double value=sum/((x1-x0)*(y1-y0));
                        if (srgb && c<3) value=value<=.0031308 ? value*12.92 : 1.055*std::pow(value,1.0/2.4)-.055;
                        mip[(size_t(y)*next_width+x)*4+c]=static_cast<unsigned char>(std::clamp(std::lround(value*255),0l,255l));
                    }
                }
            });
            if (pack_normal) normalize_normals(mip);
            result->mips.push_back(std::move(mip)); width=next_width; height=next_height; ++levels;
        }
        result->description.mip_levels=levels;
    }
    // Match the legacy NVTT DXT5n swizzle without lossy block compression.
    if (pack_normal) for (auto& mip : result->mips) for (size_t i=0;i<mip.size();i+=4) {
        mip[i+3]=mip[i]; mip[i]=255; mip[i+2]=0;
    }
    compress_texture(*result,compression,high_quality);return result;
}
} } }
