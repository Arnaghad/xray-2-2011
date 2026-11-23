#include "pch.h"
#include "m8_control_world.h"
#include <xray/render/facade/control_level.h>
namespace stalker2 {
static xray::command_line::key workflow_key("m8_workflow","","render","M8 fixed-time controls, camera, reload and pending shutdown");
xray::engine_user::world* create_m8_control_world(xray::engine_user::engine& e,xray::render::world& r) {
    return NEW(xray::render::control_level::control_world)(e,r,*g_allocator,false,workflow_key.is_set());
}
void destroy_m8_control_world(xray::engine_user::world*& world) {
    auto* control=static_cast<xray::render::control_level::control_world*>(world);DELETE(control);world=nullptr;
}
}
