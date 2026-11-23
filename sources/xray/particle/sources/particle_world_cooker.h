#ifndef PARTICLE_WORLD_COOKER_H_INCLUDED
#define PARTICLE_WORLD_COOKER_H_INCLUDED
#include <xray/resources_cook_classes.h>

namespace xray { namespace particle {
void register_particle_world_cooker();
bool can_unregister_particle_world_cooker();
bool try_unregister_particle_world_cooker();
void unregister_particle_world_cooker();
class particle_world_cooker : public resources::translate_query_cook {
public:
    particle_world_cooker();
    bool cache_by_game_resources_manager() override { return false; }
    void translate_query(resources::query_result_for_cook& query) override;
    void delete_resource(resources::resource_base* resource) override;
};
} }
#endif
