#include "pch.h"
#include <xray/render/vulkan/scene_resources.h>
#include <xray/render/vulkan/model_resources.h>
#include <xray/render/vulkan/material_resources.h>
#include <xray/resources_cook_classes.h>
#include <xray/resources_query_result.h>
#include <xray/render/core/memory.h>
#include "vulkan_diagnostics.h"
#include "vulkan_presentation_output.h"
#include "vulkan_model_cook.h"
#include "vulkan_vegetation_cook.h"
#include "vulkan_material_cook.h"
#include "vulkan_particle_material_cook.h"
#include "vulkan_texture_cook.h"
#include "vulkan_terrain_cook.h"
#include "vulkan_scene_particles.h"
#include <stdexcept>
#include <cmath>
#include <atomic>

namespace xray { namespace render { namespace vulkan {
namespace { std::atomic<u64> next_temporal_id{1}; }
scene_registry::scene_registry(HWND window, std::shared_ptr<presentation_output> output)
    : m_thread(threading::current_thread_id()),m_window(window), m_output(std::move(output)) {
    if (!m_output) { m_output = std::make_shared<presentation_output>(); m_output->hwnd = window; }
    if (m_output->hwnd != window) throw std::runtime_error("Presentation output HWND does not match the resource session");
    record_texture_reload({});
}
void scene_registry::add(unsigned kind, void* resource, HWND window) {
    std::lock_guard lock(m_mutex);
    if (kind >= 3 || !m_accepting || !resource) throw std::runtime_error("Scene registry is closed or invalid");
    if (kind == 2) {
        if (!IsWindow(window)) throw std::runtime_error("Output requires a live HWND");
        for (auto* value:m_resources[2]) if (static_cast<output_resource*>(value)->window()==window)
            throw std::runtime_error("HWND already has a cooked output");
    }
    if (!m_resources[kind].insert(resource).second) throw std::runtime_error("Duplicate scene registry entry");
}
void scene_registry::remove(unsigned kind, void* resource) noexcept {
    std::lock_guard lock(m_mutex); if (kind < 3) m_resources[kind].erase(resource);
}
void scene_registry::close() {
    if (threading::current_thread_id()!=m_thread) throw std::runtime_error("Scene session close requires its owner thread");
    std::vector<particle::world_ptr> worlds;
    std::map<std::string,resources::unmanaged_resource_ptr> retired_materials;
    std::map<std::string,std::shared_ptr<static_model_data const>> retired_models;
    {
        std::lock_guard lock(m_mutex);
        worlds.reserve(m_resources[0].size());
        for (auto* value:m_resources[0]) {
            auto const& world=static_cast<scene_resource*>(value)->m_particles;
            if (world) worlds.push_back(world);
        }
        m_accepting=false; m_material_overrides.swap(retired_materials);m_model_overrides.swap(retired_models);
    }
    // Close outside the registry lock: releasing active self-references can retire resources.
    for (auto const& world:worlds) world->close();
}
std::map<std::string,std::shared_ptr<static_model_data const>> scene_registry::model_overrides() const {std::lock_guard lock(m_mutex);return m_model_overrides;}
std::shared_ptr<static_model_data const> scene_registry::model_override(std::string const& key) const {std::lock_guard lock(m_mutex);auto found=m_model_overrides.find(key);return found==m_model_overrides.end()?nullptr:found->second;}
void scene_registry::publish_model(std::string const& key,std::shared_ptr<static_model_data const> model) {
    if(!model || model->source_path!=key || model->surfaces.empty())throw std::runtime_error("Foreign authored model source");
    auto published=std::make_shared<static_model_data>(*model);published->identity=key;
    std::map<std::string,std::shared_ptr<static_model_data const>> next;
    {std::lock_guard lock(m_mutex);if(!m_accepting)throw std::runtime_error("Closed model publication");next=m_model_overrides;}
    next.insert_or_assign(key,published);if(next.size()>1024)throw std::runtime_error("Authored model registry exceeds bounds");
    {std::lock_guard lock(m_mutex);if(!m_accepting)throw std::runtime_error("Closed model publication");m_model_overrides.swap(next);}
}
bool scene_registry::accepting() const { std::lock_guard lock(m_mutex); return m_accepting; }
std::shared_ptr<presentation_output> scene_registry::make_presentation(HWND window) const {
    if (!on_owner_thread() || !accepting() || !IsWindow(window)) throw std::runtime_error("Output lease requires its accepting cook owner and a live HWND");
    if (window==m_window) {
        if (m_output->closed || m_output->close_requested) throw std::runtime_error("Primary output has closed");
        return m_output;
    }
    auto result=std::make_shared<presentation_output>();result->hwnd=window;return result;
}
std::array<size_t,3> scene_registry::counts() const {
    std::lock_guard lock(m_mutex); return {m_resources[0].size(),m_resources[1].size(),m_resources[2].size()};
}
scene_resource::scene_resource(std::shared_ptr<scene_registry> state, scene_configuration const& config,particle::world_ptr particles)
    : m_particles(std::move(particles)),owner(std::move(state)), test_geometry(config.test_geometry), terrain_enabled(config.features.m_create_terrain),
      terrain_memory(config.terrain_memory),particle_fixed_timestep(config.particle_fixed_timestep),models(std::make_unique<scene_models>()) {
    terrain_limits(terrain_memory);
    if (!terrain_enabled && terrain_memory!=terrain_memory_profile::standard_256_mib)
        throw std::runtime_error("Extended terrain memory requires the scene terrain capability");
    if (!std::isfinite(particle_fixed_timestep) || particle_fixed_timestep<0 || particle_fixed_timestep>.25f
        || (!m_particles && particle_fixed_timestep!=0)) throw std::runtime_error("Invalid scene particle timestep profile");
    if (!owner || (m_particles && !owner->on_owner_thread()) || config.features.m_create_particle_world!=bool(m_particles)
        || (m_particles && (!m_particles->has_deleter_object() || m_particles->get_class_id()!=resources::particle_world_class || !m_particles->accepting())))
        throw std::runtime_error("Scene particle capability requires a live cooked world dependency");
    owner->add(0,this);
}
void scene_resource::close_particles() {if (m_particles) m_particles->close();}
scene_resource::~scene_resource() { close_particles();owner->remove(0,this); }
particle::world_ptr scene_resource::particle_world_handle() const {
    if (!owner->accepting() || !m_particles || !m_particles->accepting())
        throw std::runtime_error("Scene has no accepting particle world");
    return m_particles;
}
view_resource::view_resource(std::shared_ptr<scene_registry> state) : temporal_id(next_temporal_id.fetch_add(1)),owner(std::move(state)) {
    m_view.identity(); m_projection.identity(); m_culling_view.identity();
    if (!owner) throw std::runtime_error("Missing scene registry"); owner->add(1,this);
}
view_resource::~view_resource() { owner->remove(1,this); }
namespace {
void valid_matrix(math::float4x4 const& value) {
    for (unsigned r=0; r<4; ++r) for (unsigned c=0; c<4; ++c)
        if (!std::isfinite(value.elements[r][c])) throw std::runtime_error("Non-finite camera matrix");
}
}
void view_resource::set_view(math::float4x4 const& value, bool culling) {
    valid_matrix(value); std::lock_guard lock(m_mutex); m_view = value; if (culling) m_culling_view = value;
}
void view_resource::set_projection(math::float4x4 const& value) { valid_matrix(value); std::lock_guard lock(m_mutex); m_projection = value; }
std::array<math::float4x4,3> view_resource::camera() const { std::lock_guard lock(m_mutex); return {m_view,m_projection,m_culling_view}; }
void view_resource::set_mode(scene_view_mode mode) {
    if ((mode<wireframe_view_mode || mode>indirect_lighting_mode) && mode != gbuffer_packed_depth_view_mode && mode != gbuffer_tangents_view_mode
        && mode != gbuffer_hardware_depth_view_mode && mode != gbuffer_reconstructed_position_view_mode && mode != gbuffer_overview_view_mode
        && mode != gbuffer_stencil_view_mode && mode != gbuffer_roughness_view_mode && mode != gbuffer_fresnel_view_mode
        && mode != material_albedo_view_mode && mode != material_specular_view_mode)
        throw std::runtime_error("Vulkan static view mode is not implemented");
    std::lock_guard lock(m_mutex); m_mode = mode;
}
scene_view_mode view_resource::mode() const { std::lock_guard lock(m_mutex); return m_mode; }
void view_resource::set_selection(editor_selection_settings const& s) {
    if(s.version!=1 || s.size!=sizeof(s) || s.enabled>1 || s.reserved || !std::isfinite(s.rate) || s.rate<0 || s.rate>10 || !std::isfinite(s.time) || s.time<0 || s.time>1e6f || !std::isfinite(s.width) || s.width<1 || s.width>8)
        throw std::runtime_error("Invalid editor selection settings");
    std::lock_guard lock(m_mutex);m_selection=s;
}
editor_selection_settings view_resource::selection() const {std::lock_guard lock(m_mutex);return m_selection;}
void view_resource::set_environment(lighting_environment const& environment) {
    for (auto const& color : {environment.ambient,environment.fog_color}) for (float value : color)
        if (!std::isfinite(value) || value<0 || value>1) throw std::runtime_error("Environment colors require finite [0,1] channels");
    if (!std::isfinite(environment.fog_near) || environment.fog_near<0 || environment.fog_near>1e6f
        || !std::isfinite(environment.fog_distance) || environment.fog_distance<.001f || environment.fog_distance>1e6f)
        throw std::runtime_error("Invalid fog near distance or decay distance");
    std::lock_guard lock(m_mutex); m_environment=environment;
}
lighting_environment view_resource::environment() const { std::lock_guard lock(m_mutex); return m_environment; }
void view_resource::set_postprocess(postprocess_settings const& settings) {
    if(settings.ssao_pixel_radius<1 || settings.ssao_pixel_radius>32 || !std::isfinite(settings.ssao_world_radius) || settings.ssao_world_radius<.01f || settings.ssao_world_radius>128
        || !std::isfinite(settings.ssao_bias) || settings.ssao_bias<0 || settings.ssao_bias>=1 || !std::isfinite(settings.ssao_strength) || settings.ssao_strength<0 || settings.ssao_strength>4
        || !std::isfinite(settings.distortion_time) || settings.distortion_time<0 || settings.distortion_time>3600 || !std::isfinite(settings.distortion_max_pixels) || settings.distortion_max_pixels<0 || settings.distortion_max_pixels>64)
        throw std::runtime_error("SSAO/distortion parameters exceed the bounded profile");
    if(settings.enabled && (settings.ssao_enabled || settings.distortion_enabled) && (settings.msaa_samples!=1 || settings.fxaa_enabled || settings.taa_enabled))
        throw std::runtime_error("SSAO/distortion require 1x with post-AA off");
    if ((settings.msaa_samples!=1 && settings.msaa_samples!=2 && settings.msaa_samples!=4 && settings.msaa_samples!=8)
        || !std::isfinite(settings.taa_history_weight) || settings.taa_history_weight<0 || settings.taa_history_weight>.98f
        || (settings.fxaa_enabled && settings.taa_enabled))
        throw std::runtime_error("AA requires MSAA 1/2/4/8, TAA history weight [0,.98], and either TAA or FXAA");
    if (!std::isfinite(settings.exposure_ev) || settings.exposure_ev < -16 || settings.exposure_ev > 16)
        throw std::runtime_error("Postprocess exposure requires finite EV in [-16,16]");
    if (!std::isfinite(settings.bloom_threshold) || settings.bloom_threshold<0 || settings.bloom_threshold>65504
        || !std::isfinite(settings.bloom_strength) || settings.bloom_strength<0 || settings.bloom_strength>4
        || !std::isfinite(settings.bloom_radius) || settings.bloom_radius<1 || settings.bloom_radius>16)
        throw std::runtime_error("Bloom requires finite threshold [0,65504], strength [0,4] and radius [1,16] pixels");
    std::lock_guard lock(m_mutex); m_postprocess=settings;
}
void view_resource::enable_postprocess(bool enabled) { std::lock_guard lock(m_mutex); m_postprocess.enabled=enabled; }
postprocess_settings view_resource::postprocess() const { std::lock_guard lock(m_mutex); return m_postprocess; }
output_resource::output_resource(std::shared_ptr<scene_registry> state, HWND window) : m_window(window),temporal_id(next_temporal_id.fetch_add(1)),owner(std::move(state)) {
    if (!owner) throw std::runtime_error("Missing output registry");
    m_presentation=owner->make_presentation(window);owner->add(2,this,window);
}
output_resource::~output_resource() {
    if (m_window!=owner->window()) m_presentation->close_requested=true;
    owner->remove(2,this);
}
bool output_resource::closed() const {return m_presentation->closed.load();}
void output_resource::request_close() {
    if (!owner->on_owner_thread() || !owner->accepting() || m_presentation->closed)
        throw std::runtime_error("Output close requires its live renderer owner");
    m_presentation->close_requested=true;
}
math::uint2 output_resource::client_size() const {
    RECT rect = {};
    if (!owner->accepting() || m_presentation->closed || m_presentation->close_requested
        || !IsWindow(m_window) || IsIconic(GetAncestor(m_window,GA_ROOT)) || !GetClientRect(m_window,&rect)) return math::uint2(0,0);
    return math::uint2(u32(rect.right-rect.left),u32(rect.bottom-rect.top));
}
namespace {
class scene_resource_cook final : public resources::translate_query_cook {
    std::shared_ptr<scene_registry> m_owner;
    memory::base_allocator* m_allocator=render::g_allocator;
    void finish(resources::query_result_for_cook& query,scene_configuration const& config,particle::world_ptr world={}) {
        try {
            if (!m_owner->accepting()) throw std::runtime_error("Scene cook session is closed");
            auto* resource=XRAY_NEW_IMPL(*m_allocator,scene_resource)(m_owner,config,world);
            query.set_unmanaged_resource(resource,resources::nocache_memory,sizeof(*resource));
            query.finish_query(result_success);
        } catch (...) {if (world) world->close();throw;}
    }
    static void failed(resources::query_result_for_cook& query,std::exception const& error) {
        diagnostic("error","Vulkan scene query failed: %s",error.what());query.finish_query(result_error);
    }
public:
    explicit scene_resource_cook(std::shared_ptr<scene_registry> owner)
        : translate_query_cook(resources::scene_class,reuse_false,use_current_thread_id),m_owner(std::move(owner)) {}
    bool cache_by_game_resources_manager() override {return false;}
    void translate_query(resources::query_result_for_cook& query) override {
        try {
            if (!m_owner->accepting()) throw std::runtime_error("Scene cook session is closed");
            auto bytes=query.creation_data_from_user();
            if (bytes.size()!=1 || !bytes.c_ptr() || *static_cast<u8 const*>(bytes.c_ptr())!=0)
                throw std::runtime_error("Scene requires internal zero-byte creation request");
            scene_configuration config;
            if (auto* data=query.user_data()) {
                if (auto* value=data->get_if<scene_configuration>()) config=*value;
                else if (auto* value=data->get_if<editor_renderer_configuration>()) config.features=*value;
                else throw std::runtime_error("Unknown scene configuration user data");
            }
            terrain_limits(config.terrain_memory);
            if (!config.features.m_create_terrain && config.terrain_memory!=terrain_memory_profile::standard_256_mib)
                throw std::runtime_error("Extended terrain memory requires the scene terrain capability");
            if (!std::isfinite(config.particle_fixed_timestep) || config.particle_fixed_timestep<0 || config.particle_fixed_timestep>.25f
                || (!config.features.m_create_particle_world && config.particle_fixed_timestep!=0))
                throw std::runtime_error("Invalid scene particle timestep profile");
            if (!config.features.m_create_particle_world) {finish(query,config);return;}
            if (!m_owner->on_owner_thread()) throw std::runtime_error("Particle scene creation requires the scene session owner thread");
            for (auto id:{resources::particle_world_class,resources::particle_system_class,resources::particle_system_instance_class})
                if (!resources::cook_base::find_cook(id)) throw std::runtime_error("Scene requires registered production particle cooks");
            auto data=std::make_shared<resources::user_data_variant>();
            data->set(particle::world_configuration{make_scene_particle_adapter(m_owner),50000});
            resources::user_data_variant const* user=data.get();
            resources::creation_request request("scene_particle_world",const_buffer("",1),resources::particle_world_class);
            resources::query_create_resources(&request,1,[this,&query,config,data](resources::queries_result& result) {
                try {
                    if (!result.is_successful()) {query.finish_query(result_error);return;}
                    auto* value=dynamic_cast<particle::world*>(result[0].get_unmanaged_resource().c_ptr());
                    if (!value || !value->has_deleter_object() || value->get_class_id()!=resources::particle_world_class)
                        throw std::runtime_error("Invalid scene particle world dependency");
                    finish(query,config,value);
                } catch (std::exception const& error) {failed(query,error);}
            },m_allocator,&user,&query,assert_on_fail_false);
        } catch (std::exception const& error) {failed(query,error);}
    }
    void delete_resource(resources::resource_base* value) override {
        auto* resource=static_cast<scene_resource*>(value);XRAY_DELETE_IMPL(*m_allocator,resource);
    }
};
template<typename T> class scene_cook final : public resources::unmanaged_cook {
    std::shared_ptr<scene_registry> m_owner;
    memory::base_allocator* m_allocator;
public:
    scene_cook(resources::class_id_enum id, std::shared_ptr<scene_registry> owner)
        : unmanaged_cook(id,reuse_false,use_current_thread_id,use_current_thread_id), m_owner(std::move(owner)), m_allocator(render::g_allocator) {}
    bool cache_by_game_resources_manager() override { return false; }
    mutable_buffer allocate_resource(resources::query_result_for_cook&, const_buffer, bool) override {
        auto* data = XRAY_ALLOC_IMPL(*m_allocator,u8,sizeof(T)); return mutable_buffer(data,sizeof(T));
    }
    void deallocate_resource(pvoid data) override { XRAY_FREE_IMPL(*m_allocator,data); }
    void create_resource(resources::query_result_for_cook& query, const_buffer source, mutable_buffer buffer) override {
        try {
            T* result = nullptr;
            if constexpr (std::is_same_v<T,output_resource>) {
                HWND window = nullptr;
                auto* value = query.user_data() ? query.user_data()->get_if<HWND>() : nullptr;
                if (!value) throw std::runtime_error("Output requires HWND user data");
                window = *value;
                result = new(buffer.c_ptr()) T(m_owner,window);
            } else result = new(buffer.c_ptr()) T(m_owner);
            query.set_unmanaged_resource(result,resources::nocache_memory,sizeof(T));
            query.finish_query(result_success);
        } catch (std::exception const& e) { diagnostic("error","Vulkan resource query failed: %s",e.what()); query.finish_query(result_error); }
    }
    void destroy_resource(resources::unmanaged_resource* resource) override { static_cast<T*>(resource)->~T(); }
};
struct cook_group {
    u32 const thread_id = threading::current_thread_id();
    std::shared_ptr<scene_registry> owner;
    scene_resource_cook scene;
    scene_cook<view_resource> view;
    scene_cook<output_resource> output;
    static_model_cook model, model_asset, skeletal_model, skeletal_asset;
    skeleton_model_cook skeletal_wrapper;
    vegetation_cook tree,tree_instance;
    terrain_cook terrain_mesh,terrain_instance,terrain_wrapper;
    texture_cook texture;
    material_cook material;
    particle_material_cook particle_material;
    explicit cook_group(HWND window, std::shared_ptr<presentation_output> output_state) : owner(std::make_shared<scene_registry>(window,std::move(output_state))),
        scene(owner),view(resources::scene_view_class,owner),output(resources::render_output_window_class,owner),
        model(true,owner),model_asset(false,owner),skeletal_model(true,owner,true),skeletal_asset(false,owner,true),skeletal_wrapper(owner),
        tree(false,owner),tree_instance(true,owner),terrain_mesh(resources::terrain_mesh_class,owner),terrain_instance(resources::terrain_instance_class,owner),terrain_wrapper(resources::terrain_model_class,owner),texture(owner),material(owner),particle_material(owner) {}
};
std::unique_ptr<cook_group> cooks;
}
bool finalize_scene_resources() {
    if (!cooks) return true;
    if (threading::current_thread_id() != cooks->thread_id || !resources::is_initialized() || cooks->owner->accepting() || resources::pending_queries_count()
        || cooks->scene.cook_users_count() || cooks->view.cook_users_count() || cooks->output.cook_users_count() || cooks->model.cook_users_count()
        || cooks->model_asset.cook_users_count() || cooks->skeletal_model.cook_users_count() || cooks->skeletal_asset.cook_users_count()
        || cooks->skeletal_wrapper.cook_users_count() || cooks->tree.cook_users_count() || cooks->tree_instance.cook_users_count()
        || cooks->terrain_mesh.cook_users_count() || cooks->terrain_instance.cook_users_count() || cooks->terrain_wrapper.cook_users_count()
        || cooks->texture.cook_users_count() || cooks->material.cook_users_count() || cooks->particle_material.cook_users_count()) return false;
    if (resources::cook_base::find_cook(resources::scene_class) != &cooks->scene
        || resources::cook_base::find_cook(resources::scene_view_class) != &cooks->view
        || resources::cook_base::find_cook(resources::render_output_window_class) != &cooks->output
        || resources::cook_base::find_cook(resources::static_render_model_instance_class) != &cooks->model
        || resources::cook_base::find_cook(resources::static_render_model_class) != &cooks->model_asset
        || resources::cook_base::find_cook(resources::skeleton_render_model_instance_class) != &cooks->skeletal_model
        || resources::cook_base::find_cook(resources::skeleton_render_model_class) != &cooks->skeletal_asset
        || resources::cook_base::find_cook(resources::skeleton_model_instance_class) != &cooks->skeletal_wrapper
        || resources::cook_base::find_cook(resources::speedtree_class) != &cooks->tree
        || resources::cook_base::find_cook(resources::speedtree_instance_class) != &cooks->tree_instance
        || resources::cook_base::find_cook(resources::terrain_mesh_class) != &cooks->terrain_mesh
        || resources::cook_base::find_cook(resources::terrain_instance_class) != &cooks->terrain_instance
        || resources::cook_base::find_cook(resources::terrain_model_class) != &cooks->terrain_wrapper
        || resources::cook_base::find_cook(resources::texture_class) != &cooks->texture
        || resources::cook_base::find_cook(resources::material_class) != &cooks->material
        || resources::cook_base::find_cook(resources::material_effects_instance_class) != &cooks->particle_material) return false;
    resources::unregister_cook(resources::scene_class); resources::unregister_cook(resources::scene_view_class);
    resources::unregister_cook(resources::render_output_window_class);
    resources::unregister_cook(resources::static_render_model_instance_class);
    resources::unregister_cook(resources::static_render_model_class);
    resources::unregister_cook(resources::skeleton_render_model_instance_class);
    resources::unregister_cook(resources::skeleton_render_model_class);
    resources::unregister_cook(resources::skeleton_model_instance_class);
    resources::unregister_cook(resources::speedtree_class); resources::unregister_cook(resources::speedtree_instance_class);
    resources::unregister_cook(resources::terrain_mesh_class); resources::unregister_cook(resources::terrain_instance_class); resources::unregister_cook(resources::terrain_model_class);
    resources::unregister_cook(resources::texture_class); resources::unregister_cook(resources::material_class);
    resources::unregister_cook(resources::material_effects_instance_class); cooks.reset(); return true;
}
std::shared_ptr<scene_registry> register_scene_resources(HWND window, std::shared_ptr<presentation_output> output_state) {
    if (!resources::is_initialized() || !render::g_allocator || !window || !IsWindow(window)) throw std::runtime_error("Scene cook registration requires resources, renderer allocator and a valid HWND");
    if (!finalize_scene_resources()) throw std::runtime_error("Previous scene resources/queries must drain before reinitialization");
    for (auto id : {resources::scene_class,resources::scene_view_class,resources::render_output_window_class,resources::static_render_model_instance_class,
        resources::static_render_model_class,resources::skeleton_render_model_instance_class,resources::skeleton_render_model_class,resources::skeleton_model_instance_class,
        resources::speedtree_class,resources::speedtree_instance_class,resources::terrain_mesh_class,resources::terrain_instance_class,resources::terrain_model_class,resources::texture_class,resources::material_class,resources::material_effects_instance_class})
        if (resources::cook_base::find_cook(id)) throw std::runtime_error("Scene cook ID already owned by another renderer");
    auto group = std::make_unique<cook_group>(window,std::move(output_state));
    resources::register_cook(&group->scene); resources::register_cook(&group->view); resources::register_cook(&group->output);
    resources::register_cook(&group->model);
    resources::register_cook(&group->model_asset);
    resources::register_cook(&group->skeletal_model); resources::register_cook(&group->skeletal_asset);
    resources::register_cook(&group->skeletal_wrapper);
    resources::register_cook(&group->tree); resources::register_cook(&group->tree_instance);
    resources::register_cook(&group->terrain_mesh); resources::register_cook(&group->terrain_instance); resources::register_cook(&group->terrain_wrapper);
    resources::register_cook(&group->texture); resources::register_cook(&group->material);
    resources::register_cook(&group->particle_material);
    auto result = group->owner; cooks = std::move(group); return result;
}
} } }
