#ifndef XRAY_BOUNDED_PARTICLE_TEMPLATE_COOK_H_INCLUDED
#define XRAY_BOUNDED_PARTICLE_TEMPLATE_COOK_H_INCLUDED
#include <xray/resources_cook_classes.h>

namespace xray { namespace particle {
class bounded_particle_template_cook : public resources::translate_query_cook {
public:
    bounded_particle_template_cook();
    bool cache_by_game_resources_manager() override { return false; }
    void translate_request_path(pcstr,fs_new::virtual_path_string&) const override;
    void translate_query(resources::query_result_for_cook&) override;
    void delete_resource(resources::resource_base*) override;
};
void check_particle_template_registration();
void register_bounded_particle_template_cook();
bool can_unregister_bounded_particle_template_cook();
void unregister_bounded_particle_template_cook();
} }
#endif
