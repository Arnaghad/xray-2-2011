#include "pch.h"
#include "particle_system_instance_cook.h"
#include "particle_system.h"
#include "particle_world.h"
#include <xray/render/facade/material_effects_instance_cook_data.h>
#include <algorithm>
#include <string>
#include <vector>
#include <stdexcept>

namespace xray { namespace particle {
particle_system_instance_cook::particle_system_instance_cook()
    : super(resources::particle_system_instance_class,reuse_false,use_current_thread_id) {}

void particle_system_instance_cook::translate_query(resources::query_result_for_cook& parent) {
    try {
        auto* owner=parent.user_data() ? parent.user_data()->get_if<world_ptr>() : nullptr;
        auto* value=owner ? dynamic_cast<particle_world*>(owner->c_ptr()) : nullptr;
        if (!value || !value->has_deleter_object() || value->get_class_id()!=resources::particle_world_class)
            throw std::runtime_error("Particle instance query requires a retained cooked world_ptr");
        value->require_accepting();
        if (!resources::cook_base::find_cook(resources::particle_system_class))
            throw std::runtime_error("Particle template cook is not registered");
        query_resource(parent.get_requested_path(),resources::particle_system_class,
            [this, owner=*owner](resources::queries_result& data) { on_sub_resources_loaded(data,owner); },
            memory::g_mt_allocator,nullptr,&parent,assert_on_fail_false);
    } catch (std::exception const& error) {
        LOG_ERROR("Particle instance query failed: %s",error.what());parent.finish_query(result_error);
    }
}

void particle_system_instance_cook::on_sub_resources_loaded(resources::queries_result& data,world_ptr const& owner) {
    auto* parent=data.get_parent_query();
    try {
        auto& value=*static_cast<particle_world*>(owner.c_ptr());
        value.require_accepting();
        if (!data.is_successful()) throw std::runtime_error("Particle template dependency failed");
        auto* templ=dynamic_cast<particle_system*>(data[0].get_unmanaged_resource().c_ptr());
        // This is a trusted cooked-template boundary, not a parser for legacy raw binaries.
        if (!templ || templ->m_num_lods!=1 || !templ->m_lods || !templ->m_lods[0].m_emitters_array
            || !templ->m_lods[0].m_num_emitters || templ->m_lods[0].m_num_emitters>64)
            throw std::runtime_error("Particle instances require one LOD and 1..64 cooked emitters");
        if (!resources::cook_base::find_cook(resources::material_effects_instance_class))
            throw std::runtime_error("Particle material effects cook is not registered");
        std::vector<std::string> names;
        for (u32 i=0;i<templ->m_lods[0].m_num_emitters;++i) {
            auto const& emitter=templ->m_lods[0].m_emitters_array[i];
            if (emitter.m_event)
                throw std::runtime_error("Particle child/event emitters are not integrated");
            auto* billboard=dynamic_cast<particle_action_billboard*>(emitter.get_data_type_action());
            if (!billboard || !billboard->get_visibility() || billboard->m_use_sub_uv
                || billboard->m_billboard_parameters.locked_axis!=particle_locked_axis_none
                || (billboard->m_billboard_parameters.screen_alignment!=particle_screen_alignment_square
                    && billboard->m_billboard_parameters.screen_alignment!=particle_screen_alignment_rectangle))
                throw std::runtime_error("Particle instances require camera-facing square/rectangle billboards without subUV");
            // Match the constructor's first visible data type; bound malformed trusted action chains too.
            particle_action_data_type* visible=nullptr;
            unsigned count=0;
            for (particle_action* action=emitter.m_actions;action;action=action->m_next) {
                if (++count>64) throw std::runtime_error("Particle emitter exceeds 64 actions or contains a cycle");
                auto* type=dynamic_cast<particle_action_data_type*>(action);
                if (type && type->get_visibility() && !visible) visible=type;
            }
            if (visible!=billboard) throw std::runtime_error("Particle emitter data type does not match its action chain");
            pcstr name=emitter.get_material_name();
            if (std::find(name,name+128,'\0')==name+128)
                throw std::runtime_error("Particle material name is not terminated");
            names.emplace_back(*name ? name : "default_particle");
        }
        using material_request=render::material_effects_instance_cook_data;
        std::vector<std::shared_ptr<material_request>> payloads;
        std::vector<resources::user_data_variant> users(names.size());
        std::vector<resources::user_data_variant const*> user_ptrs;
        std::vector<resources::request> requests;
        for (size_t i=0;i<names.size();++i) {
            payloads.push_back(std::make_shared<material_request>(render::particle_vertex_input_type,nullptr,false));
            users[i].set(payloads.back().get());user_ptrs.push_back(&users[i]);
            requests.push_back({names[i].c_str(),resources::material_effects_instance_class});
        }
        // The callback retains the world, template and raw-pointer userdata until every child completes.
        resources::query_resources(requests.data(),u32(requests.size()),
            [this,owner,particle_template=data[0].get_unmanaged_resource(),payloads](resources::queries_result& materials) {
                on_materials_loaded(materials,owner,particle_template);
            },memory::g_mt_allocator,user_ptrs.data(),parent,assert_on_fail_false);
    } catch (std::exception const& error) {
        LOG_ERROR("Particle instance dependency failed: %s",error.what());parent->finish_query(result_error);
    }
}

void particle_system_instance_cook::on_materials_loaded(resources::queries_result& data,world_ptr const& owner,
    resources::unmanaged_resource_ptr const& particle_template) {
    auto* parent=data.get_parent_query();
    try {
        auto& value=*static_cast<particle_world*>(owner.c_ptr());
        value.require_accepting();
        if (!data.is_successful()) throw std::runtime_error("Particle material dependency failed");
        auto* templ=static_cast<particle_system*>(particle_template.c_ptr());
        for (u32 i=0;i<templ->m_lods[0].m_num_emitters;++i)
            if (!data[i].get_unmanaged_resource()) throw std::runtime_error("Particle material dependency is empty");
        auto destroy=[](particle_system_instance_impl* instance) { MT_DELETE(instance); };
        std::unique_ptr<particle_system_instance_impl,decltype(destroy)> instance(
            MT_NEW(particle_system_instance_impl)(value),destroy);
        instance->m_world_owner=owner.c_ptr();
        instance->set_template(0,templ);
        for (u32 i=0;i<templ->m_lods[0].m_num_emitters;++i)
            instance->add_emitter_instance(0,value.create_ready_emitter_instance(
                templ->m_lods[0].m_emitters_array[i],data[i].get_unmanaged_resource()));
        value.require_accepting(); // An adapter callback can close the world during construction.
        // Resource manager installs creation/deleter metadata before any intrusive pointer is made.
        parent->set_unmanaged_resource(instance.get(),resources::nocache_memory,sizeof(*instance));
        instance.release();parent->finish_query(result_success);
    } catch (std::exception const& error) {
        LOG_ERROR("Particle instance dependency failed: %s",error.what());parent->finish_query(result_error);
    }
}

void particle_system_instance_cook::delete_resource(resources::resource_base* resource) {
    auto* instance=static_cast<particle_system_instance_impl*>(resource);MT_DELETE(instance);
}

xray::uninitialized_reference<particle_system_instance_cook> s_particle_cook_object;
void register_particles_cooker() {
    if (s_particle_cook_object.initialized()) {
        if (resources::cook_base::find_cook(resources::particle_system_instance_class)!=s_particle_cook_object.c_ptr())
            throw std::runtime_error("Particle instance cook registry ownership changed");
        return;
    }
    if (resources::cook_base::find_cook(resources::particle_system_instance_class))
        throw std::runtime_error("Particle instance class ID is already owned");
    XRAY_CONSTRUCT_REFERENCE(s_particle_cook_object,particle_system_instance_cook)();
    resources::register_cook(s_particle_cook_object.c_ptr());
}
void unregister_particles_cooker() {
    if (!s_particle_cook_object.initialized()) return;
    resources::unregister_cook(resources::particle_system_instance_class);
    XRAY_DESTROY_REFERENCE(s_particle_cook_object);
}
} }
