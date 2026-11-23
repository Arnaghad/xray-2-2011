#ifndef XRAY_ENGINE_M8_CONTROL_PROTOCOL_H_INCLUDED
#define XRAY_ENGINE_M8_CONTROL_PROTOCOL_H_INCLUDED
#include <cstdint>

namespace xray { namespace engine { namespace m8_control {
// One atomic HWND property publishes both frame and shot. ACK returns that
// exact token after readback; logic never publishes a second outstanding shot.
inline constexpr char request_property[]="XRAY_M8_CAPTURE_REQUEST";
inline constexpr char ack_property[]="XRAY_M8_CAPTURE_ACK";
// Optional model preview, published only after all native cooks are ready.
inline constexpr char preview_property[]="XRAY_M8_MODEL_PREVIEW";
enum class shot : unsigned {
    overview=1, inspection, no_ssao, restored_ssao, no_distortion, restored_distortion,
    no_decals, restored_decals, no_particles, restored_particles, no_tree, restored_tree,
    no_actor, restored_actor, no_static, restored_static, no_terrain, restored_terrain,
    no_alpha, restored_alpha, no_additive, restored_additive,
    particle_moved, restored_particle_transform, tree_moved, restored_tree_transform,
    decal_moved, restored_decal_transform, actor_transition, restored_animation,
    particles_stopped_removed, particles_replayed,
    camera_moved, camera_restored, unloaded, reloaded_overview, reloaded_inspection,
    editor_props_hidden, editor_props_restored, editor_props_moved, editor_transform_restored,
    editor_cell_isolated, editor_layer_added, editor_layer_removed, editor_cells_restored,
    editor_layer_exchanged, editor_layers_restored,
    editor_overlay, editor_overlay_restored,
    sdk_property_baseline, sdk_property_edited, sdk_property_undo, sdk_property_redo, sdk_property_restored,
    sdk_layout_edited, sdk_layout_undo, sdk_layout_redo, sdk_layout_restored,
    sdk_mode_0,sdk_mode_1,sdk_mode_2,sdk_mode_3,sdk_mode_4,sdk_mode_5,sdk_mode_6,sdk_mode_7,sdk_mode_8,sdk_mode_9,sdk_mode_10,
    sdk_mode_11,sdk_mode_12,sdk_mode_13,sdk_mode_14,sdk_mode_15,sdk_mode_16,sdk_mode_17,sdk_mode_18,sdk_mode_19,sdk_mode_20
};
inline char const* name(shot value) {
    switch(value) {
#define XRAY_M8_SHOT(id) case shot::id:return #id
        XRAY_M8_SHOT(overview);XRAY_M8_SHOT(inspection);XRAY_M8_SHOT(no_ssao);XRAY_M8_SHOT(restored_ssao);
        XRAY_M8_SHOT(no_distortion);XRAY_M8_SHOT(restored_distortion);XRAY_M8_SHOT(no_decals);XRAY_M8_SHOT(restored_decals);
        XRAY_M8_SHOT(no_particles);XRAY_M8_SHOT(restored_particles);XRAY_M8_SHOT(no_tree);XRAY_M8_SHOT(restored_tree);
        XRAY_M8_SHOT(no_actor);XRAY_M8_SHOT(restored_actor);XRAY_M8_SHOT(no_static);XRAY_M8_SHOT(restored_static);
        XRAY_M8_SHOT(no_terrain);XRAY_M8_SHOT(restored_terrain);
        XRAY_M8_SHOT(no_alpha);XRAY_M8_SHOT(restored_alpha);XRAY_M8_SHOT(no_additive);XRAY_M8_SHOT(restored_additive);
        XRAY_M8_SHOT(particle_moved);XRAY_M8_SHOT(restored_particle_transform);XRAY_M8_SHOT(tree_moved);XRAY_M8_SHOT(restored_tree_transform);
        XRAY_M8_SHOT(decal_moved);XRAY_M8_SHOT(restored_decal_transform);XRAY_M8_SHOT(actor_transition);XRAY_M8_SHOT(restored_animation);
        XRAY_M8_SHOT(particles_stopped_removed);XRAY_M8_SHOT(particles_replayed);XRAY_M8_SHOT(camera_moved);XRAY_M8_SHOT(camera_restored);
        XRAY_M8_SHOT(unloaded);XRAY_M8_SHOT(reloaded_overview);XRAY_M8_SHOT(reloaded_inspection);
        XRAY_M8_SHOT(editor_props_hidden);XRAY_M8_SHOT(editor_props_restored);
        XRAY_M8_SHOT(editor_props_moved);XRAY_M8_SHOT(editor_transform_restored);
        XRAY_M8_SHOT(editor_cell_isolated);XRAY_M8_SHOT(editor_cells_restored);
        XRAY_M8_SHOT(editor_layer_added);XRAY_M8_SHOT(editor_layer_removed);
        XRAY_M8_SHOT(editor_layer_exchanged);XRAY_M8_SHOT(editor_layers_restored);
        XRAY_M8_SHOT(editor_overlay);XRAY_M8_SHOT(editor_overlay_restored);
        XRAY_M8_SHOT(sdk_property_baseline);XRAY_M8_SHOT(sdk_property_edited);XRAY_M8_SHOT(sdk_property_undo);
        XRAY_M8_SHOT(sdk_property_redo);XRAY_M8_SHOT(sdk_property_restored);
        XRAY_M8_SHOT(sdk_layout_edited);XRAY_M8_SHOT(sdk_layout_undo);XRAY_M8_SHOT(sdk_layout_redo);XRAY_M8_SHOT(sdk_layout_restored);
        XRAY_M8_SHOT(sdk_mode_0);XRAY_M8_SHOT(sdk_mode_1);XRAY_M8_SHOT(sdk_mode_2);XRAY_M8_SHOT(sdk_mode_3);XRAY_M8_SHOT(sdk_mode_4);XRAY_M8_SHOT(sdk_mode_5);XRAY_M8_SHOT(sdk_mode_6);
        XRAY_M8_SHOT(sdk_mode_7);XRAY_M8_SHOT(sdk_mode_8);XRAY_M8_SHOT(sdk_mode_9);XRAY_M8_SHOT(sdk_mode_10);XRAY_M8_SHOT(sdk_mode_11);XRAY_M8_SHOT(sdk_mode_12);XRAY_M8_SHOT(sdk_mode_13);
        XRAY_M8_SHOT(sdk_mode_14);XRAY_M8_SHOT(sdk_mode_15);XRAY_M8_SHOT(sdk_mode_16);XRAY_M8_SHOT(sdk_mode_17);XRAY_M8_SHOT(sdk_mode_18);XRAY_M8_SHOT(sdk_mode_19);XRAY_M8_SHOT(sdk_mode_20);
#undef XRAY_M8_SHOT
    }
    return nullptr;
}
inline std::uintptr_t token(unsigned frame,shot value) {return (std::uintptr_t(frame)<<8)|unsigned(value);}
inline shot token_shot(std::uintptr_t value) {return shot(value&255);}
inline unsigned token_frame(std::uintptr_t value) {return unsigned(value>>8);}
} } }
#endif
