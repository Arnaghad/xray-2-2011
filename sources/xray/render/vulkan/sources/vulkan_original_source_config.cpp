#include "pch.h"
#include "vulkan_original_static_assets.h"
#include "vulkan_binary_config.h"
#include <xray/crc32.h>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <string_view>
#include <stdexcept>
extern "C" {
#include <lua.h>
#include <lauxlib.h>
#include <lualib.h>
}

namespace xray { namespace render { namespace vulkan { namespace original {
value const& value::field(pcstr key) const {
    for(auto const& child:children)if(child.name==key)return child;
    throw std::runtime_error(std::string("Missing original config field: ")+key);
}
value& value::field(pcstr key) {return const_cast<value&>(std::as_const(*this).field(key));}
namespace {
struct budget{size_t used=0;};
void* allocate(void* user,void* pointer,size_t old_size,size_t size) {
    auto& b=*static_cast<budget*>(user);if(!pointer)old_size=0;
    if(!size){(std::free)(pointer);b.used-=old_size;return nullptr;}
    if(size>32u*1024u*1024u-b.used+old_size)return nullptr;
    auto* result=(std::realloc)(pointer,size);if(result)b.used=b.used-old_size+size;return result;
}
void timed_out(lua_State* state,lua_Debug*){luaL_error(state,"Original config instruction limit exceeded");}
char const helper[]=R"LUA(
local sources, entry = ...
local cache, loading = {}, {}
local read, resolve, flatten
resolve = function(ref)
    local module, suffix = ref:match("^modules%['([^']+)'%](.*)$")
    assert(module and suffix:match('^%.[%w_.]+$'), 'Unsupported original reference: '..ref)
    assert(not loading[module], 'Cyclic original inheritance: '..module)
    if not cache[module] then
        loading[module] = true; cache[module] = read(module); loading[module] = nil
    end
    local v = cache[module]
    for key in suffix:gmatch('[%w_]+') do v=v[key]; assert(v~=nil, 'Missing original reference: '..ref) end
    return v
end
local function property(v)
    return setmetatable(v, {__index=function(_, key)
        for _, ref in ipairs(rawget(v, '__super_tables') or {}) do
            local inherited=resolve(ref)[key]; if inherited~=nil then return inherited end
        end
    end})
end
local function vector(kind, ...) return {__vector=kind, values={...}} end
read = function(path)
    local env={create_property_table=property, create_reference=resolve,
        float2=function(...)return vector(6, ...)end,
        float3=function(...)return vector(7, ...)end,
        float4=function(...)return vector(8, ...)end}
    assert(load(assert(sources[path], 'Missing original module: '..path), '@'..path, 't', env))()
    env.create_property_table=nil; env.create_reference=nil
    env.float2=nil; env.float3=nil; env.float4=nil
    return env
end
local nodes=100000
flatten=function(v, depth)
    nodes=nodes-1; assert(nodes>=0 and depth<=64, 'Original config expansion limit')
    if type(v)~='table' then return v end
    local out={}
    for _, ref in ipairs(rawget(v,'__super_tables') or {}) do
        for k, child in pairs(flatten(resolve(ref),depth+1)) do if out[k]==nil then out[k]=child end end
    end
    for k, child in pairs(v) do if k~='__super_tables' then out[k]=flatten(child,depth+1) end end
    return out
end
loading[entry]=true
return flatten(read(entry),0)
)LUA";
value node(lua_State* state,int index,size_t& nodes,unsigned depth) {
    if(++nodes>100000 || depth>64)throw std::runtime_error("Original config expansion exceeds bounds");
    index=lua_absindex(state,index);value result;
    switch(lua_type(state,index)) {
    case LUA_TBOOLEAN:result.type=0;result.number=lua_toboolean(state,index);return result;
    case LUA_TNUMBER:
        result.number=lua_tonumber(state,index);result.type=lua_isinteger(state,index)?1:2;
        if(!std::isfinite(result.number) || (result.type==2 && !std::isfinite(float(result.number)))
            || (result.type==1 && (result.number<-2147483648. || result.number>2147483647.)))
            throw std::runtime_error("Original config number exceeds finite ABI range");
        return result;
    case LUA_TSTRING:
        result.type=5;result.text=lua_tostring(state,index);
        if(result.text.size()!=lua_rawlen(state,index) || result.text.size()>65534)throw std::runtime_error("Invalid original config string");
        return result;
    case LUA_TTABLE:break;
    default:throw std::runtime_error("Unsupported original config value");
    }
    lua_getfield(state,index,"__vector");
    if(!lua_isnil(state,-1)) {
        if(!lua_isinteger(state,-1) || lua_tointeger(state,-1)<6 || lua_tointeger(state,-1)>8)throw std::runtime_error("Invalid original vector tag");
        result.type=u16(lua_tointeger(state,-1));lua_pop(state,1);lua_getfield(state,index,"values");
        if(!lua_istable(state,-1) || lua_rawlen(state,-1)!=size_t(result.type-4))throw std::runtime_error("Invalid original vector size");
        for(unsigned i=1;i<=unsigned(result.type-4);++i) {
            lua_rawgeti(state,-1,i);auto v=lua_tonumber(state,-1);
            if(lua_type(state,-1)!=LUA_TNUMBER || !std::isfinite(v) || !std::isfinite(float(v)))throw std::runtime_error("Non-finite original vector");
            result.vector.push_back(float(v));lua_pop(state,1);
        }
        lua_pop(state,1);return result;
    }
    lua_pop(state,1);size_t length=lua_rawlen(state,index),count=0;bool named=false,indexed=false;
    lua_pushnil(state);
    while(lua_next(state,index)) {
        ++count;if(count>65535)throw std::runtime_error("Original config table exceeds ABI bounds");
        if(lua_type(state,-2)==LUA_TSTRING)named=true;
        else if(lua_isinteger(state,-2) && lua_tointeger(state,-2)>0 && size_t(lua_tointeger(state,-2))<=length)indexed=true;
        else throw std::runtime_error("Invalid original table key");
        if(named && indexed)throw std::runtime_error("Mixed original config table");
        if(named) {
            auto child=node(state,-1,nodes,depth+1);child.name=lua_tostring(state,-2);
            if(child.name.empty() || child.name.size()!=lua_rawlen(state,-2) || child.name.size()>1024)
                throw std::runtime_error("Invalid original config field name");
            result.children.push_back(std::move(child));
        }
        lua_pop(state,1);
    }
    if(indexed) {
        if(count!=length)throw std::runtime_error("Sparse original config array");
        result.type=4;
        for(size_t i=1;i<=length;++i){lua_rawgeti(state,index,i);result.children.push_back(node(state,-1,nodes,depth+1));lua_pop(state,1);}
    }
    return result;
}
u32 crc(std::string const& name){crc32_type c;c.process_block(name.data(),name.data()+name.size());return c.checksum();}
}
std::vector<std::string> source_references(const_buffer source) {
    if(!source.c_ptr() || !source.size() || source.size()>4u*1024u*1024u)throw std::runtime_error("Original config source exceeds bounds");
    std::string_view text(static_cast<pcstr>(source.c_ptr()),source.size());std::vector<std::string> result;
    size_t offset=0;
    while((offset=text.find("modules['",offset))!=std::string_view::npos) {
        auto start=offset+9,end=text.find("']",start);
        if(end==std::string_view::npos || end-start>220)throw std::runtime_error("Unbounded original module reference");
        auto path=std::string(text.substr(start,end-start));binary::validate_path(path);offset=end+2;
        if(path.size()>220)throw std::runtime_error("Original module path exceeds bounds");
        if(std::find(result.begin(),result.end(),path)==result.end())result.push_back(std::move(path));
        if(result.size()>128)throw std::runtime_error("Original module count exceeds bounds");
    }
    return result;
}
value read_source_config(files const& sources,std::string const& entry) {
    if(sources.size()>128 || !sources.contains(entry))throw std::runtime_error("Original source module set exceeds bounds");
    budget memory;
    std::unique_ptr<lua_State,void(*)(lua_State*)> state(lua_newstate(allocate,&memory,luaL_makeseed(nullptr)),lua_close);
    if(!state)throw std::runtime_error("Cannot allocate original config state");
    luaL_requiref(state.get(),LUA_GNAME,luaopen_base,1);lua_pop(state.get(),1);
    luaL_requiref(state.get(),LUA_STRLIBNAME,luaopen_string,1);lua_pop(state.get(),1);
    lua_sethook(state.get(),timed_out,LUA_MASKCOUNT,1000000);
    if(luaL_loadbufferx(state.get(),helper,sizeof(helper)-1,"original_config_reader","t")!=LUA_OK)throw std::runtime_error("Cannot load original config reader");
    lua_newtable(state.get());size_t total=0;
    for(auto const& [name,data]:sources) {
        total+=data.size();if(data.size()>4u*1024u*1024u || total>8u*1024u*1024u)throw std::runtime_error("Original source bytes exceed bounds");
        lua_pushlstring(state.get(),reinterpret_cast<pcstr>(data.data()),data.size());lua_setfield(state.get(),-2,name.c_str());
    }
    lua_pushlstring(state.get(),entry.data(),entry.size());
    if(lua_pcall(state.get(),2,1,0)!=LUA_OK) {
        auto* error=lua_tostring(state.get(),-1);
        throw std::runtime_error(std::string("Invalid original source config: ")+(error?error:"Lua error"));
    }
    size_t nodes=0;return node(state.get(),-1,nodes,0);
}
bytes binary_config(value root) {
    struct disk{u64 id,data;u32 crc;u16 type,count;};static_assert(sizeof(disk)==24);
    std::vector<disk> fat(1);bytes blob;
    auto emit=[&](auto&& self,value n,size_t offset,unsigned depth)->void {
        if(depth>64 || fat.size()>100000)throw std::runtime_error("Original binary config exceeds bounds");
        disk v={};v.type=n.type;
        if(!n.name.empty()){v.id=blob.size()+1;v.crc=crc(n.name);blob.insert(blob.end(),n.name.begin(),n.name.end());blob.push_back(0);}
        if(n.type==3 || n.type==4) {
            if(n.children.size()>65535)throw std::runtime_error("Original binary table exceeds bounds");
            if(n.type==3)std::sort(n.children.begin(),n.children.end(),[](auto const& a,auto const& b){return crc(a.name)<crc(b.name);});
            size_t first=fat.size();v.data=first*sizeof(disk);v.count=u16(n.children.size());fat.resize(first+n.children.size());
            for(size_t i=0;i<n.children.size();++i)self(self,std::move(n.children[i]),first+i,depth+1);
        } else if(n.type<=2) {
            v.count=4;if(n.type==2){float f=float(n.number);u32 bits;std::memcpy(&bits,&f,4);v.data=bits;}else v.data=u32(s32(n.number));
        } else {
            v.data=blob.size();
            if(n.type==5){blob.insert(blob.end(),n.text.begin(),n.text.end());blob.push_back(0);}
            else {auto* p=reinterpret_cast<unsigned char const*>(n.vector.data());blob.insert(blob.end(),p,p+n.vector.size()*4);}
            size_t size=blob.size()-v.data;if(size>65535)throw std::runtime_error("Original binary payload exceeds bounds");v.count=u16(size);
        }
        fat[offset]=v;
    };
    emit(emit,std::move(root),0,0);size_t size=fat.size()*sizeof(disk);
    for(auto& v:fat){if(v.id)v.id+=size-1;if(v.type>4)v.data+=size;}
    auto* p=reinterpret_cast<unsigned char const*>(fat.data());bytes result(p,p+size);result.insert(result.end(),blob.begin(),blob.end());return result;
}
} } } }
