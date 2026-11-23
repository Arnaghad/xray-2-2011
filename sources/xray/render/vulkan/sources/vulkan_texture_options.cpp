#include "pch.h"
#include <xray/render/vulkan/material_resources.h>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <stdexcept>
extern "C" {
#include <lua.h>
#include <lauxlib.h>
}

namespace xray { namespace render { namespace vulkan {
namespace {
struct budget { size_t used=0; };
void* allocate(void* user,void* pointer,size_t old_size,size_t size) {
    auto& state=*static_cast<budget*>(user); if (!pointer) old_size=0;
    if (!size) { (std::free)(pointer); state.used-=old_size; return nullptr; }
    if (size>8u*1024u*1024u-state.used+old_size) return nullptr;
    void* result=(std::realloc)(pointer,size);
    if (result) state.used=state.used-old_size+size;
    return result;
}
void timed_out(lua_State* state,lua_Debug*) { luaL_error(state,"Texture options instruction limit exceeded"); }
struct context { const_buffer bytes; texture_configuration config; };
bool flag(lua_State* state,pcstr name) {
    lua_getfield(state,-1,name);
    bool result=false;
    if (lua_isboolean(state,-1)) result=lua_toboolean(state,-1)!=0;
    else if (lua_type(state,-1)==LUA_TNUMBER && (lua_tonumber(state,-1)==0 || lua_tonumber(state,-1)==1)) result=lua_tonumber(state,-1)==1;
    else luaL_error(state,"Texture option %s must be boolean or 0/1",name);
    lua_pop(state,1); return result;
}
int read_options(lua_State* state) {
    auto* data=static_cast<context*>(lua_touserdata(state,1));
    if (luaL_loadbufferx(state,static_cast<pcstr>(data->bytes.c_ptr()),data->bytes.size(),"texture.options","t")!=LUA_OK) return lua_error(state);
    lua_call(state,0,0);
    lua_getglobal(state,"options"); if (!lua_istable(state,-1)) return luaL_error(state,"Missing texture options table");
    data->config.srgb=flag(state,"srgb"); data->config.generate_mips=flag(state,"has_mip_levels");
    lua_getfield(state,-1,"texture_dimension");
    if (lua_type(state,-1)!=LUA_TNUMBER) return luaL_error(state,"Expected numeric source texture dimension");
    double dimension=lua_tonumber(state,-1);
    if (dimension!=1 && dimension!=2 && dimension!=3 && dimension!=4)
        return luaL_error(state,"Expected 2D, volume, cubemap or array source texture dimension");
    data->config.source_dimension=static_cast<unsigned>(dimension);
    data->config.source_cube=data->config.source_dimension==3;
    lua_pop(state,1); lua_pushnil(state);
    while (lua_next(state,-2)) {
        if (lua_type(state,-2)!=LUA_TSTRING) return luaL_error(state,"Invalid texture option key");
        pcstr key=lua_tostring(state,-2);
        auto is_flag_value=[state]() {
            return lua_isboolean(state,-1) || (lua_type(state,-1)==LUA_TNUMBER
                && (lua_tonumber(state,-1)==0 || lua_tonumber(state,-1)==1));
        };
        if (!std::strcmp(key,"format") || !std::strcmp(key,"ver")) {
            if (lua_type(state,-1)!=LUA_TSTRING || lua_rawlen(state,-1)>64) return luaL_error(state,"Invalid texture metadata");
            if (!std::strcmp(key,"format")) {
                pcstr format=lua_tostring(state,-1);data->config.pack_tga_normal=!std::strcmp(format,"NORMAL_MAP");
                if(!std::strcmp(format,"UNCOMPRESSED"))data->config.source_compression=0;
                else if(!std::strcmp(format,"DXT1"))data->config.source_compression=1;
                else if(!std::strcmp(format,"DXT1a"))data->config.source_compression=5;
                else if(!std::strcmp(format,"A8"))data->config.source_compression=4;
                else if(!std::strcmp(format,"DXT3"))data->config.source_compression=2;
                else if(!std::strcmp(format,"DXT5") || !std::strcmp(format,"NORMAL_MAP"))data->config.source_compression=3;
                else return luaL_error(state,"Unsupported texture format %s",format);
            }
        } else if (!std::strcmp(key,"high_quality")) {
            if (!is_flag_value()) return luaL_error(state,"Invalid texture compression quality flag");
            data->config.high_quality=lua_isboolean(state,-1)?lua_toboolean(state,-1)!=0:lua_tonumber(state,-1)==1;
        } else if (!std::strcmp(key,"bump_mode")) {
            // Obsolete editor default, ignored by texture_converter_cook; not material parallax.
            if (lua_type(state,-1)!=LUA_TNUMBER || lua_tonumber(state,-1)!=0) return luaL_error(state,"Unsupported non-default texture bump_mode");
        } else if (!std::strcmp(key,"width") || !std::strcmp(key,"height") || !std::strcmp(key,"detail_scale") || !std::strcmp(key,"tile")) {
            if (lua_type(state,-1)!=LUA_TNUMBER || !std::isfinite(lua_tonumber(state,-1)) || lua_tonumber(state,-1)<0)
                return luaL_error(state,"Invalid legacy texture numeric option %s",key);
        } else if (!std::strcmp(key,"alpha_blend_mode") || !std::strcmp(key,"pack_mode")) {
            bool valid=(lua_type(state,-1)==LUA_TSTRING && lua_rawlen(state,-1)<=64)
                || (lua_type(state,-1)==LUA_TNUMBER && std::isfinite(lua_tonumber(state,-1)));
            if (!valid) return luaL_error(state,"Invalid legacy texture mode option %s",key);
        } else if (!std::strcmp(key,"normal_name") || !std::strcmp(key,"color_name") || !std::strcmp(key,"detail_name") || !std::strcmp(key,"bump_name")) {
            if (lua_type(state,-1)!=LUA_TSTRING || lua_rawlen(state,-1)>260)
                return luaL_error(state,"Invalid legacy texture reference option %s",key);
        } else if (!std::strcmp(key,"use_autogenerated_names")) {
            if (!is_flag_value()) return luaL_error(state,"Invalid legacy texture autogenerated flag");
        } else if (std::strcmp(key,"srgb") && std::strcmp(key,"has_mip_levels") && std::strcmp(key,"texture_dimension"))
            return luaL_error(state,"Unsupported texture option %s",key);
        lua_pop(state,1);
    }
    if (data->config.source_dimension!=1 && data->config.pack_tga_normal) return luaL_error(state,"Multi-layer/volume NORMAL_MAP packing is unsupported");
    return 0;
}
}
texture_configuration load_texture_options(const_buffer source,texture_configuration defaults) {
    if (!source.c_ptr() || !source.size() || source.size()>65536) throw std::runtime_error("Texture options exceed bounds or are empty");
    budget memory;
    std::unique_ptr<lua_State,void(*)(lua_State*)> state(lua_newstate(allocate,&memory,luaL_makeseed(nullptr)),lua_close);
    if (!state) throw std::runtime_error("Cannot allocate texture options Lua state");
    // No standard libraries or host callbacks are opened for source configs.
    lua_sethook(state.get(),timed_out,LUA_MASKCOUNT,100000);
    context data{source,defaults};
    lua_pushcfunction(state.get(),read_options); lua_pushlightuserdata(state.get(),&data);
    if (lua_pcall(state.get(),1,0,0)!=LUA_OK) {
        pcstr error=lua_tostring(state.get(),-1);
        throw std::runtime_error(std::string("Invalid texture options: ")+(error ? error : "Lua error"));
    }
    return data.config;
}
} } }
