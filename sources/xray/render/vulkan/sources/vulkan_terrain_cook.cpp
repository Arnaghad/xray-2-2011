#include "pch.h"
#include "vulkan_terrain_cook.h"
#include "vulkan_binary_config.h"
#include "vulkan_diagnostics.h"
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/facade/terrain_base.h>
#include <xray/render/facade/terrain_collision.h>
#include <xray/render/core/memory.h>
#include <xray/resources_query_result.h>
#include <xray/resources_pinned_ptr.h>
#include <stdexcept>
#include <cmath>
#include <cstring>

namespace xray { namespace render { namespace vulkan {
namespace {
class terrain_game_resource final : public terrain_model_instance {
public:
    std::shared_ptr<scene_registry> const owner;
    terrain_game_resource(std::shared_ptr<scene_registry> state,render_model_instance_ptr model,
        terrain_collision_access& dependency,math::float4x4 const& transform) : owner(std::move(state)) {
        m_render_model=std::move(model);m_bt_collision_shape=dependency.physics_shape();
        m_collision_geom=dependency.geometry_instance();m_collision_center=dependency.center_offset();m_transform=transform;
    }
};
void failed(resources::query_result_for_cook& query,std::exception const& e) {
    diagnostic("error","Vulkan terrain query failed: %s",e.what()); query.finish_query(resources::cook_base::result_error);
}
struct request_context {
    scene_ptr scene;
    std::string project;
    resources::user_data_variant user;
    terrain_memory_profile profile=terrain_memory_profile::standard_256_mib;
};
std::shared_ptr<request_context> context(resources::query_result_for_cook& query,std::shared_ptr<scene_registry> const& owner) {
    auto result=std::make_shared<request_context>();
    if (query.user_data()) {
        auto* input=query.user_data()->get_if<terrain_cook_user_data>();
        auto* scene=input ? dynamic_cast<scene_resource*>(input->scene.c_ptr()) : nullptr;
        if (!scene || scene->owner!=owner || !owner->accepting()) throw std::runtime_error("Terrain user data requires a live scene from this session");
        result->scene=input->scene;
        result->profile=input->memory_profile;
        if (terrain_limits(result->profile).cell_bytes>terrain_limits(scene->terrain_memory).cell_bytes)
            throw std::runtime_error("Terrain cell memory profile exceeds its scene profile");
        if (input->current_project_resource_path) {
            // Copy the caller's project string before scheduling child queries.
            auto* name=input->current_project_resource_path; size_t length=0;
            while (length<fs_new::max_path_length && name[length]) ++length;
            if (!length || length==fs_new::max_path_length) throw std::runtime_error("Invalid terrain project path length");
            result->project.assign(name,length); binary::validate_path(result->project);
        }
        terrain_cook_user_data copy={result->scene,result->project.empty() ? nullptr : result->project.c_str(),result->profile};
        result->user.set(copy);
    }
    return result;
}
}
terrain_instance_resource::terrain_instance_resource(std::shared_ptr<scene_registry> state,resources::unmanaged_resource_ptr mesh)
    : owner(std::move(state)),asset(std::move(mesh)) {
    auto* resource=dynamic_cast<terrain_mesh_resource*>(asset.c_ptr());
    if (!owner || !owner->accepting() || !resource || resource->owner!=owner || !resource->data || !resource->data->cell)
        throw std::runtime_error("Terrain instance requires a live mesh from this session");
    m_cell=resource->data->cell;
    m_layers=resource->data->layers;
    for (auto const& layer : resource->data->layers) {
        for (auto const& source : {layer.diffuse,layer.normal,layer.specular}) {
            auto* texture=dynamic_cast<texture_resource*>(source.c_ptr());
            if (!texture || texture->owner!=owner) throw std::runtime_error("Foreign terrain texture resource");
            m_texture_states.push_back(texture->state);
        }
    }
    auto images=owner->terrain_texture_snapshots(m_texture_states);
    if (images.empty() || images.size()!=m_texture_states.size()) throw std::runtime_error("Initial terrain dependencies have a pending or failed reload");
    auto textures=std::make_shared<terrain_texture_set>();
    textures->memory_profile=resource->data->memory_profile;
    for (size_t i=0;i<images.size();++i) (i%3==0 ? textures->diffuse : i%3==1 ? textures->normal : textures->specular).push_back(images[i]);
    m_textures=normalize_terrain_textures(*textures);
    m_texture_sources=std::move(textures);
}
terrain_instance_resource::~terrain_instance_resource()=default;
void terrain_instance_resource::update_cell(std::span<terrain_cell_fragment const> fragments) {
    if (!owner->accepting()) throw std::runtime_error("Terrain instance session is closed");
    auto next=update_terrain_cell(m_cell,fragments);
    if (m_game_geometry_locked && next!=m_cell) throw std::runtime_error("Cooked game terrain buffers are immutable; collision recook is required");
    m_cell=std::move(next);
}
void terrain_instance_resource::update_transform(math::float4x4 const& transform) {
    if (!owner->accepting()) throw std::runtime_error("Terrain instance session is closed");
    if (m_game_geometry_locked) throw std::runtime_error("Cooked game terrain placement is immutable; collision recook is required");
    for (auto const& row : transform.elements) for (float v : row)
        if (!std::isfinite(v) || std::abs(v)>1e6f) throw std::runtime_error("Terrain value exceeds finite bounds");
    if (transform.elements[0][3]!=0 || transform.elements[1][3]!=0 || transform.elements[2][3]!=0 || transform.elements[3][3]!=1)
        throw std::runtime_error("Terrain transform must be affine");
    double determinant=double(transform.i.x)*(double(transform.j.y)*transform.k.z-double(transform.j.z)*transform.k.y)
        -double(transform.i.y)*(double(transform.j.x)*transform.k.z-double(transform.j.z)*transform.k.x)
        +double(transform.i.z)*(double(transform.j.x)*transform.k.y-double(transform.j.y)*transform.k.x);
    if (determinant<1e-8) throw std::runtime_error("Singular/reflected terrain transform is unsupported");
    if (std::memcmp(&m_cell->transform,&transform,sizeof(math::float4x4))==0) return;
    auto next=std::make_shared<terrain_cell_data>(*m_cell);
    next->transform=transform;
    m_cell=std::move(next);
}
void terrain_instance_resource::seal_game_geometry() {
    if (!owner->accepting()) throw std::runtime_error("Terrain instance session is closed");
    auto const& transform=m_cell->transform;
    for (unsigned r=0;r<3;++r) for (unsigned c=0;c<3;++c)
        if (transform.elements[r][c]!=(r==c ? 1.f : 0.f))
            throw std::runtime_error("Terrain game collision currently requires translation-only placement");
    m_game_geometry_locked=true;
}
std::shared_ptr<terrain_binding_update const> terrain_instance_resource::prepare_texture_bindings(std::vector<terrain_layer_resources> layers) const {
    if (!owner->accepting()) throw std::runtime_error("Terrain instance session is closed");
    if (layers.empty() || layers.size()>16) throw std::runtime_error("Terrain bindings require 1..16 slots");
    for (auto const& layer : layers) if (layer.name.empty() && (layer.diffuse || layer.normal || layer.specular))
        throw std::runtime_error("Empty terrain slot cannot retain texture dependencies");
    while (!layers.empty() && layers.back().name.empty()) layers.pop_back();
    std::vector<std::string> names;for (auto const& layer : layers) names.push_back(layer.name);
    auto next=std::make_shared<terrain_binding_update>();next->cell=rebind_terrain_cell(m_cell,names);
    auto occupied=std::find_if(layers.begin(),layers.end(),[](auto const& layer) {return !layer.name.empty();});
    for (auto const& slot : layers) {
        // Holes are never indexed, but array uploads still require a complete compatible layer.
        auto const& layer=slot.name.empty() ? *occupied : slot;
        for (auto const& source : {layer.diffuse,layer.normal,layer.specular}) {
            auto* texture=dynamic_cast<texture_resource*>(source.c_ptr());
            if (!texture || texture->owner!=owner) throw std::runtime_error("Terrain binding requires all three textures from this session");
            next->states.push_back(texture->state);
        }
    }
    auto images=owner->terrain_texture_snapshots(next->states);
    if (images.empty()) throw std::runtime_error("Terrain binding dependencies have a pending or failed reload");
    auto textures=std::make_shared<terrain_texture_set>();
    textures->memory_profile=m_texture_sources->memory_profile;
    for (size_t i=0;i<images.size();++i) (i%3==0 ? textures->diffuse : i%3==1 ? textures->normal : textures->specular).push_back(images[i]);
    next->sources=std::move(textures);
    next->textures=normalize_terrain_textures(*next->sources,m_texture_sources.get(),m_textures);
    bool same=layers.size()==m_layers.size();
    for (size_t i=0;same && i<layers.size();++i) {
        auto const& a=layers[i];auto const& b=m_layers[i];
        same=a.name==b.name && a.diffuse==b.diffuse && a.normal==b.normal && a.specular==b.specular;
    }
    next->source_generation=m_binding_generation;
    next->next_generation=same && next->textures==m_textures ? m_binding_generation : std::make_shared<u8>(0);
    next->previous=m_cell;next->layers=std::move(layers);return next;
}
void terrain_instance_resource::commit_texture_bindings(std::shared_ptr<terrain_binding_update const> const& next) {
    terrain_binding_commit entry={this,next};commit_texture_binding_batch({&entry,1});
}
void terrain_instance_resource::commit_texture_binding_batch(std::span<terrain_binding_commit const> batch) {
    if (batch.empty() || batch.size()>64) throw std::runtime_error("Terrain binding batch requires 1..64 cells");
    std::shared_ptr<scene_registry> owner;
    std::vector<std::shared_ptr<texture_state>> states;
    for (size_t i=0;i<batch.size();++i) {
        auto* instance=batch[i].instance;auto const& next=batch[i].candidate;
        if (!instance || !instance->owner->accepting()) throw std::runtime_error("Terrain instance session is closed");
        if (!owner) owner=instance->owner;
        if (instance->owner!=owner || !next || next->source_generation!=instance->m_binding_generation || next->previous!=instance->m_cell)
            throw std::runtime_error("Stale or foreign terrain binding transaction");
        for (size_t j=0;j<i;++j) if (batch[j].instance==instance) throw std::runtime_error("Duplicate terrain binding batch target");
        states.insert(states.end(),next->states.begin(),next->states.end());
    }
    // Capture the entire batch under one registry lock, not independently per cell.
    auto images=owner->terrain_texture_snapshots(states);
    if (images.size()!=states.size()) throw std::runtime_error("Terrain binding dependencies are no longer ready");
    struct publication {
        std::vector<terrain_layer_resources> layers;
        std::vector<std::shared_ptr<texture_state>> states;
    };
    std::vector<publication> ready;ready.reserve(batch.size());size_t offset=0;
    for (auto const& entry : batch) {
        auto const& next=*entry.candidate;
        for (size_t i=0;i<next.states.size();++i)
            if (images[offset+i]!=(i%3==0 ? next.sources->diffuse : i%3==1 ? next.sources->normal : next.sources->specular)[i/3])
                throw std::runtime_error("Terrain binding dependencies changed after preflight");
        offset+=next.states.size();ready.push_back({next.layers,next.states});
    }
    // No allocating or failing work after this point: all cells publish in one render-thread operation.
    for (size_t i=0;i<batch.size();++i) {
        auto& instance=*batch[i].instance;auto const& next=*batch[i].candidate;
        instance.m_layers.swap(ready[i].layers);instance.m_texture_states.swap(ready[i].states);
        instance.m_cell=next.cell;instance.m_texture_sources=next.sources;instance.m_textures=next.textures;
        if (instance.m_binding_generation!=next.next_generation) ++instance.m_binding_revision;
        instance.m_binding_generation=next.next_generation;instance.m_rejected_textures.clear();
    }
}
math::aabb terrain_instance_resource::local_bounds() const {
    auto result=math::create_zero_aabb();
    for (unsigned c=0;c<3;++c) {
        result.min[c]=m_bounds ? std::min(m_bounds->min[c],m_cell->mesh->minimum[c]) : m_cell->mesh->minimum[c];
        result.max[c]=m_bounds ? std::max(m_bounds->max[c],m_cell->mesh->maximum[c]) : m_cell->mesh->maximum[c];
    }
    return result;
}
void terrain_instance_resource::update_bounds(math::aabb const& bounds) {
    if (!owner->accepting()) throw std::runtime_error("Terrain instance session is closed");
    for (unsigned c=0;c<3;++c)
        if (!std::isfinite(bounds.min[c]) || !std::isfinite(bounds.max[c]) || bounds.min[c]>bounds.max[c]
            || std::abs(bounds.min[c])>1e6f || std::abs(bounds.max[c])>1e6f)
            throw std::runtime_error("Terrain local AABB requires finite ordered bounds within +/-1e6");
    m_bounds=bounds;
}
std::shared_ptr<terrain_texture_set const> terrain_instance_resource::textures() {
    auto images=owner->terrain_texture_snapshots(m_texture_states);
    if (images.empty()) return m_textures;
    bool same=images.size()==m_rejected_textures.size();
    for (size_t i=0;same && i<images.size();++i) same=m_rejected_textures[i].lock()==images[i];
    if (same) return m_textures;
    auto next=std::make_shared<terrain_texture_set>();
    next->memory_profile=m_texture_sources->memory_profile;
    for (size_t i=0;i<images.size();++i) (i%3==0 ? next->diffuse : i%3==1 ? next->normal : next->specular).push_back(images[i]);
    if (next->diffuse==m_texture_sources->diffuse && next->normal==m_texture_sources->normal && next->specular==m_texture_sources->specular) return m_textures;
    std::shared_ptr<terrain_texture_set const> normalized;
    try { normalized=normalize_terrain_textures(*next,m_texture_sources.get(),m_textures); }
    catch (std::runtime_error const& error) {
        m_rejected_textures.assign(images.begin(),images.end());++m_texture_rejections;
        diagnostic("error","Terrain texture set reload rejected; retaining previous set: %s",error.what());return m_textures;
    }
    m_rejected_textures.clear();m_texture_sources=std::move(next);m_textures=std::move(normalized);++m_texture_commits;return m_textures;
}
terrain_cook::terrain_cook(resources::class_id_enum kind,std::shared_ptr<scene_registry> owner)
    : translate_query_cook(kind,reuse_false,use_current_thread_id),m_owner(std::move(owner)),m_allocator(render::g_allocator),m_kind(kind) {}
void terrain_cook::load_layers(resources::query_result_for_cook& query,std::shared_ptr<terrain_cell_data const> cell) {
    if (!m_owner->accepting()) throw std::runtime_error("Terrain cook session is closed");
    auto profile=context(query,m_owner)->profile;
    std::vector<std::string> names; std::vector<size_t> slots;
    for (auto const& name : cell->textures) {
        auto it=std::find(names.begin(),names.end(),name);
        if (it==names.end()) { slots.push_back(names.size()); names.push_back(name); }
        else slots.push_back(size_t(it-names.begin()));
    }
    std::vector<std::string> paths; paths.reserve(names.size()*3);
    for (auto const& name : names) for (auto suffix : {"","_nmap","_spec"}) {
        paths.push_back(name+suffix); binary::validate_path(paths.back());
    }
    std::vector<resources::request> requests;
    std::vector<resources::user_data_variant> data(paths.size());
    std::vector<resources::user_data_variant const*> users;
    for (size_t i=0;i<paths.size();++i) {
        texture_configuration config; config.srgb=i%3!=1; config.force_linear=i%3==1;
        config.generate_mips=true;config.require_rgba=true;
        data[i].set(config); users.push_back(&data[i]); requests.push_back({paths[i].c_str(),resources::texture_class});
    }
    resources::query_resources(requests.data(),u32(requests.size()),[this,&query,cell,names,slots,profile](resources::queries_result& result) {
        try {
            if (!m_owner->accepting()) throw std::runtime_error("Terrain cook session is closed");
            if (!result.is_successful()) throw std::runtime_error("Terrain diffuse/normal/specular dependency query failed");
            std::vector<terrain_layer_resources> unique; size_t total=0;
            for (size_t i=0;i<names.size();++i) {
                terrain_layer_resources layer; layer.name=names[i];
                resources::unmanaged_resource_ptr* targets[]={&layer.diffuse,&layer.normal,&layer.specular};
                for (unsigned role=0;role<3;++role) {
                    *targets[role]=result[u32(i*3+role)].get_unmanaged_resource();
                    auto* texture=dynamic_cast<texture_resource*>(targets[role]->c_ptr());
                    if (!texture || texture->owner!=m_owner) throw std::runtime_error("Foreign terrain texture resource");
                    auto snapshot=texture->snapshot();
                    if (snapshot->description.type!=rhi::texture_type::type_2d || snapshot->description.array_layers!=1
                        || snapshot->description.width!=snapshot->description.height || snapshot->description.width>4096)
                        throw std::runtime_error("Terrain layer requires square 2D textures up to 4096");
                    for (auto const& mip : snapshot->mips) {
                        auto limit=terrain_limits(profile).cell_bytes;
                        if (mip.size()>limit-total) throw std::runtime_error("Terrain decoded textures exceed "+std::to_string(limit/(1024*1024))+" MiB");
                        total+=mip.size();
                    }
                }
                unique.push_back(std::move(layer));
            }
            auto data=std::make_shared<terrain_asset_data>(); data->cell=cell;
            data->memory_profile=profile;
            for (size_t slot : slots) data->layers.push_back(unique[slot]);
            auto* resource=XRAY_NEW_IMPL(*m_allocator,terrain_mesh_resource)(m_owner,data);
            query.set_unmanaged_resource(resource,resources::nocache_memory,sizeof(*resource)); query.finish_query(result_success);
        } catch (std::exception const& e) { failed(query,e); }
    },m_allocator,users.data(),&query,assert_on_fail_false);
}
void terrain_cook::load_file(resources::query_result_for_cook& query,std::string const& path) {
    resources::query_resource(path.c_str(),resources::raw_data_class,[this,&query](resources::queries_result& result) {
        try {
            if (!m_owner->accepting()) throw std::runtime_error("Terrain cook session is closed");
            if (!result.is_successful()) { query.finish_query(result_error); return; }
            resources::pinned_ptr_const<u8> bytes(result[0].get_managed_resource()); load_layers(query,load_terrain_cell(bytes));
        } catch (std::exception const& e) { failed(query,e); }
    },m_allocator,nullptr,&query,assert_on_fail_false);
}
void terrain_cook::translate_query(resources::query_result_for_cook& query) {
    try {
        if (!m_owner->accepting()) throw std::runtime_error("Terrain cook session is closed");
        if (m_kind==resources::terrain_model_class && !resources::cook_base::find_cook(resources::terrain_collision_shape_class))
            throw std::runtime_error("Terrain game wrapper requires the engine collision cook");
        auto request=context(query,m_owner); std::string path=query.get_requested_path(); binary::validate_path(path);
        if (!path.ends_with(".terr")) throw std::runtime_error("Terrain query requires a .terr path");
        if (m_kind!=resources::terrain_mesh_class) {
            auto complete=[this,&query,request](resources::queries_result& result) {
                try {
                    if (!m_owner->accepting()) throw std::runtime_error("Terrain cook session is closed");
                    if (!result.is_successful()) { query.finish_query(result_error); return; }
                    if (m_kind==resources::terrain_model_class) { load_wrapper(query,result[0].get_unmanaged_resource());return; }
                    auto* resource=XRAY_NEW_IMPL(*m_allocator,terrain_instance_resource)(m_owner,result[0].get_unmanaged_resource());
                    query.set_unmanaged_resource(resource,resources::nocache_memory,sizeof(*resource)); query.finish_query(result_success);
                } catch (std::exception const& e) { failed(query,e); }
            };
            auto* user=query.user_data() ? &request->user : nullptr;
            auto child_kind=m_kind==resources::terrain_model_class ? resources::terrain_instance_class : resources::terrain_mesh_class;
            if (auto bytes=query.creation_data_from_user()) {
                resources::creation_request child(path.c_str(),bytes,child_kind);
                resources::user_data_variant const* data=user;
                resources::query_create_resources(&child,1,complete,m_allocator,&data,&query,assert_on_fail_false);
            } else resources::query_resource(path.c_str(),child_kind,complete,m_allocator,user,&query,assert_on_fail_false);
            return;
        }
        if (auto bytes=query.creation_data_from_user()) { load_layers(query,load_terrain_cell(bytes)); return; }
        auto inspect=[this,&query](std::string const& selected) {
            resources::query_vfs_iterator(fs_new::virtual_path_string(selected.c_str()),[this,&query,selected](vfs::vfs_locked_iterator const& it) {
                try {
                    if (!m_owner->accepting()) throw std::runtime_error("Terrain cook session is closed");
                    if (it.is_end() || it.is_folder() || !it.get_file_size() || it.get_file_size()>terrain_cell_data::byte_limit+8)
                        throw std::runtime_error("Missing, empty or oversized terrain file");
                    load_file(query,selected);
                } catch (std::exception const& e) { failed(query,e); }
            },m_allocator,resources::recursive_false,&query);
        };
        resources::query_vfs_iterator(fs_new::virtual_path_string(path.c_str()),[this,&query,request,path,inspect](vfs::vfs_locked_iterator const& it) {
            try {
                if (!m_owner->accepting()) throw std::runtime_error("Terrain cook session is closed");
                if (!it.is_end()) { inspect(path); return; }
                if (request->project.empty()) throw std::runtime_error("Terrain file missing and no project fallback supplied");
                auto name=path.substr(path.find_last_of('/')+1);
                std::string fallback="resources/projects/"+request->project+"/terrain/"+name; binary::validate_path(fallback);
                inspect(fallback);
            } catch (std::exception const& e) { failed(query,e); }
        },m_allocator,resources::recursive_false,&query);
    } catch (std::exception const& e) { failed(query,e); }
}
void terrain_cook::load_wrapper(resources::query_result_for_cook& query,resources::unmanaged_resource_ptr model) {
    auto* instance=dynamic_cast<terrain_instance_resource*>(model.c_ptr());
    if (!instance || instance->owner!=m_owner || !m_owner->accepting()) throw std::runtime_error("Invalid terrain wrapper render dependency");
    instance->seal_game_geometry();render_model_instance_ptr source=instance;
    resources::user_data_variant user;user.set(source);resources::user_data_variant const* data=&user;
    resources::creation_request child(query.get_requested_path(),const_buffer("",1),resources::terrain_collision_shape_class);
    resources::query_create_resources(&child,1,[this,&query,source](resources::queries_result& result) {
        try {
            if (!m_owner->accepting()) throw std::runtime_error("Terrain cook session is closed");
            if (!result.is_successful()) { query.finish_query(result_error);return; }
            auto dependency=result[0].get_unmanaged_resource();
            auto* access=dynamic_cast<terrain_collision_access*>(dependency.c_ptr());
            if (!access || access->source_instance()!=source.c_ptr() || !access->geometry_instance() || !access->physics_shape()
                || static_cast<resources::unmanaged_resource*>(access->physics_shape())!=dependency.c_ptr()
                || !access->physics_shape()->get_bt_shape() || access->geometry_instance()->delete_by_collision_object())
                throw std::runtime_error("Invalid terrain wrapper collision/physics dependency");
            auto* instance=static_cast<terrain_instance_resource*>(source.c_ptr());
            auto* resource=XRAY_NEW_IMPL(*m_allocator,terrain_game_resource)(m_owner,source,*access,instance->cell()->transform);
            query.set_unmanaged_resource(resource,resources::nocache_memory,sizeof(*resource));query.finish_query(result_success);
        } catch (std::exception const& error) { failed(query,error); }
    },m_allocator,&data,&query,assert_on_fail_false);
}
void terrain_cook::delete_resource(resources::resource_base* resource) {
    if (m_kind==resources::terrain_mesh_class) { auto* value=static_cast<terrain_mesh_resource*>(resource); XRAY_DELETE_IMPL(*m_allocator,value); }
    else if (m_kind==resources::terrain_model_class) { auto* value=static_cast<terrain_game_resource*>(resource);XRAY_DELETE_IMPL(*m_allocator,value); }
    else { auto* value=static_cast<terrain_instance_resource*>(resource); XRAY_DELETE_IMPL(*m_allocator,value); }
}
} } }
