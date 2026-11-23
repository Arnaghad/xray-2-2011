#ifndef XRAY_PARTICLE_WORLD_RESOURCES_H_INCLUDED
#define XRAY_PARTICLE_WORLD_RESOURCES_H_INCLUDED

#include <xray/resources_unmanaged_resource.h>
#include <xray/particle/world.h>
#include <memory>

namespace xray { namespace particle {
struct engine;
typedef resources::resource_ptr<world,resources::unmanaged_intrusive_base> world_ptr;

// Passed by value as world query userdata; the adapter must not own its world.
// The scene owner must close the world before dropping its last owning handle.
struct world_configuration {
    std::shared_ptr<engine> adapter;
    u32 max_particles = 50000;
};
} }
#endif
