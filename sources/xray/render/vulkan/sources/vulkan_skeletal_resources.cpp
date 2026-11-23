#include "pch.h"
#include <xray/render/vulkan/skeletal_resources.h>
#include <xray/render/engine/model_format.h>
#include <cmath>
#include <cstring>
#include <limits>
#include <set>
#include <stdexcept>
namespace xray { namespace render { namespace vulkan {
namespace {
struct checked_reader {
    std::span<unsigned char const> bytes;
    size_t offset=0;
    explicit checked_reader(const_buffer source) {
        if (!source.c_ptr() || source.size()>64u*1024u*1024u) throw std::runtime_error("Missing or oversized skeletal data");
        bytes={static_cast<unsigned char const*>(source.c_ptr()),source.size()};
    }
    template<class T> T read() {
        if (sizeof(T)>bytes.size()-offset) throw std::runtime_error("Truncated skeletal data");
        T value; std::memcpy(&value,bytes.data()+offset,sizeof(T)); offset+=sizeof(T); return value;
    }
    std::string name() {
        std::string value;
        for (;;) { char c=read<char>(); if (!c) break; if (value.size()>=127) throw std::runtime_error("Bone name exceeds 127 bytes"); value+=c; }
        if (value.empty()) throw std::runtime_error("Empty bone name"); return value;
    }
    void end() const { if (offset!=bytes.size()) throw std::runtime_error("Unexpected skeletal trailing data"); }
};
void finite(float value) {
    if (!std::isfinite(value) || std::abs(value)>1e6f) throw std::runtime_error("Skeletal value exceeds finite bounds");
}
void vector(math::float3 const& v) { finite(v.x); finite(v.y); finite(v.z); }
void basis(math::float3 const& v) { vector(v); if (v.squared_length()<1e-12f) throw std::runtime_error("Degenerate skeletal basis"); }
void rigid(math::float4x4 const& m) {
    validate_skeleton_pose(&m,1);
    for (unsigned i=0;i<3;++i) for (unsigned j=0;j<3;++j) {
        float dot=0; for (unsigned k=0;k<3;++k) dot+=m.elements[i][k]*m.elements[j][k];
        if (std::abs(dot-(i==j ? 1.f : 0.f))>.002f) throw std::runtime_error("Skeletal palette requires rigid transforms");
    }
    if (math::dot_product(math::cross_product(m.i.xyz(),m.j.xyz()),m.k.xyz())<.998f)
        throw std::runtime_error("Skeletal palette reflection is unsupported");
}
math::float3 unpack(u32 packed) { return math::float3(float((packed>>16)&255),float((packed>>8)&255),float(packed&255))*(2.f/255)-math::float3(1,1,1); }
u32 pack(math::float3 value,u32 alpha) {
    basis(value); base_basis b; b.set(value); return math::color_rgba(u32(b.x),b.y,b.z,alpha);
}
}
void validate_skeleton_pose(math::float4x4 const* matrices,u32 count) {
    if (!matrices || !count || count>skeletal_bone_limit) throw std::runtime_error("Skeleton pose requires 1..128 matrices");
    for (u32 i=0;i<count;++i) {
        auto const& m=matrices[i]; for (auto const& row : m.elements) for (float v : row) finite(v);
        if (m.elements[0][3]!=0 || m.elements[1][3]!=0 || m.elements[2][3]!=0 || m.elements[3][3]!=1)
            throw std::runtime_error("Skeleton pose must be affine");
    }
}
skeletal_bind_pose load_skeletal_bind_pose(const_buffer source) {
    checked_reader reader(source); u16 count=reader.read<u16>();
    if (!count || count>skeletal_bone_limit) throw std::runtime_error("Bind pose requires 1..128 bones");
    skeletal_bind_pose result; std::set<std::string> names;
    for (u16 i=0;i<count;++i) {
        auto name=reader.name();
        if (!names.insert(name).second) throw std::runtime_error("Duplicate bind-pose bone name");
        result.names.push_back(std::move(name));
        auto p=reader.read<math::float3>(),r=reader.read<math::float3>(),s=reader.read<math::float3>();
        vector(p); vector(r); vector(s);
        if (s.x<=0 || s.y<=0 || s.z<=0) throw std::runtime_error("Bind-pose scale must be positive");
        auto matrix=math::create_scale(s)*math::create_rotation(r)*math::create_translation(p); math::float4x4 inverse;
        if (!inverse.try_invert(matrix)) throw std::runtime_error("Singular bind pose");
        validate_skeleton_pose(&inverse,1); result.inverse.push_back(inverse);
    }
    reader.end(); return result;
}
std::vector<math::float4x4> load_bind_pose(const_buffer source) { return load_skeletal_bind_pose(source).inverse; }
std::shared_ptr<skeletal_mesh_data const> load_skeletal_mesh(const_buffer source,u32 bones) {
    if (!bones || bones>skeletal_bone_limit) throw std::runtime_error("Invalid skeletal bone count");
    checked_reader file(source); const_buffer vertices,indices;
    while (file.offset<file.bytes.size()) {
        u32 id=file.read<u32>(),size=file.read<u32>();
        if ((size&0xc0000000u) || size>file.bytes.size()-file.offset) throw std::runtime_error("Truncated/unsupported skeletal chunk");
        auto* target=id==model_chunk_vertices ? &vertices : id==model_chunk_indices ? &indices : nullptr;
        if (!target || target->c_ptr()) throw std::runtime_error("Unsupported/duplicate skeletal chunk");
        *target=const_buffer(file.bytes.data()+file.offset,size); file.offset+=size;
    }
    checked_reader vr(vertices),ir(indices); u32 count=vr.read<u32>();
    if (!count || count>65536) throw std::runtime_error("Invalid skeletal vertex count");
    size_t payload=vr.bytes.size()-vr.offset;
    bool raw=payload==size_t(count)*sizeof(vert_boned_4w);
    if (!raw && payload!=size_t(count)*sizeof(hardware_4weights_skinning_vertex)) throw std::runtime_error("Unsupported skeletal vertex stride (4-weight raw/converted required)");
    auto result=std::make_shared<skeletal_mesh_data>(); result->vertices.reserve(count);
    result->bone_aabbs.resize(bones);
    for (auto& b : result->bone_aabbs) { b.minimum.fill(std::numeric_limits<float>::max()); b.maximum.fill(std::numeric_limits<float>::lowest()); }
    for (u32 i=0;i<count;++i) {
        skeletal_vertex v;
        if (raw) {
            auto disk=vr.read<vert_boned_4w>(); v.position=disk.P; v.normal=disk.N; v.tangent=disk.T; v.binormal=disk.B; v.uv=disk.uv;
            for (unsigned j=0;j<4;++j) v.bones[j]=j && disk.m[j]==u16(-1) ? disk.m[0] : disk.m[j];
            std::copy(disk.w,disk.w+3,v.weights.begin());
        } else {
            auto disk=vr.read<hardware_4weights_skinning_vertex>(); v.position=disk.P; v.normal=unpack(disk.N); v.tangent=unpack(disk.T); v.binormal=unpack(disk.B); v.uv=disk.uv;
            std::copy(disk.indices,disk.indices+4,v.bones.begin()); std::copy(disk.weights,disk.weights+3,v.weights.begin());
        }
        vector(v.position); basis(v.normal); basis(v.tangent); basis(v.binormal); finite(v.uv.x); finite(v.uv.y);
        double total=0;
        for (unsigned j=0;j<4;++j) {
            if (v.bones[j]>=bones) throw std::runtime_error("Skeletal bone index exceeds bind pose");
            if (j<3) { if (!std::isfinite(v.weights[j]) || v.weights[j]<0 || v.weights[j]>1) throw std::runtime_error("Invalid skeletal weight"); total+=v.weights[j]; }
        }
        if (total>1.00001) throw std::runtime_error("Skeletal weights exceed one");
        v.weights[3]=float(std::max(0.0,1-total));
        // Permit only float-rounding excess, not arbitrary weight normalization.
        if (total>1) for (float& w : v.weights) w=float(w/total);
        for (unsigned j=0;j<4;++j) if (v.weights[j]>0.f) {
            auto& b=result->bone_aabbs[v.bones[j]]; b.used=true;
            b.minimum[0]=std::min(b.minimum[0],v.position.x); b.minimum[1]=std::min(b.minimum[1],v.position.y); b.minimum[2]=std::min(b.minimum[2],v.position.z);
            b.maximum[0]=std::max(b.maximum[0],v.position.x); b.maximum[1]=std::max(b.maximum[1],v.position.y); b.maximum[2]=std::max(b.maximum[2],v.position.z);
        }
        result->vertices.push_back(v);
    }
    u32 index_count=ir.read<u32>();
    if (!index_count || index_count%3 || ir.bytes.size()-ir.offset!=size_t(index_count)*sizeof(u16)) throw std::runtime_error("Invalid skeletal triangle indices");
    result->indices.reserve(index_count);
    for (u32 i=0;i<index_count;++i) { auto index=ir.read<u16>(); if (index>=count) throw std::runtime_error("Skeletal index exceeds vertices"); result->indices.push_back(index); }
    return result;
}
std::shared_ptr<static_mesh_data const> skin_skeletal_mesh(skeletal_mesh_data const& source,std::span<math::float4x4 const> palette) {
    if (palette.empty() || palette.size()>skeletal_bone_limit || source.vertices.empty() || source.vertices.size()>65536 || source.indices.empty())
        throw std::runtime_error("Invalid skeletal mesh/palette");
    for (auto const& m : palette) rigid(m);
    if (source.indices.size()%3 || source.vertices.size()*sizeof(skeletal_vertex)+source.indices.size()*sizeof(u16)>64u*1024u*1024u)
        throw std::runtime_error("Invalid/oversized skeletal geometry");
    for (u16 index : source.indices) if (index>=source.vertices.size()) throw std::runtime_error("Skeletal index exceeds vertices");
    auto result=std::make_shared<static_mesh_data>(); result->indices=source.indices; result->vertices.reserve(source.vertices.size());
    result->minimum.fill(std::numeric_limits<float>::max()); result->maximum.fill(std::numeric_limits<float>::lowest());
    for (auto const& v : source.vertices) {
        vector(v.position); basis(v.normal); basis(v.tangent); basis(v.binormal); finite(v.uv.x); finite(v.uv.y);
        double total=0;
        for (float w : v.weights) { if (!std::isfinite(w) || w<0 || w>1) throw std::runtime_error("Invalid skeletal weight"); total+=w; }
        if (std::abs(total-1)>1e-5) throw std::runtime_error("Skeletal weights do not sum to one");
        math::float3 p(0,0,0),n(0,0,0),t(0,0,0),b(0,0,0);
        for (unsigned i=0;i<4;++i) {
            if (v.bones[i]>=palette.size()) throw std::runtime_error("Skeletal vertex exceeds palette");
            auto const& m=palette[v.bones[i]]; float w=v.weights[i];
            p+=m.transform_position(v.position)*w; n+=m.transform_direction(v.normal)*w;
            t+=m.transform_direction(v.tangent)*w; b+=m.transform_direction(v.binormal)*w;
        }
        vector(p); static_vertex output={{p.x,p.y,p.z},pack(n,127),pack(t,0),pack(b,0),{v.uv.x,v.uv.y}};
        for (unsigned axis=0;axis<3;++axis) { result->minimum[axis]=std::min(result->minimum[axis],output.position[axis]); result->maximum[axis]=std::max(result->maximum[axis],output.position[axis]); }
        result->vertices.push_back(output);
    }
    return result;
}
void static_model_resource::update_skeleton(math::float4x4 const* matrices,u32 count) {
    if (!owner->accepting()) throw std::runtime_error("Skeletal model session is closed");
    if (data->inverse_bind_pose.empty()) throw std::runtime_error("update_skeleton requires a skeletal model");
    validate_skeleton_pose(matrices,count);
    if (count!=data->inverse_bind_pose.size()) throw std::runtime_error("Skeleton pose count differs from bind pose");
    std::vector<math::float4x4> palette; palette.reserve(count);
    for (u32 i=0;i<count;++i) {
        auto m=data->inverse_bind_pose[i]*matrices[i];
        rigid(m);
        palette.push_back(m);
    }
    if (bone_palette && bone_palette->size()==palette.size() && std::memcmp(bone_palette->data(),palette.data(),palette.size()*sizeof(math::float4x4))==0)
        return;
    std::array<float,3> minimum,maximum; minimum.fill(std::numeric_limits<float>::max()); maximum.fill(std::numeric_limits<float>::lowest());
    for (size_t i=0;i<surfaces.size();++i) {
        if (!data->surfaces[i].skin) throw std::runtime_error("Skeletal surface has no skin data");
        auto const& skin=*data->surfaces[i].skin;
        if (skin.bone_aabbs.empty()) {
            for (unsigned axis=0;axis<3;++axis) { minimum[axis]=std::min(minimum[axis],surfaces[i].mesh->minimum[axis]); maximum[axis]=std::max(maximum[axis],surfaces[i].mesh->maximum[axis]); }
            continue;
        }
        for (size_t bi=0;bi<skin.bone_aabbs.size() && bi<palette.size();++bi) {
            auto const& bb=skin.bone_aabbs[bi];
            if (!bb.used) continue;
            auto const& m=palette[bi];
            for (unsigned corner=0;corner<8;++corner) {
                math::float3 p=m.transform_position(math::float3(
                    (corner&1)?bb.maximum[0]:bb.minimum[0],
                    (corner&2)?bb.maximum[1]:bb.minimum[1],
                    (corner&4)?bb.maximum[2]:bb.minimum[2]));
                minimum[0]=std::min(minimum[0],p.x); maximum[0]=std::max(maximum[0],p.x);
                minimum[1]=std::min(minimum[1],p.y); maximum[1]=std::max(maximum[1],p.y);
                minimum[2]=std::min(minimum[2],p.z); maximum[2]=std::max(maximum[2],p.z);
            }
        }
    }
    if (minimum[0]<=maximum[0]) { geometry_minimum=minimum; geometry_maximum=maximum; }
    bone_palette=std::make_shared<std::vector<math::float4x4> const>(std::move(palette));
    ++skin_revision;
}
} } }
