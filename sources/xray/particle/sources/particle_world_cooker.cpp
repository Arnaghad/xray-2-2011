#include "pch.h"
#include "particle_world_cooker.h"
#include "particle_system_instance_cook.h"
#include "particle_world.h"
#include <xray/particle/world_resources.h>
#include <xray/particle/engine.h>
#include <xray/resources_unmanaged_allocator.h>
#include <stdexcept>

namespace xray { namespace particle {
particle_world_cooker::particle_world_cooker()
    : translate_query_cook(resources::particle_world_class,reuse_false,use_current_thread_id) {}

void particle_world_cooker::translate_query(resources::query_result_for_cook& query) {
    u8* buffer=nullptr;
    particle_world* value=nullptr;
    try {
        auto* config=query.user_data() ? query.user_data()->get_if<world_configuration>() : nullptr;
        auto bytes=query.creation_data_from_user();
        if (!config || !config->adapter || !config->max_particles || config->max_particles>50000
            || bytes.size()!=1 || !bytes.c_ptr() || *static_cast<u8 const*>(bytes.c_ptr())!=0)
            throw std::runtime_error("Particle world requires internal creation, owned adapter and capacity 1..50000");
        static_assert(sizeof(base_particle)%alignof(void*)==0);
        constexpr size_t offset=(sizeof(particle_world)+XRAY_DEFAULT_ALIGN_SIZE-1)/XRAY_DEFAULT_ALIGN_SIZE*XRAY_DEFAULT_ALIGN_SIZE;
        size_t arena_bytes=size_t(config->max_particles)*sizeof(base_particle);
        size_t total=offset+arena_bytes;
        buffer=UNMANAGED_ALLOC(u8,total);
        if (!buffer) throw std::bad_alloc();
        value=new(buffer) particle_world(*config->adapter);
        value->m_engine_owner=config->adapter;
        value->m_capacity=value->m_max_particles=config->max_particles;
        value->m_allocator.initialize(buffer+offset,arena_bytes,"particle_world");
        query.set_unmanaged_resource(value,resources::nocache_memory,total);
        buffer=nullptr;value=nullptr;query.finish_query(result_success);
    } catch (std::exception const& error) {
        if (value) value->~particle_world();
        if (buffer) UNMANAGED_FREE(buffer);
        LOG_ERROR("Particle world query failed: %s",error.what());query.finish_query(result_error);
    }
}
void particle_world_cooker::delete_resource(resources::resource_base* resource) {
    auto* value=static_cast<particle_world*>(resource);
    value->~particle_world();UNMANAGED_FREE(value);
}

namespace {
uninitialized_reference<particle_world_cooker> world_cook;
u32 registration_thread=0;
}
extern uninitialized_reference<particle_system_instance_cook> s_particle_cook_object;
void register_particles_cooker();
void unregister_particles_cooker();

void register_particle_world_cooker() {
    if (!resources::is_initialized()) throw std::runtime_error("Particle cooks require initialized resources");
    if (world_cook.initialized()) {
        if (registration_thread!=threading::current_thread_id()
            || resources::cook_base::find_cook(resources::particle_world_class)!=world_cook.c_ptr()
            || resources::cook_base::find_cook(resources::particle_system_instance_class)!=s_particle_cook_object.c_ptr())
            throw std::runtime_error("Particle cooks have another thread or registry owner");
        return;
    }
    if (resources::cook_base::find_cook(resources::particle_world_class)
        || resources::cook_base::find_cook(resources::particle_system_instance_class))
        throw std::runtime_error("Particle world/instance class ID is already owned");
    register_particles_cooker();
    XRAY_CONSTRUCT_REFERENCE(world_cook,particle_world_cooker)();
    resources::register_cook(world_cook.c_ptr());registration_thread=threading::current_thread_id();
    // Template raw-binary/source and unfinished preview cooks are deliberately not registered.
}
bool can_unregister_particle_world_cooker() {
    if (!world_cook.initialized()) return true;
    if (!resources::is_initialized() || registration_thread!=threading::current_thread_id()
        || resources::pending_queries_count() || world_cook->cook_users_count() || s_particle_cook_object->cook_users_count()
        || resources::cook_base::find_cook(resources::particle_world_class)!=world_cook.c_ptr()
        || resources::cook_base::find_cook(resources::particle_system_instance_class)!=s_particle_cook_object.c_ptr()) return false;
    return true;
}
bool try_unregister_particle_world_cooker() {
    if (!can_unregister_particle_world_cooker()) return false;
    if (!world_cook.initialized()) return true;
    resources::unregister_cook(resources::particle_world_class);XRAY_DESTROY_REFERENCE(world_cook);
    unregister_particles_cooker();registration_thread=0;return true;
}
void unregister_particle_world_cooker() {
    if (!try_unregister_particle_world_cooker())
        throw std::runtime_error("Particle cooks still have pending queries/resources or a foreign thread/registry owner");
}
} }
