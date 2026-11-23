#pragma once
#include <cstdint>
#include <cstddef>

namespace xray::editor::hosting {
// Payloads are copied on the owner thread. Requests retain no managed pointers.
struct sdk_resource_request {
    std::uint32_t version,size;
    std::uint64_t query;
    std::uint32_t operation,kind; // begin, poll, release/cancel, mount; raw, model, material, texture
    char const* path;
    char const* physical;
};
struct sdk_resource_result {
    std::uint32_t version,size;
    std::uint64_t query;
    std::uint32_t state,units; // pending, ready, failed, cancelled
    std::uint64_t bytes;
};
struct sdk_resource_callbacks {
    std::uint32_t version,size;
    void* context;
    int (*request)(void*,sdk_resource_request const*,sdk_resource_result*);
};
struct sdk_byte_buffer {std::uint32_t version,size,capacity,used;void* data;};
struct sdk_model_binding {char const* surface;std::uint64_t material;};
struct sdk_model_build {std::uint32_t version,size,count,reserved;char const* identity;sdk_model_binding const* surfaces;};
struct sdk_animation_sample {std::uint32_t version,size;float seconds;std::uint32_t reserved;sdk_byte_buffer output;};
struct sdk_collision_primitive {std::uint32_t type,reserved;float position[3],rotation[3],scale[3];};
struct sdk_model_physics {std::uint32_t version,size,count,reserved;float mass,friction,restitution,linear_damping,angular_damping,mass_center[3],linear_sleeping,angular_sleeping;std::uint32_t additional_damping;float additional_factor,additional_linear_threshold,additional_angular_threshold,additional_angular_factor;std::uint32_t reserved2;sdk_collision_primitive const* primitives;};
static_assert(sizeof(sdk_collision_primitive)==44 && sizeof(sdk_model_physics)==88);
static_assert(sizeof(sdk_animation_sample)==40);
static_assert(sizeof(sdk_byte_buffer)==24 && sizeof(sdk_model_binding)==16 && sizeof(sdk_model_build)==32);
using bind_resources_fn=int (*)(void*,sdk_resource_callbacks const*);
static_assert(sizeof(sdk_resource_request)==40 && offsetof(sdk_resource_request,path)==24);
static_assert(sizeof(sdk_resource_result)==32 && sizeof(sdk_resource_callbacks)==24);
}
