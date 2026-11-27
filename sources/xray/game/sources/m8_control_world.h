#ifndef STALKER2_M8_CONTROL_WORLD_H_INCLUDED
#define STALKER2_M8_CONTROL_WORLD_H_INCLUDED
#include <xray/engine/engine_user.h>
namespace stalker2 {
bool m8_control_level_requested();
bool test_range_requested();
xray::engine_user::world* create_m8_control_world(xray::engine_user::engine&,xray::render::world&);
void destroy_m8_control_world(xray::engine_user::world*&);
}
#endif
