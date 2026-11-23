#include "pch.h"
#include "vulkan_original_static_assets.h"
#include "vulkan_binary_config.h"
#include <cmath>
#include <cstring>
#include <set>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan { namespace original {
namespace {
void require(bool condition,pcstr reason) { if(!condition)throw std::runtime_error(reason); }
struct reader {
    unsigned char const* data; size_t size;
    explicit reader(const_buffer source):data(static_cast<unsigned char const*>(source.c_ptr())),size(source.size()){}
    template<class T>T at(size_t offset) const {
        require(offset<=size && sizeof(T)<=size-offset,"Original archive/mesh field exceeds bounds");
        T result;std::memcpy(&result,data+offset,sizeof(T));return result;
    }
};
void name(std::string const& text) {
    binary::validate_path(text);
    require(text.find('/')==std::string::npos && text.size()<256,"Invalid original archive node name");
}
}
files read_export_archive(const_buffer source) {
    reader input(source);
    require(source.c_ptr() && source.size()>=24 && source.size()<=64u*1024u*1024u,"Original archive size exceeds bounds");
    require(!std::memcmp(input.data,"little-endian\0\0\0",16),"Unsupported original archive header");
    u32 count=input.at<u32>(16),size=input.at<u32>(20);
    require(count && count<=10000 && size>=64 && size<=input.size-24,"Invalid original archive FAT bounds");
    reader fat(const_buffer(input.data+24,size));
    std::vector<size_t> roots;
    for(size_t i=16;i+52<=size;i+=8)
        if(fat.at<u16>(i+48)==13 && !fat.at<u16>(i+50) && !fat.at<u64>(i+24) && !fat.at<u64>(i+32)
            && fat.at<u64>(i-16)>0 && fat.at<u64>(i-16)<size)roots.push_back(i);
    require(roots.size()==1,"Original archive has no unique supported root");
    auto root=roots.front();std::set<size_t> visited;files result;
    auto walk=[&](auto&& self,size_t node,size_t parent,std::string const& prefix,unsigned depth)->std::string {
        require(depth<=32 && node>=16 && node<=size && size-node>=52 && node%8==0
            && visited.size()<count && visited.insert(node).second,"Cyclic or invalid original archive graph");
        u16 flags=fat.at<u16>(node+48);u64 stored_parent=fat.at<u64>(node+32);
        require((flags==4 || flags==5 || flags==13) && (node==root ? stored_parent==0 : stored_parent==parent || stored_parent==root-16),
            "Unsupported original archive flags or parent");
        require(node+51<size,"Original archive name exceeds FAT");
        size_t end=node+51;while(end<size && end-node-51<256 && fat.data[end])++end;
        require(end<size && end-node-51<256,"Unterminated original archive name");
        std::string text(reinterpret_cast<pcstr>(fat.data+node+51),end-node-51);
        if(node==root)require(text.empty() && flags==13,"Invalid original archive root");
        else {name(text);require(flags!=13,"Nested original archive root");}
        auto path=prefix+text;
        if(flags&1) {
            size_t child=fat.at<u64>(node-16);std::set<std::string> siblings;
            while(child) {
                auto child_name=self(self,child,node-16,path.empty()?"":path+"/",depth+1);
                require(siblings.insert(child_name).second,"Duplicate original archive sibling");
                child=fat.at<u64>(child+24);
            }
        } else {
            require(node>=24,"Original archive file prefix exceeds FAT");
            u32 length=fat.at<u32>(node-24);u64 start=fat.at<u64>(node-16);
            require(start>=24u+size && start<=input.size && length<=input.size-start,"Original archive payload exceeds input");
            require(result.emplace(path,bytes(input.data+start,input.data+start+length)).second,"Duplicate original archive payload");
        }
        return text;
    };
    walk(walk,root,0,"",0);
    require(visited.size()==count,"Unreachable original archive nodes");
    return result;
}
bytes convert_static_surface(const_buffer vertices,const_buffer indices) {
    reader source(vertices),index(indices);
    require(vertices.c_ptr() && indices.c_ptr() && vertices.size()>=4 && indices.size()>=4,"Truncated original static mesh");
    u32 count=source.at<u32>(0),n=index.at<u32>(0);
    require(count && count<=65536 && vertices.size()==4u+size_t(count)*56 && n && n<=3000000 && n%3==0 && indices.size()==4u+size_t(n)*2,
        "Invalid original static mesh sizes");
    for(u32 i=0;i<n;++i)require(index.at<u16>(4+size_t(i)*2)<count,"Original mesh index exceeds vertex count");
    unsigned char const declaration[]={0,0,0,0,2,0,0,0,0,0,12,0,4,0,3,0,0,0,16,0,4,0,6,0,
        0,0,20,0,4,0,7,0,0,0,24,0,1,0,5,0,255,0,0,0,17,0,0,0};
    bytes result;
    auto append=[&](void const* data,size_t size){auto* p=static_cast<unsigned char const*>(data);result.insert(result.end(),p,p+size);};
    auto word=[&](u32 value){append(&value,4);};
    word(3);word(sizeof(declaration)+4+count*32);append(declaration,sizeof(declaration));word(count);
    auto uv_alpha=[](float uv){double scaled=double(uv)*1024;double primary=std::clamp(std::floor(scaled),-32768.,32767.);
        return static_cast<unsigned char>(std::clamp(std::floor(255.5*(scaled-primary)),0.,255.));};
    for(u32 i=0;i<count;++i) {
        size_t offset=4+size_t(i)*56;std::array<float,14> values;
        std::memcpy(values.data(),source.data+offset,56);
        for(float f:values)require(std::isfinite(f),"Non-finite original static vertex");
        append(source.data+offset,12);
        for(unsigned basis=0;basis<3;++basis) {
            auto* v=values.data()+3+basis*3;double length=std::sqrt(double(v[0])*v[0]+double(v[1])*v[1]+double(v[2])*v[2]);
            require(std::isfinite(length) && length>1e-12,"Degenerate original static basis");
            unsigned char packed[4];
            for(unsigned c=0;c<3;++c)packed[c]=static_cast<unsigned char>(std::clamp(std::floor((v[2-c]/length+1)*127.5),0.,255.));
            packed[3]=basis==0?127:uv_alpha(values[11+basis]);append(packed,4);
        }
        append(source.data+offset+48,8);
    }
    word(4);word(static_cast<u32>(indices.size()));append(indices.c_ptr(),indices.size());
    // Validate through the same native cooked-geometry reader used by existing models.
    load_static_mesh(const_buffer(result.data(),result.size()));
    return result;
}
} } } }
