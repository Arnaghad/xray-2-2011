#include "pch.h"
#include <xray/render/vulkan/material_resources.h>
#include <cstring>
#include <stdexcept>
namespace xray::render::vulkan {
std::shared_ptr<texture_data const> rgba_texture(std::shared_ptr<texture_data const> source) {
    if(!source)throw std::runtime_error("Missing BC texture");auto format=source->description.pixel_format;
    if(format==rhi::format::r8g8b8a8_unorm || format==rhi::format::r8g8b8a8_srgb)return source;
    unsigned kind=format==rhi::format::bc1_unorm || format==rhi::format::bc1_srgb?1:format==rhi::format::bc2_unorm || format==rhi::format::bc2_srgb?2:format==rhi::format::bc3_unorm || format==rhi::format::bc3_srgb?3:0;
    auto const& desc=source->description;
    if(!kind || !desc.width || !desc.height || desc.width>4096 || desc.height>4096 || !desc.mip_levels || desc.mip_levels>13 || !desc.array_layers || desc.array_layers>256 || source->mips.size()!=size_t(desc.mip_levels)*desc.array_layers)throw std::runtime_error("Unsupported BC-to-RGBA texture description");
    auto result=std::make_shared<texture_data>();result->description=desc;
    result->description.pixel_format=format==rhi::format::bc1_srgb || format==rhi::format::bc2_srgb || format==rhi::format::bc3_srgb?rhi::format::r8g8b8a8_srgb:rhi::format::r8g8b8a8_unorm;
    size_t aggregate=0;
    for(size_t mip=0;mip<source->mips.size();++mip) {
        unsigned level=unsigned(mip)%desc.mip_levels,w=std::max(1u,desc.width>>level),h=std::max(1u,desc.height>>level),block_size=kind==1?8:16;
        auto const& encoded=source->mips[mip];size_t size=size_t(w)*h*4;
        if(encoded.size()!=size_t((w+3)/4)*((h+3)/4)*block_size || size>128u*1024u*1024u-aggregate)throw std::runtime_error("BC mip bytes or RGBA expansion exceed bounds");aggregate+=size;
        auto& pixels=result->mips.emplace_back(size);size_t offset=0;
        for(unsigned y=0;y<h;y+=4)for(unsigned x=0;x<w;x+=4) {
            auto* block=encoded.data()+offset;auto* color=block+(kind==1?0:8);u16 a=color[0]|u16(color[1])<<8,b=color[2]|u16(color[3])<<8;unsigned palette[4][4]{};
            auto unpack=[](u16 c,unsigned* rgb){unsigned r=(c>>11)&31,g=(c>>5)&63,b=c&31;rgb[0]=(r<<3)|(r>>2);rgb[1]=(g<<2)|(g>>4);rgb[2]=(b<<3)|(b>>2);rgb[3]=255;};unpack(a,palette[0]);unpack(b,palette[1]);
            for(unsigned channel=0;channel<3;++channel) {
                palette[2][channel]=kind!=1 || a>b?(2*palette[0][channel]+palette[1][channel])/3:(palette[0][channel]+palette[1][channel])/2;
                palette[3][channel]=kind!=1 || a>b?(palette[0][channel]+2*palette[1][channel])/3:0;
            }
            palette[2][3]=255;palette[3][3]=kind!=1 || a>b?255:0;u32 indices=0;std::memcpy(&indices,color+4,4);u64 alpha_indices=0;unsigned alphas[8]{};
            if(kind==3){alphas[0]=block[0];alphas[1]=block[1];for(unsigned i=0;i<6;++i)alpha_indices|=u64(block[2+i])<<(i*8);
                if(alphas[0]>alphas[1])for(unsigned i=1;i<=6;++i)alphas[i+1]=((7-i)*alphas[0]+i*alphas[1])/7;
                else {for(unsigned i=1;i<=4;++i)alphas[i+1]=((5-i)*alphas[0]+i*alphas[1])/5;alphas[6]=0;alphas[7]=255;}}
            for(unsigned row=0;row<4;++row)for(unsigned col=0;col<4;++col)if(x+col<w && y+row<h){unsigned index=row*4+col,entry=(indices>>(index*2))&3;auto* p=pixels.data()+(size_t(y+row)*w+x+col)*4;
                for(unsigned c=0;c<4;++c)p[c]=u8(palette[entry][c]);if(kind==2)p[3]=u8(((block[index/2]>>((index%2)*4))&15)*17);else if(kind==3)p[3]=u8(alphas[(alpha_indices>>(index*3))&7]);}
            offset+=block_size;
        }
    }
    return result;
}
}
