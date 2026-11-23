#include "pch.h"
#include "editor_world.h"

extern "C" {

XRAY_DLL_EXPORT xray::editor::editor_world* xray_native_editor_create(xray::editor::engine* engine)
{
    return new xray::editor::editor_world(*engine);
}

XRAY_DLL_EXPORT void xray_native_editor_destroy(xray::editor::editor_world* world)
{
    delete world;
}

XRAY_DLL_EXPORT void xray_native_editor_tick(xray::editor::editor_world* world)
{
    world->tick();
}

} // extern "C"
