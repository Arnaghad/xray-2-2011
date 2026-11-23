#include "pch.h"
#include "vulkan_particle_material_cook.h"
#include "vulkan_binary_config.h"
#include "vulkan_diagnostics.h"
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/facade/material_effects_instance_cook_data.h>
#include <xray/render/core/memory.h>
#include <xray/resources_query_result.h>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {
particle_material_snapshot capture(std::shared_ptr<scene_registry> const& owner,resources::unmanaged_resource_ptr const& source) {
    auto* material=dynamic_cast<material_resource*>(source.c_ptr());
    if (!owner || !owner->accepting() || !material || material->owner!=owner)
        throw std::runtime_error("Particle material requires a live same-session material resource");
    auto const& config=*material->data;
    if ((material->blend!=material_blend_mode::alpha && material->blend!=material_blend_mode::additive)
        || config.alpha_test || config.cull!=rhi::cull_mode::none || config.uv_scale!=std::array<float,2>{1,1})
        throw std::runtime_error("Particle material requires unlit alpha/additive, two-sided and unit UV tiling");
    auto const& s=config.sampler; rhi::sampler_desc defaults;
    if (s.min_filter!=defaults.min_filter || s.mag_filter!=defaults.mag_filter || s.mip_filter!=defaults.mip_filter
        || s.address_u!=defaults.address_u || s.address_v!=defaults.address_v || s.min_lod!=defaults.min_lod
        || s.max_lod!=defaults.max_lod || s.lod_bias!=defaults.lod_bias || s.anisotropy!=defaults.anisotropy
        || s.comparison!=defaults.comparison || s.compare!=defaults.compare)
        throw std::runtime_error("Particle material custom samplers are unsupported (fixed linear clamp LOD0 profile)");
    std::shared_ptr<texture_data const> texture;
    if (config.texture) texture=static_cast<texture_resource*>(config.texture.c_ptr())->snapshot();
    else {
        // An authored constant-color material needs a white sample, not a missing-texture fallback.
        auto white=std::make_shared<texture_data>();
        white->description.width=white->description.height=white->description.mip_levels=1;
        white->description.pixel_format=rhi::format::r8g8b8a8_unorm;
        white->mips={{255,255,255,255}};texture=std::move(white);
    }
    validate_particle_texture(*texture);
    return {std::move(texture),config.tint,material->blend==material_blend_mode::alpha ? particle_blend::alpha : particle_blend::additive};
}
void fail(resources::query_result_for_cook& query,std::exception const& error) {
    diagnostic("error","Particle material query failed: %s",error.what());query.finish_query(resources::cook_base::result_error);
}
}
particle_material_resource::particle_material_resource(std::shared_ptr<scene_registry> state,resources::unmanaged_resource_ptr material)
    : owner(std::move(state)),source(std::move(material)),data(capture(owner,source)) {}

particle_material_cook::particle_material_cook(std::shared_ptr<scene_registry> owner)
    : translate_query_cook(resources::material_effects_instance_class,reuse_false,use_current_thread_id),
      m_owner(std::move(owner)),m_allocator(render::g_allocator) {}
void particle_material_cook::finish(resources::query_result_for_cook& query,resources::unmanaged_resource_ptr material) {
    auto* result=XRAY_NEW_IMPL(*m_allocator,particle_material_resource)(m_owner,std::move(material));
    query.set_unmanaged_resource(result,resources::nocache_memory,sizeof(*result));query.finish_query(result_success);
}
void particle_material_cook::translate_query(resources::query_result_for_cook& query) {
    try {
        if (!m_owner->accepting()) throw std::runtime_error("Particle material cook session is closed");
        if (query.creation_data_from_user()) throw std::runtime_error("Particle effects raw creation payloads are unsupported");
        auto* pointer=query.user_data() ? query.user_data()->get_if<material_effects_instance_cook_data*>() : nullptr;
        auto* request=pointer ? *pointer : nullptr;
        if (!request || request->vertex_input_type!=particle_vertex_input_type || request->delete_in_cook
            || (request->cull_mode!=cull_mode_back && request->cull_mode!=cull_mode_none))
            throw std::runtime_error("Particle material query requires caller-owned billboard cook data (no subUV/beam/mesh)");
        // Copy retained dependencies synchronously; never carry the caller's userdata pointer into a child callback.
        if (request->material) { finish(query,request->material);return; }
        std::string name=query.get_requested_path();binary::validate_path(name);
        resources::query_resource(name.c_str(),resources::material_class,[this,&query](resources::queries_result& data) {
            try {
                if (!data.is_successful()) throw std::runtime_error("Particle material dependency failed");
                finish(query,data[0].get_unmanaged_resource());
            } catch (std::exception const& error) { fail(query,error); }
        },m_allocator,nullptr,&query,assert_on_fail_false);
    } catch (std::exception const& error) { fail(query,error); }
}
void particle_material_cook::delete_resource(resources::resource_base* resource) {
    auto* material=static_cast<particle_material_resource*>(resource);XRAY_DELETE_IMPL(*m_allocator,material);
}
} } }
