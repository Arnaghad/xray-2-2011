#ifndef XRAY_RENDER_UI_DRAW_OPTIONS_H_INCLUDED
#define XRAY_RENDER_UI_DRAW_OPTIONS_H_INCLUDED
#include <xray/render/engine/base_classes.h>
namespace xray { namespace render { namespace ui {
struct draw_options {
    resources::unmanaged_resource_ptr texture;
    bool scissor_enabled=false;
    math::rectangle<math::float2> scissor={math::float2(0,0),math::float2(0,0)};
};
} } }
#endif
