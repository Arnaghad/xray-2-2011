#ifndef XRAY_VULKAN_SCENE_PARTICLES_H_INCLUDED
#define XRAY_VULKAN_SCENE_PARTICLES_H_INCLUDED
#include <xray/render/vulkan/scene_resources.h>
#include <xray/particle/engine.h>
#include <xray/render/vulkan/particle_resources.h>

namespace xray { namespace render { namespace vulkan {
std::shared_ptr<particle::engine> make_scene_particle_adapter(std::shared_ptr<scene_registry> const&);
std::vector<std::shared_ptr<particle_billboard_batch const>> snapshot_scene_particles(
    particle::world&,std::shared_ptr<scene_registry> const&,math::float4x4 const& view);
} } }
#endif
