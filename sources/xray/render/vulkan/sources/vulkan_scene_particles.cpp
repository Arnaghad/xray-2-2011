#include "pch.h"
#include "vulkan_scene_particles.h"
#include <xray/render/vulkan/particle_resources.h>
#include <xray/particle/render_particle_emitter_instance.h>
#include <xray/render/base/memory.h>
#include <stdexcept>
#include <cmath>

namespace xray { namespace render { namespace vulkan {
namespace {
// No scene/world ownership here: retained instances may outlive a closed scene.
class scene_particle_adapter final : public particle::engine {
    std::weak_ptr<scene_registry> m_owner;
    u32 const m_thread=threading::current_thread_id();
    struct emitter final : particle::render_particle_emitter_instance {
        scene_particle_adapter& adapter;
        resources::unmanaged_resource_ptr material;
        math::float4x4 transform;
        particle::particle_list_type const& particles;
        particle_billboard_options options;
        explicit emitter(scene_particle_adapter& value,particle::particle_list_type const& list,math::float4x4 const& matrix,
            particle::enum_particle_screen_alignment alignment,math::float4 const& color)
            : adapter(value),transform(matrix),particles(list) {options.alignment=alignment;options.color=color;}
        std::shared_ptr<particle_billboard_batch const> snapshot(std::shared_ptr<scene_registry> const& owner,math::float4x4 const& view) const {
            if (adapter.active_owner()!=owner) throw std::runtime_error("Particle emitter belongs to another scene session");
            auto* resource=dynamic_cast<particle_material_resource*>(material.c_ptr());
            if (!resource) throw std::runtime_error("Particle emitter has no ready material");
            auto captured=options;captured.transform=transform;captured.blend=resource->data.blend;
            for (unsigned c=0;c<4;++c) captured.color[c]*=resource->data.tint[c];
            return snapshot_billboards(particles,captured,view,resource->data.texture);
        }
        void change_material(resources::unmanaged_resource_ptr const& value) override {
            auto owner=adapter.active_owner();
            auto* resource=dynamic_cast<particle_material_resource*>(value.c_ptr());
            if (!resource || resource->owner!=owner) throw std::runtime_error("Particle material belongs to another Vulkan session");
            validate_particle_texture(*resource->data.texture);material=value;
        }
        void update_render_buffers(particle::enum_particle_data_type type,bool subuv,u32 count,u32 sheets) override {
            adapter.active_owner();
            if (type!=particle::particle_data_type_billboard || subuv || count>particle_batch_limit || sheets)
                throw std::runtime_error("Scene particles support bounded non-subUV billboards only");
        }
        void set_transform(math::float4x4 const& matrix) override {
            adapter.active_owner();
            for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c)
                if (!std::isfinite(matrix.elements[r][c])) throw std::runtime_error("Non-finite particle transform");
            transform=matrix;
        }
        void set_aabb(math::aabb const&) override {adapter.active_owner();}
    };
    std::shared_ptr<scene_registry> active_owner() const {
        if (m_thread!=threading::current_thread_id()) throw std::runtime_error("Particle adapter requires its owner thread");
        auto owner=m_owner.lock();
        if (!owner || !owner->accepting()) throw std::runtime_error("Particle adapter session is closed");
        return owner;
    }
public:
    explicit scene_particle_adapter(std::shared_ptr<scene_registry> const& owner) : m_owner(owner) {}
    particle::render_particle_emitter_instance* create_render_emitter_instance(particle::particle_emitter_instance&,
        particle::particle_list_type const& particles,particle::billboard_parameters* billboard,particle::beamtrail_parameters* beam,
        particle::enum_particle_locked_axis axis,particle::enum_particle_screen_alignment alignment,
        math::float4x4 const& transform,math::float4 const& color) override {
        active_owner();
        if (!billboard || beam || axis!=particle::particle_locked_axis_none
            || (alignment!=particle::particle_screen_alignment_square && alignment!=particle::particle_screen_alignment_rectangle))
            throw std::runtime_error("Unsupported scene particle emitter profile");
        return new emitter(*this,particles,transform,alignment,color);
    }
    void destroy(particle::render_particle_emitter_instance*& value) override {
        if (m_thread!=threading::current_thread_id()) throw std::runtime_error("Particle adapter destruction requires its owner thread");
        auto* resource=dynamic_cast<emitter*>(value);
        if (!resource || &resource->adapter!=this) throw std::runtime_error("Foreign particle render emitter");
        delete resource;value=nullptr;
    }
    static std::shared_ptr<particle_billboard_batch const> snapshot(particle::render_particle_emitter_instance& instance,
        std::shared_ptr<scene_registry> const& owner,math::float4x4 const& view) {
        auto* resource=dynamic_cast<emitter*>(&instance);
        if (!resource) throw std::runtime_error("Foreign particle renderer adapter");
        return resource->snapshot(owner,view);
    }
};
}
std::shared_ptr<particle::engine> make_scene_particle_adapter(std::shared_ptr<scene_registry> const& owner) {
    return std::make_shared<scene_particle_adapter>(owner);
}
std::vector<std::shared_ptr<particle_billboard_batch const>> snapshot_scene_particles(
    particle::world& world,std::shared_ptr<scene_registry> const& owner,math::float4x4 const& view) {
    validate_particle_transform(view);
    particle::render_particle_emitter_instances_type emitters(get_render_allocator());
    world.get_render_emitter_instances_unculled(emitters);
    std::vector<std::shared_ptr<particle_billboard_batch const>> result;
    for (auto* emitter:emitters) {
        if (!emitter) throw std::runtime_error("Null particle render emitter");
        auto batch=scene_particle_adapter::snapshot(*emitter,owner,view);
        if (batch->indices.empty()) continue;
        result.push_back(std::move(batch));validate_particle_batches(result);
    }
    return result;
}
} } }
