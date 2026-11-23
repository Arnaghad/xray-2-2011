#pragma once
#include <cstdint>
#include <cstddef>
namespace xray::editor::hosting {
struct sdk_scene_object {
    std::uint64_t id;
    std::uint32_t kind,flags; // locator/model/light; visible,selected,pickable
    char const* path;
    float position[3],rotation[3],scale[3];
    std::uint32_t color;
    float range,intensity;
};
struct sdk_scene_ray {float origin[3],direction[3];std::uint32_t snap,reserved;std::uint64_t exclude;};
struct sdk_scene_hit {std::uint64_t id;float point[3],distance;};
// Operations 7/8/9 are additive SDK-5 services; the SDK-4 payloads are unchanged.
struct sdk_viewport_settings {
    std::uint32_t version,size,mode,flags;
    float grid_step,selection_rate;
    std::uint32_t grid_dimension,selection_color;
};
struct sdk_viewport_snapshot {
    std::uint32_t version,size,mode,flags;
    float view[16],culling_view[16];
};
struct sdk_selection_bounds {float minimum[3],maximum[3];std::uint32_t count,reserved;};
static_assert(sizeof(sdk_viewport_settings)==32 && sizeof(sdk_viewport_snapshot)==144 && sizeof(sdk_selection_bounds)==32);
// Additive operations 10 configure, 11 pick, 12 evaluate a frozen drag origin.
struct sdk_gizmo_settings {
    std::uint32_t version,size,mode,active_axis;
    std::uint64_t object;
    float anchor[16],camera[16],projection[16];
    std::uint32_t width,height;
    float pixels,reserved;
};
struct sdk_gizmo_hit {
    std::uint32_t axis,mode;
    std::uint64_t object;
    float distance,scale,point[3],reserved;
};
struct sdk_gizmo_drag {
    std::uint32_t version,size,axis,reserved;
    sdk_gizmo_settings origin;
    sdk_scene_ray from,to;
};
struct sdk_gizmo_delta {std::uint32_t version,size,valid,axis;float amount,reserved[3];};
struct sdk_gizmo_delta_extended {std::uint32_t version,size,valid,axis;float amount,translation[3],rotation_axis[3],scale[3],reserved[2];};
static_assert(sizeof(sdk_gizmo_delta_extended)==64);
static_assert(sizeof(sdk_gizmo_settings)==232 && sizeof(sdk_gizmo_hit)==40 && sizeof(sdk_gizmo_drag)==328 && sizeof(sdk_gizmo_delta)==32);
struct sdk_scene_transform {
    std::uint32_t mode,axis,local,reserved;
    float amount;
    float position[3],rotation[3],scale[3],pivot[3];
};
struct sdk_scene_transform_frame {std::uint32_t version,size;sdk_scene_transform object;float frame[16];};
static_assert(sizeof(sdk_scene_transform_frame)==140);
struct sdk_scene_request {
    std::uint32_t version,size,operation,count;
    std::uint64_t session,query;
    void* data;
    void* result;
};
struct sdk_scene_callbacks {
    std::uint32_t version,size;
    void* context;
    int (*request)(void*,sdk_scene_request*);
};
using bind_scene_fn=int (*)(void*,sdk_scene_callbacks const*);
static_assert(sizeof(sdk_scene_object)==72 && sizeof(sdk_scene_request)==48 && sizeof(sdk_scene_callbacks)==24);
static_assert(sizeof(sdk_scene_transform)==68 && sizeof(sdk_scene_ray)==40 && sizeof(sdk_scene_hit)==24);
// Independent SDK view/output leases. Window is the host's stable native child,
// not the replaceable managed HWND. Operations 20/21/23/24: open/config/close/poll.
struct sdk_output_settings {
    std::uint32_t version,size,kind,flags; // camera/model/texture/material; active
    std::uint64_t id,window;
    char const* asset;
    float camera[16];
    float fov,near_plane,far_plane,reserved;
};
struct sdk_output_status {
    std::uint32_t version,size,state,pending; // loading/ready/failed/closed
    std::uint64_t id,window;
    std::uint32_t width,height;
    std::uint64_t frames;
    float minimum[3],maximum[3];
};
static_assert(sizeof(sdk_output_settings)==120 && sizeof(sdk_output_status)==72);
// Operation 26 poses only an independent model output. Matrices are absolute
// skeleton-space transforms in the model's verified bind-pose name order.
struct sdk_output_pose {std::uint32_t version,size;std::uint64_t output;std::uint32_t count,reserved;float const* matrices;};
static_assert(sizeof(sdk_output_pose)==32);
struct sdk_scene_statistics {std::uint64_t scene_builds,model_cooks,delta_commits,objects;};
static_assert(sizeof(sdk_scene_statistics)==32);
struct sdk_runtime_settings {std::uint32_t version,size,flags,reserved;float camera[16],projection[16];};
struct sdk_runtime_status {std::uint32_t version,size,state,paused;std::uint64_t ticks,frames;double seconds;std::uint32_t logic_thread,objects;std::uint64_t generation,reserved;};
struct sdk_runtime_physics_status {std::uint32_t version,size,bodies,dynamic;float minimum_y,maximum_y;std::uint64_t steps;};
static_assert(sizeof(sdk_runtime_physics_status)==32);
struct sdk_runtime_pose {
    std::uint32_t version,size;
    std::uint64_t object;
    std::uint32_t count,flags; // bit 0: has_transform, bit 1: has_bones
    float transform[16];
    float const* matrices;
};
static_assert(sizeof(sdk_runtime_pose)==96);
static_assert(sizeof(sdk_runtime_settings)==144 && sizeof(sdk_runtime_status)==64);
// SDK-6: publish a validated writable texture source (40), poll reload (41).
struct sdk_model_publish {char const* uri;std::uint64_t resource;};
static_assert(sizeof(sdk_model_publish)==16);
struct sdk_material_keys {std::uint32_t version,size,capacity,used;char* utf8;};
struct sdk_material_binding {char const* uri;std::uint64_t resource;};
static_assert(sizeof(sdk_material_keys)==24 && sizeof(sdk_material_binding)==16);
struct sdk_texture_source {std::uint32_t version,size;char const* virtual_path;char const* physical_path;};
struct sdk_asset_publish_status {std::uint32_t version,size,affected,reserved;std::uint64_t requested,reloaded,unchanged,failed,cancelled,pending;};
static_assert(sizeof(sdk_texture_source)==24 && sizeof(sdk_asset_publish_status)==64);
// SDK-7: world-tool object extensions (50), terrain cell buffer uploads (51), live world status (52).
struct sdk_world_object_extension {
    std::uint32_t version, size;
    std::uint64_t id;
    std::uint32_t world_kind, light_type;
    float spot_umbra_angle, spot_penumbra_angle, spot_falloff, attenuation_power;
    float diffuse_factor, specular_factor, decal_width, decal_height;
    float decal_depth, time_scale, wire_width, sun_distance;
    std::uint32_t flags, shadow_map_size, num_sun_cascades, locator_shape;
    char const* primary_uri;
    char const* secondary_uri;
    char const* tertiary_uri;
    char const* quaternary_uri;
    float extra_points[9];
    std::uint32_t reserved_pad;
};
struct sdk_terrain_cell_upload {
    std::uint32_t version, size;
    std::int32_t kx, ky;
    char const* virtual_path;
    void const* bytes;
    std::uint32_t byte_length, flags;
};
struct sdk_world_scene_status {
    std::uint32_t version, size;
    std::uint32_t terrain_cells, terrain_collision_triangles;
    std::uint32_t solid_visuals, skinned_visuals;
    std::uint32_t lights, speedtrees;
    std::uint32_t particles, decals;
    std::uint32_t sky_active, misc_locators;
};
static_assert(sizeof(sdk_world_object_extension)==160 && sizeof(sdk_terrain_cell_upload)==40 && sizeof(sdk_world_scene_status)==48);
}
