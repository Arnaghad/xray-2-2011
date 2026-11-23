#include "pch.h"
#include <xray/render/vulkan/terrain_resources.h>
#include <xray/render/engine/model_format.h>
#include <zlib.h>
#include <cmath>
#include <cstring>
#include <limits>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {
struct checked_reader {
    std::span<unsigned char const> bytes;
    size_t offset=0;
    template<class T> T read() {
        if (sizeof(T)>bytes.size()-offset) throw std::runtime_error("Truncated terrain cell");
        T value; std::memcpy(&value,bytes.data()+offset,sizeof(T)); offset+=sizeof(T); return value;
    }
    std::string name() {
        std::string value;
        for (;;) {
            auto c=read<unsigned char>(); if (!c) break;
            if (c<32 || c>126 || value.size()>=255) throw std::runtime_error("Invalid terrain texture name");
            value+=char(c);
        }
        return value;
    }
};
void texture_name(std::string const& name) {
    if (name.empty() || name.size()>255) throw std::runtime_error("Empty/oversized terrain texture name");
    size_t start=0;
    for (size_t i=0;i<=name.size();++i) {
        if (i<name.size()) {
            unsigned char c=name[i];
            if (c<32 || c>126 || c=='\\' || c==':' || c=='"' || c=='*' || c=='?' || c=='<' || c=='>')
                throw std::runtime_error("Unsafe terrain texture path");
            if (c!='/') continue;
        }
        auto part=name.substr(start,i-start);
        if (part.empty() || part=="." || part=="..") throw std::runtime_error("Unsafe terrain texture component");
        start=i+1;
    }
}
void finite(float value) {
    if (!std::isfinite(value) || std::abs(value)>1e6f) throw std::runtime_error("Terrain value exceeds finite bounds");
}
void validate(terrain_cell_data const& cell,bool runtime_slots=false) {
    if (cell.row_size<2 || cell.row_size>256 || cell.samples.size()!=size_t(cell.row_size)*cell.row_size)
        throw std::runtime_error("Terrain grid requires 2..256 rows and exact sample count");
    if (!std::isfinite(cell.physical_size) || cell.physical_size<.001f || cell.physical_size>65536)
        throw std::runtime_error("Terrain physical size is outside .001..65536");
    if (cell.textures.empty() || cell.textures.size()>16) throw std::runtime_error("Terrain requires 1..16 textures");
    // Distinct user slots may alias the same texture (legacy texture pool reuses it).
    bool occupied=false;
    for (auto const& name : cell.textures) {
        if (runtime_slots && name.empty()) continue;
        texture_name(name);occupied=true;
    }
    if (!occupied) throw std::runtime_error("Terrain requires at least one occupied texture slot");
    auto const& m=cell.transform;
    for (auto const& row : m.elements) for (float v : row) finite(v);
    if (m.elements[0][3]!=0 || m.elements[1][3]!=0 || m.elements[2][3]!=0 || m.elements[3][3]!=1)
        throw std::runtime_error("Terrain transform must be affine");
    double determinant=double(m.i.x)*(double(m.j.y)*m.k.z-double(m.j.z)*m.k.y)
        -double(m.i.y)*(double(m.j.x)*m.k.z-double(m.j.z)*m.k.x)
        +double(m.i.z)*(double(m.j.x)*m.k.y-double(m.j.y)*m.k.x);
    if (determinant<1e-8) throw std::runtime_error("Singular/reflected terrain transform is unsupported");
    for (auto const& sample : cell.samples) {
        finite(sample.height); for (float v : sample.shift) finite(v);
        // Zero-weight slots are still indexed by the legacy shader; validate all three.
        for (u8 id : sample.texture_ids) if (id>=cell.textures.size() || cell.textures[id].empty())
            throw std::runtime_error("Terrain texture index references an absent slot");
    }
    if (runtime_slots && std::find(cell.textures.begin(),cell.textures.end(),std::string())!=cell.textures.end()) {
        // IDs are interpolated and rounded by both terrain shaders, even for zero-weight roles.
        auto triangle=[&](size_t a,size_t b,size_t c) {
            for (unsigned role=0;role<3;++role) {
                unsigned low=std::min({cell.samples[a].texture_ids[role],cell.samples[b].texture_ids[role],cell.samples[c].texture_ids[role]});
                unsigned high=std::max({cell.samples[a].texture_ids[role],cell.samples[b].texture_ids[role],cell.samples[c].texture_ids[role]});
                for (unsigned id=low;id<=high;++id) if (cell.textures[id].empty())
                    throw std::runtime_error("Interpolated terrain texture ID crosses an absent slot");
            }
        };
        for (u32 y=0;y<cell.row_size-1;++y) for (u32 x=0;x<cell.row_size-1;++x) {
            size_t a=size_t(y)*cell.row_size+x,b=a+1,c=a+cell.row_size,d=c+1;
            triangle(a,b,c);triangle(b,d,c);
        }
    }
}
u32 pack(double x,double y,double z) {
    double length=std::sqrt(x*x+y*y+z*z);
    auto channel=[&](double value) { return u32(std::clamp(std::floor((value/length*.5+.5)*255),0.,255.)); };
    // The renderer's base_basis stores Z,Y,X in low-to-high bytes.
    return channel(z)|(channel(y)<<8)|(channel(x)<<16);
}
std::shared_ptr<static_mesh_data const> geometry(terrain_cell_data const& cell) {
    auto result=std::make_shared<static_mesh_data>(); u32 row=cell.row_size;
    result->vertices.resize(cell.samples.size()); result->indices.reserve(size_t(row-1)*(row-1)*6);
    result->minimum.fill(std::numeric_limits<float>::max()); result->maximum.fill(std::numeric_limits<float>::lowest());
    double step=double(cell.physical_size)/(row-1);
    for (u32 y=0;y<row;++y) for (u32 x=0;x<row;++x) {
        auto& v=result->vertices[size_t(y)*row+x]; auto const& sample=cell.samples[size_t(y)*row+x];
        v.position[0]=float(x*step); v.position[1]=sample.height; v.position[2]=float(-double(y)*step);
        auto height=[&](u32 a,u32 b) { return double(cell.samples[size_t(b)*row+a].height); };
        u32 left=x ? x-1 : x,right=x+1<row ? x+1 : x,up=y ? y-1 : y,down=y+1<row ? y+1 : y;
        double dx=(height(right,y)-height(left,y))/((right-left)*step);
        double dz=(height(x,up)-height(x,down))/((down-up)*step);
        v.normal=pack(-dx,1,-dz); v.tangent=pack(1,dx,0);
        // Orthonormal B=cross(N,T); UV V increases toward negative local Z.
        v.binormal=pack(dx*dz,-dz,-(1+dx*dx));
        v.uv[0]=float(x)/float(row-1); v.uv[1]=float(y)/float(row-1);
        for (unsigned axis=0;axis<3;++axis) {
            result->minimum[axis]=std::min(result->minimum[axis],v.position[axis]);
            result->maximum[axis]=std::max(result->maximum[axis],v.position[axis]);
        }
    }
    for (u32 y=0;y<row-1;++y) for (u32 x=0;x<row-1;++x) {
        u16 a=u16(y*row+x),b=u16(y*row+x+1),c=u16((y+1)*row+x),d=u16((y+1)*row+x+1);
        for (u16 i : {a,b,c,b,d,c}) result->indices.push_back(i);
    }
    return result;
}
}
void validate_terrain_texture_name(std::string const& name) { texture_name(name); }
std::shared_ptr<terrain_cell_data const> load_terrain_cell(const_buffer source) {
    if (!source.c_ptr() || source.size()<8 || source.size()>terrain_cell_data::byte_limit+8)
        throw std::runtime_error("Missing/oversized terrain cell");
    checked_reader file{{static_cast<unsigned char const*>(source.c_ptr()),source.size()}};
    u32 id=file.read<u32>(),size=file.read<u32>();
    if (size!=source.size()-8 || (id!=model_chunk_terrain_data && id!=model_chunk_terrain_data_compressed1))
        throw std::runtime_error("Terrain requires exactly one chunk 102 or 103");
    std::vector<unsigned char> inflated;
    std::span<unsigned char const> payload=file.bytes.subspan(8);
    if (id==model_chunk_terrain_data_compressed1) {
        inflated.resize(terrain_cell_data::byte_limit);
        z_stream stream={}; stream.next_in=const_cast<Bytef*>(payload.data()); stream.avail_in=uInt(payload.size());
        stream.next_out=inflated.data(); stream.avail_out=uInt(inflated.size());
        if (inflateInit(&stream)!=Z_OK) throw std::runtime_error("Cannot initialize terrain zlib stream");
        int status=inflate(&stream,Z_FINISH); size_t output=stream.total_out,remaining=stream.avail_in;
        int ended=inflateEnd(&stream);
        if (status!=Z_STREAM_END || ended!=Z_OK || remaining || !output)
            throw std::runtime_error("Corrupt, oversized or trailing terrain zlib data");
        inflated.resize(output); payload=inflated;
    }
    checked_reader reader{payload}; auto cell=std::make_shared<terrain_cell_data>();
    cell->row_size=reader.read<u32>(); cell->physical_size=reader.read<float>(); u32 count=reader.read<u32>();
    if (cell->row_size<2 || cell->row_size>256 || !count || count>16) throw std::runtime_error("Invalid terrain header counts");
    for (u32 i=0;i<count;++i) cell->textures.push_back(reader.name());
    cell->transform.identity();
    for (unsigned i=0;i<4;++i) for (unsigned j=0;j<3;++j) cell->transform.elements[i][j]=reader.read<float>();
    size_t samples=size_t(cell->row_size)*cell->row_size;
    if (payload.size()-reader.offset!=samples*sizeof(terrain_sample)) throw std::runtime_error("Unsupported terrain sample stride/count (22 bytes required)");
    cell->samples.resize(samples); std::memcpy(cell->samples.data(),payload.data()+reader.offset,samples*sizeof(terrain_sample));
    validate(*cell); cell->mesh=geometry(*cell); return cell;
}
std::shared_ptr<terrain_cell_data const> update_terrain_cell(std::shared_ptr<terrain_cell_data const> const& previous,
    std::span<terrain_cell_fragment const> fragments) {
    if (!previous) throw std::runtime_error("Missing terrain snapshot");
    validate(*previous,true);
    if (fragments.empty() || fragments.size()>4096) throw std::runtime_error("Terrain update requires 1..4096 fragments");
    std::vector<bool> touched(previous->samples.size(),false); size_t total=0;
    for (auto const& f : fragments) {
        u32 row=previous->row_size;
        if (!f.width || !f.height || f.x>=row || f.y>=row || f.width>row-f.x || f.height>row-f.y
            || f.samples.size()!=size_t(f.width)*f.height || !f.samples.data())
            throw std::runtime_error("Invalid terrain update rectangle/sample count");
        total+=f.samples.size(); if (total>previous->samples.size()) throw std::runtime_error("Terrain update exceeds one cell");
        for (u32 y=0;y<f.height;++y) for (u32 x=0;x<f.width;++x) {
            size_t index=size_t(f.y+y)*row+f.x+x;
            if (touched[index]) throw std::runtime_error("Overlapping terrain fragments are unsupported");
            touched[index]=true;
        }
    }
    auto next=std::make_shared<terrain_cell_data>(*previous);
    for (auto const& f : fragments) for (u32 y=0;y<f.height;++y)
        std::memcpy(next->samples.data()+size_t(f.y+y)*next->row_size+f.x,f.samples.data()+size_t(y)*f.width,size_t(f.width)*sizeof(terrain_sample));
    validate(*next,true);
    if (!std::memcmp(next->samples.data(),previous->samples.data(),next->samples.size()*sizeof(terrain_sample))) return previous;
    next->mesh=geometry(*next); return next;
}
std::shared_ptr<terrain_cell_data const> rebind_terrain_cell(std::shared_ptr<terrain_cell_data const> const& previous,
    std::vector<std::string> const& names) {
    if (!previous || !previous->mesh) throw std::runtime_error("Missing terrain geometry snapshot");
    validate(*previous,true);
    if (previous->textures==names) return previous;
    auto next=std::make_shared<terrain_cell_data>(*previous);next->textures=names;validate(*next,true);
    // Binding metadata never changes vertex attributes; sample edits always allocate a new mesh identity.
    return next;
}
} } }
