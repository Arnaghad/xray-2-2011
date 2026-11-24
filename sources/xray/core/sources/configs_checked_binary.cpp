#include "pch.h"
#include <xray/configs_checked_binary.h>
#include <xray/configs.h>
#include <xray/core/sources/configs_binary_config.h>
#include <xray/crc32.h>
#include <cmath>
#include <cstring>
#include <set>
#include <string_view>
#include <stdexcept>
#include <vector>

namespace xray { namespace configs { namespace checked {
namespace {
// Windows binary_config uses 64-bit relative pointers, followed by a data blob.
struct disk_value { u64 id,data; u32 crc; u16 type,count; };
static_assert(sizeof(disk_value)==sizeof(configs::binary_config_value));
static_assert(offsetof(configs::binary_config_value,data)==8 && offsetof(configs::binary_config_value,type)==20);
class config_preflight {
    std::span<std::byte const> bytes;
    std::set<size_t> visited;
    std::vector<disk_value> nodes;
    void range(u64 offset,size_t count) const {
        if (offset>bytes.size() || count>bytes.size()-size_t(offset)) throw std::runtime_error("Truncated binary config reference");
    }
    template <typename T>
    T read_pod(u64 offset) const {
        range(offset,sizeof(T));
        T out{};
        auto slice=bytes.subspan(size_t(offset),sizeof(T));
        std::memcpy(&out,slice.data(),sizeof(T));
        return out;
    }
    void visit(u64 offset,unsigned depth) {
        if (offset%sizeof(disk_value) || depth>32 || nodes.size()>=4096 || !visited.insert(size_t(offset)).second)
            throw std::runtime_error("Cyclic, shared, unaligned or oversized binary config tree");
        disk_value value=read_pod<disk_value>(offset);
        nodes.push_back(value);
        if (value.type==configs::t_table_named || value.type==configs::t_table_indexed) {
            range(value.data,size_t(value.count)*sizeof(disk_value));
            u32 previous=0; std::set<std::string_view> names;
            for (size_t i=0;i<value.count;++i) {
                auto child_offset=value.data+i*sizeof(disk_value);
                disk_value child=read_pod<disk_value>(child_offset);
                if (value.type==configs::t_table_named) {
                    auto name=string(child.id);
                    crc32_type crc; crc.process_block(name,name+std::strlen(name));
                    if (!child.id || child.crc!=crc.checksum() || (i && child.crc<previous) || !names.insert(name).second)
                        throw std::runtime_error("Invalid binary config named table keys");
                    previous=child.crc;
                } else if (child.id || child.crc) throw std::runtime_error("Invalid binary config indexed table keys");
                visit(child_offset,depth+1);
            }
        } else if (value.type==configs::t_boolean || value.type==configs::t_integer || value.type==configs::t_float) {
            if (value.count!=4 || value.data>0xffffffffu || (value.type==configs::t_boolean && value.data>1))
                throw std::runtime_error("Invalid binary config scalar");
        } else {
            size_t count=0;
            switch (value.type) {
            case configs::t_string: count=value.count; break;
            case configs::t_float2: case configs::t_int2: count=8; break;
            case configs::t_float3: count=12; break;
            case configs::t_float4: count=16; break;
            default: throw std::runtime_error("Unknown binary config value type");
            }
            range(value.data,count);
            if (!count || count!=value.count || (value.type==configs::t_string && std::strlen(string(value.data))+1!=count))
                throw std::runtime_error("Invalid binary config value size");
        }
    }
    pcstr string(u64 offset) const {
        range(offset,1);
        auto tail=bytes.subspan(size_t(offset));
        auto* text=reinterpret_cast<pcstr>(tail.data());
        if (!std::memchr(text,0,tail.size())) throw std::runtime_error("Unterminated binary config string");
        return text;
    }
public:
    explicit config_preflight(std::span<std::byte const> source) : bytes(source) {
        if (bytes.empty() || !bytes.data() || bytes.size()<sizeof(disk_value) || bytes.size()>1024*1024)
            throw std::runtime_error("Expected a bounded Windows binary config");
        visit(0,0);
        size_t fat_size=nodes.size()*sizeof(disk_value);
        if (*visited.rbegin()+sizeof(disk_value)!=fat_size || nodes[0].id || nodes[0].type!=configs::t_table_named)
            throw std::runtime_error("Invalid binary config root or node area");
        // fix_up mutates only the contiguous node area; payloads must not alias it.
        for (auto const& node : nodes) {
            if (node.id && node.id<fat_size) throw std::runtime_error("Binary config key aliases its nodes");
            if (node.type>configs::t_table_indexed && node.data<fat_size)
                throw std::runtime_error("Binary config payload aliases its nodes");
        }
    }
};
}
checked_config::checked_config(std::span<std::byte const> source,memory::base_allocator& allocator) {
    config_preflight checked(source);
    m_config=std::make_unique<core::configs::binary_config>(reinterpret_cast<pcbyte>(source.data()),u32(source.size()),&allocator);
}
checked_config::checked_config(const_buffer source,memory::base_allocator& allocator)
    : checked_config(std::span<std::byte const>(static_cast<std::byte const*>(source.c_ptr()),source.size()),allocator) {
}
checked_config::~checked_config() = default;
configs::binary_config_value const& checked_config::root() const { return m_config->get_root(); }
using value=configs::binary_config_value;
value const* optional(value const& table,pcstr key) {
    if (table.type!=configs::t_table_named) throw std::runtime_error("Expected a binary config named table");
    for (auto const& child : table) if (std::strcmp(child.key(),key)==0) return &child;
    return nullptr;
}
value const& field(value const& table,pcstr key,u16 type) {
    auto* result=optional(table,key);
    if (!result || result->type!=type) throw std::runtime_error(std::string("Missing or mistyped binary config field: ")+key);
    return *result;
}
void validate_path(std::string const& path) {
    if (path.empty() || path.size()>=fs_new::max_path_length || path.front()=='/' || path.back()=='/'
        || path.find_first_of("\\|:")!=std::string::npos) throw std::runtime_error("Invalid asset virtual path");
    for (unsigned char ch : path) if (ch<32 || ch==127) throw std::runtime_error("Control character in asset virtual path");
    for (size_t start=0;start<path.size();) {
        auto end=path.find('/',start); auto part=path.substr(start,end-start);
        if (part.empty() || part=="." || part=="..") throw std::runtime_error("Invalid asset path component");
        if (end==std::string::npos) break; start=end+1;
    }
}
std::string converted_path(std::string path) {
    validate_path(path);
    for (auto const& tree : {std::string("resources/"),std::string("mounts/")}) {
        size_t start=0;
        while ((start=path.find(tree,start))!=std::string::npos) {
            if (!start || path[start-1]=='/') { path.insert(start+tree.size()-1,".converted"); validate_path(path); return path; }
            start+=tree.size();
        }
    }
    return path;
}
} } }
