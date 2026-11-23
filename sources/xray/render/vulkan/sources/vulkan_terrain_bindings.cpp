#include "pch.h"
#include "vulkan_terrain_bindings.h"
#include "vulkan_diagnostics.h"
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/core/memory.h>
#include <xray/resources_query_result.h>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
void scene_registry::begin_terrain_command(size_t bytes) {
    std::lock_guard lock(m_mutex);++m_terrain_bindings.requested;++m_terrain_bindings.pending;m_terrain_bindings.queued_bytes+=bytes;
}
void scene_registry::end_terrain_command(unsigned result,size_t bytes) {
    std::lock_guard lock(m_mutex);--m_terrain_bindings.pending;m_terrain_bindings.queued_bytes-=bytes;
    ++(result==0 ? m_terrain_bindings.committed : result==1 ? m_terrain_bindings.failed : m_terrain_bindings.cancelled);
}
void scene_registry::begin_terrain_query() { std::lock_guard lock(m_mutex);++m_terrain_bindings.queries;++m_terrain_bindings.in_flight; }
void scene_registry::end_terrain_query() { std::lock_guard lock(m_mutex);--m_terrain_bindings.in_flight; }
terrain_binding_statistics scene_registry::terrain_binding_status() const {std::lock_guard lock(m_mutex);return m_terrain_bindings;}
struct terrain_binding_runtime::command {
    enum class kind { add,remove,exchange,action } type;
    scene_ptr scene;
    render_model_instance_ptr model;
    std::string old_name,name;
    u32 slot=0;
    size_t bytes=0;
    bool loading=false;
    std::function<void()> action;
    struct target {
        render_model_instance_ptr model;
        std::shared_ptr<terrain_cell_data const> cell;
        u64 revision;
        std::vector<terrain_layer_resources> layers;
    };
    std::vector<target> targets;
};
namespace {
terrain_instance_resource& member(std::shared_ptr<scene_registry> const& owner,scene_ptr const& scene,render_model_instance_ptr const& model) {
    auto* destination=dynamic_cast<scene_resource*>(scene.c_ptr());auto* instance=dynamic_cast<terrain_instance_resource*>(model.c_ptr());
    if (!owner->accepting() || !destination || destination->owner!=owner || !destination->terrain_enabled
        || !instance || instance->owner!=owner) throw std::runtime_error("Terrain binding requires a live scene/cell from this session");
    auto const& items=destination->models->terrain;
    if (std::none_of(items.begin(),items.end(),[&](auto const& item) {return item.model==model;}))
        throw std::runtime_error("Cannot bind terrain textures outside this scene");
    return *instance;
}
}
terrain_binding_runtime::~terrain_binding_runtime() {
    for (auto const& [scene,commands] : m_streams) for (auto const& command : commands) m_owner->end_terrain_command(2,command->bytes);
}
bool terrain_binding_runtime::busy(scene_ptr const& scene) const {return m_streams.contains(scene.c_ptr());}
bool terrain_binding_runtime::active(std::shared_ptr<command> const& command) const {
    auto it=m_streams.find(command->scene.c_ptr());return it!=m_streams.end() && !it->second.empty() && it->second.front()==command;
}
void terrain_binding_runtime::finish(std::shared_ptr<command> const& command,unsigned result) {
    auto it=m_streams.find(command->scene.c_ptr());
    --m_pending;m_bytes-=command->bytes;m_owner->end_terrain_command(result,command->bytes);
    it->second.pop_front();if (it->second.empty()) m_streams.erase(it);
}
void terrain_binding_runtime::cancel(scene_ptr const& scene) {
    auto it=m_streams.find(scene.c_ptr());if (it==m_streams.end()) return;
    diagnostic("info","Cancelling %zu pending terrain commands for this scene",it->second.size());
    for (auto const& command : it->second) {--m_pending;m_bytes-=command->bytes;m_owner->end_terrain_command(2,command->bytes);}
    m_streams.erase(it);
}
void terrain_binding_runtime::submit(std::shared_ptr<command> command) {
    if (!m_owner->accepting()) throw std::runtime_error("Terrain binding session is closed");
    if (m_pending>=128 || command->bytes>8u*1024u*1024u-m_bytes) throw std::runtime_error("Terrain command queue exceeds 128 commands / 8 MiB");
    auto key=command->scene.c_ptr();auto [it,inserted]=m_streams.try_emplace(key);
    try {it->second.push_back(command);} catch (...) {if (inserted) m_streams.erase(it);throw;}
    ++m_pending;m_bytes+=command->bytes;m_owner->begin_terrain_command(command->bytes);
    if (inserted) process(key,true);
}
void terrain_binding_runtime::defer(scene_ptr const& scene,size_t bytes,std::function<void()> action) {
    auto command=std::make_shared<struct command>();command->type=command::kind::action;command->scene=scene;
    command->bytes=bytes;command->action=std::move(action);submit(std::move(command));
}
void terrain_binding_runtime::add(scene_ptr const& scene,render_model_instance_ptr const& model,std::string const& name,u32 slot) {
    member(m_owner,scene,model);validate_terrain_texture_name(name);
    if (slot>=16) throw std::runtime_error("Terrain texture slot must be below 16");
    auto command=std::make_shared<struct command>();command->type=command::kind::add;command->scene=scene;command->model=model;command->name=name;command->slot=slot;submit(std::move(command));
}
void terrain_binding_runtime::remove(scene_ptr const& scene,render_model_instance_ptr const& model,u32 slot) {
    member(m_owner,scene,model);if (slot>=16) throw std::runtime_error("Terrain texture slot must be below 16");
    auto command=std::make_shared<struct command>();command->type=command::kind::remove;command->scene=scene;command->model=model;command->slot=slot;submit(std::move(command));
}
void terrain_binding_runtime::exchange(scene_ptr const& scene,std::string const& old_name,std::string const& name) {
    validate_terrain_texture_name(old_name);validate_terrain_texture_name(name);
    auto* destination=dynamic_cast<scene_resource*>(scene.c_ptr());
    if (!m_owner->accepting() || !destination || destination->owner!=m_owner || !destination->terrain_enabled)
        throw std::runtime_error("Terrain exchange requires a live terrain scene from this session");
    auto command=std::make_shared<struct command>();command->type=command::kind::exchange;command->scene=scene;command->old_name=old_name;command->name=name;submit(std::move(command));
}
void terrain_binding_runtime::process(base_scene* scene,bool propagate) {
    while (m_streams.contains(scene)) {
        auto command=m_streams.at(scene).front();if (command->loading) return;
        try {if (!start(command)) return;finish(command,0);}
        catch (std::exception const& error) {
            finish(command,1);cancel(command->scene);
            diagnostic("error","Terrain command failed; remaining scene commands cancelled: %s",error.what());
            if (propagate) throw;return;
        }
    }
}
bool terrain_binding_runtime::start(std::shared_ptr<command> const& command) {
    if (command->type==command::kind::action) {command->action();return true;}
    if (command->type==command::kind::remove) {
        auto& instance=member(m_owner,command->scene,command->model);auto layers=instance.texture_bindings();
        if (command->slot>=layers.size() || layers[command->slot].name.empty()) throw std::runtime_error("Cannot remove an absent terrain texture slot");
        layers[command->slot]={};instance.commit_texture_bindings(instance.prepare_texture_bindings(std::move(layers)));return true;
    }
    auto capture=[&](render_model_instance_ptr const& model) {
        auto& instance=member(m_owner,command->scene,model);
        command->targets.push_back({model,instance.cell(),instance.binding_revision(),instance.texture_bindings()});
    };
    if (command->type==command::kind::add) {
        auto& instance=member(m_owner,command->scene,command->model);auto const& layers=instance.texture_bindings();
        if (command->slot<layers.size() && layers[command->slot].name==command->name) return true;
        capture(command->model);
    } else {
        auto const& items=static_cast<scene_resource*>(command->scene.c_ptr())->models->terrain;
        for (auto const& item : items) {
            auto const& layers=member(m_owner,command->scene,item.model).texture_bindings();
            if (std::any_of(layers.begin(),layers.end(),[&](auto const& layer) {return layer.name==command->old_name;})) capture(item.model);
        }
        if (command->targets.empty()) throw std::runtime_error("Terrain exchange name is absent from this scene");
        if (command->old_name==command->name) return true;
    }
    std::array<std::string,3> paths={command->name,command->name+"_nmap",command->name+"_spec"};
    resources::request requests[3];resources::user_data_variant users[3];resources::user_data_variant const* data[3];
    for (unsigned role=0;role<3;++role) {
        texture_configuration config;config.srgb=role!=1;config.force_linear=role==1;config.generate_mips=true;config.require_rgba=true;
        users[role].set(config);data[role]=&users[role];requests[role]={paths[role].c_str(),resources::texture_class};
    }
    if (m_owner->terrain_binding_status().in_flight>=128) throw std::runtime_error("Terrain binding exceeds 128 in-flight triplet queries");
    command->loading=true;m_owner->begin_terrain_query();
    std::weak_ptr<terrain_binding_runtime> weak=shared_from_this();std::weak_ptr<struct command> pending=command;auto owner=m_owner;
    try {
        resources::query_resources(requests,3,[weak,pending,owner](resources::queries_result& result) {
            owner->end_terrain_query();auto self=weak.lock();auto command=pending.lock();
            if (!self || !command || !self->active(command)) return;
            self->complete(command,result);
        },render::g_allocator,data,nullptr,assert_on_fail_false);
    } catch (...) {m_owner->end_terrain_query();throw;}
    return false;
}
void terrain_binding_runtime::complete(std::shared_ptr<command> const& command,resources::queries_result& result) {
    try {
        if (!m_owner->accepting() || !result.is_successful()) throw std::runtime_error("Terrain diffuse/normal/specular binding query failed");
        terrain_layer_resources layer={command->name,result[0].get_unmanaged_resource(),result[1].get_unmanaged_resource(),result[2].get_unmanaged_resource()};
        std::vector<terrain_binding_commit> batch;
        for (auto const& target : command->targets) {
            auto& instance=member(m_owner,command->scene,target.model);
            if (instance.cell()!=target.cell || instance.binding_revision()!=target.revision) throw std::runtime_error("Terrain cell changed while binding query was pending");
            auto layers=target.layers;
            if (command->type==command::kind::add) {layers.resize(std::max(layers.size(),size_t(command->slot+1)));layers[command->slot]=layer;}
            else for (auto& slot : layers) if (slot.name==command->old_name) slot=layer;
            batch.push_back({&instance,instance.prepare_texture_bindings(std::move(layers))});
        }
        terrain_instance_resource::commit_texture_binding_batch(batch);
    } catch (std::exception const& error) {
        finish(command,1);cancel(command->scene);diagnostic("error","Terrain binding query rejected; previous sets retained: %s",error.what());return;
    }
    finish(command,0);process(command->scene.c_ptr(),false);
}
} } }
