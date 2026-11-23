#ifndef XRAY_ENGINE_TERRAIN_COLLISION_H_INCLUDED
#define XRAY_ENGINE_TERRAIN_COLLISION_H_INCLUDED
#include <xray/engine/api.h>

namespace xray { namespace engine {
// Register/finalize on the renderer/resource-callback thread with physics callbacks quiescent.
XRAY_ENGINE_API void register_terrain_collision_cook(memory::base_allocator&);
// Nonblocking: retained resources and pending queries keep their real deleter alive.
XRAY_ENGINE_API bool finalize_terrain_collision_cook();
struct terrain_contact_report {
    u64 mesh_contacts,corrected_normals,swapped_contacts;
    bool installed;
};
// Monotonic counters; installation/removal requires quiescent physics callbacks.
XRAY_ENGINE_API terrain_contact_report terrain_contact_diagnostics();
} }
#endif
