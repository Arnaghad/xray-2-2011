#pragma once
#include <xray/render/facade/scene_view_mode.h>
namespace xray::render::vulkan {
inline bool editor_forward_mode(scene_view_mode mode) {return mode==unlit_view_mode || mode==wireframe_view_mode || mode==wireframe_two_sided_view_mode || mode==miplevel_view_mode
    || mode==shader_complexity_view_mode || mode==texture_density_view_mode || mode==geometry_complexity_view_mode || mode==overdraw_view_mode;}
inline bool editor_material_mode(scene_view_mode mode) {return mode==specular_intencity_view_mode || mode==emissive_only_view_mode || mode==unlit_with_ao_view_mode;}
inline bool editor_effect_mode(scene_view_mode mode) {return mode==ambient_occlusion_only_view_mode || mode==unlit_with_ao_view_mode || mode==distortion_only_view_mode;}
}
