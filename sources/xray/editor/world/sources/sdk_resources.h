#pragma once
#include <xray/editor/world/sdk_resource_bridge.h>
#include <xray/resources.h>
#include <xray/resources_fs.h>
#include <xray/resources_query_result.h>
#include <xray/resources_pinned_ptr.h>
#include <xray/render/vulkan/model_resources.h>
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/vulkan/decal_resources.h>
#include <atomic>
#include <map>
#include <memory>
#include <string>
#include <stdexcept>
#include <cstring>
#include <vector>
#include <algorithm>
#include <cmath>
#include <xray/animation/cubic_spline_skeleton_animation.h>

namespace xray::editor::hosting {
class sdk_resources {
    struct request_state {
        std::uint32_t state=0,units=0,kind=0;
        std::uint64_t bytes=0;
        resources::unmanaged_resource_ptr resource;
        resources::managed_resource_ptr raw;
        resources::mount_ptr mount;
        std::string mount_path;
        std::vector<unsigned char> model_blob;
    };
    std::map<std::uint64_t,std::shared_ptr<request_state>> requests;
    inline static std::atomic<std::uint64_t> next{0};
    bool closed=false;
    static std::string path(char const* text) {
        if(!text || !*text || std::strlen(text)>1024)throw std::runtime_error("Invalid SDK resource path");
        std::string value=text;
        if(value.front()=='/' || value.find('\\')!=std::string::npos || value.find(':')!=std::string::npos
            || value.find("..")!=std::string::npos)throw std::runtime_error("SDK resource path traverses a mount");
        return value;
    }
    static std::vector<unsigned char> describe(render::vulkan::static_model_data const& model) {
        std::vector<unsigned char> data;
        auto append=[&](void const* bytes,size_t count){if(count>64u*1024u*1024u-data.size())throw std::runtime_error("Model transfer exceeds 64 MiB");auto first=static_cast<unsigned char const*>(bytes);data.insert(data.end(),first,first+count);};
        auto number=[&](u32 value){append(&value,4);};auto string=[&](std::string const& text){number(u32(text.size()));append(text.data(),text.size());};
        number(1);number(u32(model.surfaces.size()));number(u32(model.bone_names.size()));string(model.source_path);
        append(model.properties.minimum.data(),12);append(model.properties.maximum.data(),12);
        for(auto const& surface:model.surfaces){string(surface.name);auto* material=dynamic_cast<render::vulkan::material_resource*>(surface.material.c_ptr());string(material?material->virtual_path:std::string{});
            number(u32(surface.mesh->vertices.size()));number(u32(surface.mesh->indices.size()));append(surface.mesh->vertices.data(),surface.mesh->vertices.size()*sizeof(render::vulkan::static_vertex));append(surface.mesh->indices.data(),surface.mesh->indices.size()*2);}
        for(size_t i=0;i<model.bone_names.size();++i){string(model.bone_names[i]);append(&model.inverse_bind_pose[i],64);}return data;
    }
public:
    std::shared_ptr<render::vulkan::static_model_data const> model_result(std::uint64_t id) const {
        auto it=requests.find(id);if(closed || it==requests.end() || it->second->state!=1 || (it->second->kind!=1 && it->second->kind!=4))throw std::runtime_error("Unavailable model data");
        auto* model=dynamic_cast<render::vulkan::static_model_asset_resource*>(it->second->resource.c_ptr());if(!model || !model->data)throw std::runtime_error("Resource is not a model asset");return model->data;
    }
    int execute(sdk_resource_request const* input,sdk_resource_result* output,memory::base_allocator* allocator) {
        if(closed || !input || !output || input->version!=1 || input->size!=sizeof(*input)
            || output->version!=1 || output->size!=sizeof(*output))return -1;
        std::uint64_t id=input->query;
        if(input->operation==8) {*output={1,sizeof(*output),0,1,1,0};return 0;}
        if(input->operation==0 || input->operation==3) {
            if(id || input->kind>5)return -1;
            auto name=path(input->path);
            auto state=std::make_shared<request_state>();state->kind=input->kind;
            id=++next;requests.emplace(id,state);
            try {
                if(input->operation==3) {
                    state->mount_path=name;
                    if(!input->physical || !*input->physical)throw std::runtime_error("Mount requires a physical directory");
                    std::string physical=input->physical;
                    resources::query_mount_physical(fs_new::virtual_path_string(name.c_str()),fs_new::native_path_string(physical.c_str()),
                        "SDK project resources",fs_new::watcher_enabled_false,[state](resources::mount_ptr mount){
                            if(state->state==3)return;
                            state->mount=mount;state->state=mount?1:2;
                        },allocator,resources::recursive_false);
                } else {
                    auto kind=input->kind;
                    resources::class_id_enum type=kind==5?resources::animation_class:kind==4?resources::skeleton_render_model_class:kind==1?resources::static_render_model_class:kind==2?resources::material_class:kind==3?resources::texture_class:resources::raw_data_class_no_reuse;
                    resources::query_resource(name.c_str(),type,[state,kind](resources::queries_result& result){
                        if(state->state==3)return;
                        if(!result.is_successful()){state->state=2;return;}
                        if(kind==5) {
                            state->raw=result[0].get_managed_resource();
                            animation::cubic_spline_skeleton_animation_pinned clip(state->raw);
                            if(!clip.c_ptr() || !clip->bones_count() || clip->bones_count()>80){state->raw=nullptr;state->state=2;return;}
                            state->units=clip->bones_count();state->bytes=1;
                        }else if(kind==0) {
                            state->raw=result[0].get_managed_resource();
                            resources::pinned_ptr_const<u8> bytes(state->raw);state->bytes=bytes.size();state->units=1;
                        } else {
                            state->resource=result[0].get_unmanaged_resource();state->units=1;
                            if(kind==1 || kind==4) {
                                auto* model=dynamic_cast<render::vulkan::static_model_asset_resource*>(state->resource.c_ptr());
                                if(!model || !model->data || model->data->surfaces.empty()){state->resource=nullptr;state->state=2;return;}
                                state->units=std::uint32_t(model->data->surfaces.size());
                                for(auto const& surface:model->data->surfaces)
                                    state->bytes+=surface.mesh->vertices.size()*sizeof(render::vulkan::static_vertex)+surface.mesh->indices.size()*sizeof(u16);
                            }else if(kind==3) {
                                auto* texture=dynamic_cast<render::vulkan::texture_resource*>(state->resource.c_ptr());
                                auto data=texture?texture->snapshot():nullptr;
                                if(!data || data->mips.empty()){state->resource=nullptr;state->state=2;return;}
                                state->units=std::uint32_t(data->mips.size());for(auto const& mip:data->mips)state->bytes+=mip.size();
                            }
                        }
                        state->state=1;
                    },allocator,nullptr,nullptr,assert_on_fail_false);
                }
            }catch(...){requests.erase(id);throw;}
        } else if(input->operation==10) {
            auto model=model_result(id);auto const& physics=model->physics;std::vector<unsigned char> bytes;
            auto add=[&](void const* p,size_t n){auto* b=static_cast<unsigned char const*>(p);bytes.insert(bytes.end(),b,b+n);};
            u32 header[]{2,u32(physics.primitives.size())};add(header,8);for(float value:{physics.mass,physics.friction,physics.restitution,physics.linear_damping,physics.angular_damping})add(&value,4);add(&physics.mass_center,12);
            for(float value:{physics.linear_sleeping_threshold,physics.angular_sleeping_threshold})add(&value,4);u32 damping=physics.additional_damping;add(&damping,4);for(float value:{physics.additional_damping_factor,physics.additional_linear_threshold,physics.additional_angular_threshold,physics.additional_angular_factor})add(&value,4);
            for(auto const& p:physics.primitives){add(&p.type,4);add(&p.position,12);add(&p.rotation,12);add(&p.scale,12);}
            auto* buffer=reinterpret_cast<sdk_byte_buffer*>(const_cast<char*>(input->physical));if(!buffer || buffer->version!=1 || buffer->size!=sizeof(*buffer))return -1;buffer->used=u32(bytes.size());if(buffer->capacity){if(!buffer->data || buffer->capacity<bytes.size())return -1;std::memcpy(buffer->data,bytes.data(),bytes.size());}
        } else if(input->operation==9) {
            auto model=model_result(id);auto* data=reinterpret_cast<sdk_model_physics const*>(input->physical);
            if(!data || data->version!=1 || data->size!=sizeof(*data) || data->reserved || data->reserved2 || data->additional_damping>1 || data->count>128 || (data->count && !data->primitives))return -1;
            auto prepared=std::make_shared<render::vulkan::static_model_data>(*model);auto& physics=prepared->physics;physics={};physics.mass=data->mass;physics.friction=data->friction;physics.restitution=data->restitution;physics.linear_damping=data->linear_damping;physics.angular_damping=data->angular_damping;std::memcpy(&physics.mass_center,data->mass_center,12);
            physics.linear_sleeping_threshold=data->linear_sleeping;physics.angular_sleeping_threshold=data->angular_sleeping;physics.additional_damping=data->additional_damping!=0;physics.additional_damping_factor=data->additional_factor;physics.additional_linear_threshold=data->additional_linear_threshold;physics.additional_angular_threshold=data->additional_angular_threshold;physics.additional_angular_factor=data->additional_angular_factor;
            for(unsigned i=0;i<data->count;++i){auto const& p=data->primitives[i];if(p.reserved)return -1;xray::physics::model_collision_primitive primitive;primitive.type=p.type;std::memcpy(&primitive.position,p.position,12);std::memcpy(&primitive.rotation,p.rotation,12);std::memcpy(&primitive.scale,p.scale,12);physics.primitives.push_back(primitive);}physics.validate();
            auto state=std::make_shared<request_state>();state->kind=requests.at(id)->kind;id=++next;requests.emplace(id,state);resources::user_data_variant user;user.set(render::vulkan::authored_model_request{prepared});
            try{resources::query_create_resource("sdk_model_physics",const_buffer("",1),resources::static_render_model_class,[state,prepared](resources::queries_result& result){if(state->state==3)return;if(!result.is_successful()){state->state=2;return;}state->resource=result[0].get_unmanaged_resource();state->units=u32(prepared->surfaces.size());state->state=1;},allocator,&user,nullptr,assert_on_fail_false);}catch(...){requests.erase(id);throw;}
        } else if(input->operation==6) {
            auto it=requests.find(id);if(it==requests.end() || it->second->state!=1 || it->second->kind!=5)return -1;
            auto* sample=reinterpret_cast<sdk_animation_sample*>(const_cast<char*>(input->physical));
            if(!sample || sample->version!=1 || sample->size!=sizeof(*sample) || sample->reserved || !std::isfinite(sample->seconds) || sample->seconds<0 || sample->output.version!=1 || sample->output.size!=sizeof(sdk_byte_buffer))return -1;
            animation::cubic_spline_skeleton_animation_pinned clip(it->second->raw);
            std::vector<unsigned char> data;auto append=[&](void const* p,size_t n){auto b=static_cast<unsigned char const*>(p);data.insert(data.end(),b,b+n);};
            u32 header[]{1,clip->bones_count()};append(header,sizeof(header));
            float duration=clip->length_in_frames()/30.f;append(&duration,4);
            for(u32 bone=0;bone<clip->bones_count();++bone) {
                pcstr name=clip->get_bone_names().bone_name(bone);u32 length=u32(std::strlen(name));append(&length,4);append(name,length);
                animation::current_frame_position cursor;auto frame=clip->bone(bone).bone_frame(clip->min_time_in_frames()+std::min(sample->seconds,duration)*30.f,cursor);
                for(float value:frame.channels)if(!std::isfinite(value))return -1;
                append(frame.channels,sizeof(frame.channels));
            }
            sample->output.used=u32(data.size());if(sample->output.capacity){if(!sample->output.data || sample->output.capacity<data.size())return -1;std::memcpy(sample->output.data,data.data(),data.size());}
        } else if(input->operation==4) {
            auto model=model_result(id);auto& state=*requests.at(id);if(state.model_blob.empty())state.model_blob=describe(*model);
            auto* buffer=reinterpret_cast<sdk_byte_buffer*>(const_cast<char*>(input->physical));if(!buffer || buffer->version!=1 || buffer->size!=sizeof(*buffer))return -1;
            buffer->used=u32(state.model_blob.size());if(buffer->capacity){if(!buffer->data || buffer->capacity<buffer->used)return -1;std::memcpy(buffer->data,state.model_blob.data(),buffer->used);}
        } else if(input->operation==5) {
            auto model=model_result(id);auto* input_model=reinterpret_cast<sdk_model_build const*>(input->physical);
            if(!input_model || input_model->version!=1 || input_model->size!=sizeof(*input_model) || input_model->reserved || !input_model->identity || std::strlen(input_model->identity)>1024 || input_model->count!=model->surfaces.size() || !input_model->surfaces)return -1;
            auto data=std::make_shared<render::vulkan::static_model_data>(*model);data->identity=input_model->identity;
            if(!data->identity.starts_with("resources/models/__sdk6_preview/") || !data->identity.ends_with("/render"))return -1;
            std::map<std::string,resources::unmanaged_resource_ptr> materials;
            for(unsigned i=0;i<input_model->count;++i){auto const& b=input_model->surfaces[i];if(!b.surface || std::strlen(b.surface)>1024 || materials.contains(b.surface))return -1;materials.emplace(b.surface,b.material?material_result(b.material):nullptr);}
            for(auto& surface:data->surfaces){auto found=materials.find(surface.name);if(found==materials.end())return -1;surface.material=found->second;}
            auto state=std::make_shared<request_state>();state->kind=requests.at(id)->kind;id=++next;requests.emplace(id,state);
            resources::user_data_variant user;user.set(render::vulkan::authored_model_request{data});
            resources::query_create_resource(data->identity.c_str(),const_buffer("",1),resources::static_render_model_class,[state,data](resources::queries_result& result){
                if(state->state==3)return;if(!result.is_successful()){state->state=2;return;}state->resource=result[0].get_unmanaged_resource();state->units=u32(data->surfaces.size());state->state=1;
            },allocator,&user,nullptr,assert_on_fail_false);
        } else if(input->operation==2) {
            auto found=requests.find(id);if(found==requests.end())return -1;
            found->second->state=3;requests.erase(found);
            *output={1,sizeof(*output),id,3,0,0};return 0;
        } else if(input->operation==1) resources::dispatch_callbacks();
        else return -1;
        auto found=requests.find(id);if(found==requests.end())return -1;
        auto const& state=*found->second;
        *output={1,sizeof(*output),id,state.state,state.units,state.bytes};return 0;
    }
    resources::unmanaged_resource_ptr material_result(std::uint64_t id) const {
        auto it=requests.find(id);if(closed || it==requests.end() || it->second->state!=1 || it->second->kind!=2)throw std::runtime_error("Unavailable prepared material");
        auto resource=it->second->resource;if(!dynamic_cast<render::vulkan::material_resource*>(resource.c_ptr()) && !dynamic_cast<render::vulkan::decal_material_resource*>(resource.c_ptr()))throw std::runtime_error("Prepared material requires the static surface profile");return resource;
    }
    void close() {
        closed=true;
        std::vector<std::shared_ptr<request_state>> mounts;
        for(auto& pair:requests) {
            auto& state=*pair.second;state.state=3;state.resource=nullptr;state.raw=nullptr;
            if(state.mount)mounts.push_back(pair.second);
        }
        std::sort(mounts.begin(),mounts.end(),[](auto const& a,auto const& b){return a->mount_path.size()>b->mount_path.size();});
        for(auto& state:mounts)state->mount=nullptr;
        requests.clear();
    }
};
}
