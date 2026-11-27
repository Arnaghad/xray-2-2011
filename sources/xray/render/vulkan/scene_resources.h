#ifndef XRAY_VULKAN_SCENE_RESOURCES_H_INCLUDED
#define XRAY_VULKAN_SCENE_RESOURCES_H_INCLUDED
#include <xray/extensions.h>
#include <xray/platform_surface.h>
#include <xray/render/engine/base_classes.h>
#include <xray/render/facade/common_types.h>
#include <xray/render/vulkan/library_linkage.h>
#include <xray/render/vulkan/diagnostics.h>
#include <xray/render/facade/scene_view_mode.h>
#include <xray/render/facade/editor_selection.h>
#include <xray/render/facade/lighting_environment.h>
#include <xray/render/facade/postprocess_settings.h>
#include <xray/render/facade/terrain_memory_profile.h>
#include <xray/particle/world_resources.h>
#include <memory>
#include <mutex>
#include <map>
#include <set>
#include <unordered_set>
#include <array>
#include <vector>

namespace xray { namespace render {
struct light_props;
namespace vulkan {
enum class scene_light_type { directional, point, spot };
struct scene_light {
    u32 id=0;
    scene_light_type type=scene_light_type::directional;
    std::array<float,3> direction={0,0,1},color={1,1,1};
    std::array<float,3> position={};
    float intensity=1,diffuse=1,specular=1;
    float range=1,attenuation_power=1,inner_cosine=1,outer_cosine=0,spot_falloff=1,spot_outer_angle=1;
    bool casts_shadows=false;
    u32 shadow_resolution=512,shadow_cascades=4,shadow_face_mask=0;
    float shadow_transparency=0,shadow_depth_bias=.0001f;
};
XRAY_RENDER_VULKAN_API scene_light describe_light(u32,light_props const&);
struct scene_configuration {
    editor_renderer_configuration features;
    bool test_geometry = false;
    // 0 uses bounded wall time; (0,.25] is an explicit deterministic step.
    float particle_fixed_timestep = 0;
    terrain_memory_profile terrain_memory=terrain_memory_profile::standard_256_mib;
};
class scene_resource;
class view_resource;
class output_resource;
struct presentation_output;
struct scene_models;
struct texture_state;
struct texture_data;
struct terrain_binding_statistics {
    // Accepted binding commands and buffer/AABB commands deferred behind them, not all terrain edits.
    u64 requested=0,committed=0,failed=0,cancelled=0,pending=0,queries=0,in_flight=0;
    size_t queued_bytes=0;
};
struct static_model_data;
class XRAY_RENDER_VULKAN_API scene_registry : public std::enable_shared_from_this<scene_registry> {
    unsigned reload_textures(std::string const* virtual_path,std::string const* replacement_path);
    mutable std::mutex m_mutex;
    u32 const m_thread;
    bool m_accepting = true;
    platform::native_surface_handle m_window;
    std::shared_ptr<presentation_output> m_output;
    std::unordered_set<void*> m_resources[3];
    std::vector<std::weak_ptr<texture_state>> m_textures;
    std::map<std::string,std::string> m_texture_sources;
    texture_reload_statistics m_texture_reload;
    std::set<std::string> m_material_keys;
    std::map<std::string,resources::unmanaged_resource_ptr> m_material_overrides;
    std::map<std::string,std::shared_ptr<static_model_data const>> m_model_overrides;
    terrain_binding_statistics m_terrain_bindings;
    friend class terrain_binding_runtime;
    void begin_terrain_command(size_t);
    void end_terrain_command(unsigned,size_t);
    void begin_terrain_query();
    void end_terrain_query();
    void finish_texture_reload(std::weak_ptr<texture_state> const&,std::shared_ptr<texture_data const> const&);
    friend class terrain_instance_resource;
    std::vector<std::shared_ptr<texture_data const>> terrain_texture_snapshots(std::vector<std::shared_ptr<texture_state>> const&) const;
public:
    explicit scene_registry(platform::native_surface_handle window, std::shared_ptr<presentation_output> output = {});
    void add(unsigned kind, void* resource, platform::native_surface_handle window = {});
    void remove(unsigned kind, void* resource) noexcept;
    void close();
    bool accepting() const;
    bool on_owner_thread() const {return m_thread==threading::current_thread_id();}
    platform::native_surface_handle window() const { return m_window; }
    std::shared_ptr<presentation_output> presentation() const { return m_output; }
    std::shared_ptr<presentation_output> make_presentation(platform::native_surface_handle) const;
    std::array<size_t,3> counts() const;
    void track_texture(std::shared_ptr<texture_state> const&);
    void track_material(std::string const&);
    std::map<std::string,std::shared_ptr<static_model_data const>> model_overrides() const;
    std::shared_ptr<static_model_data const> model_override(std::string const&) const;
    void publish_model(std::string const&,std::shared_ptr<static_model_data const>);
    std::vector<std::string> material_keys() const;
    std::map<std::string,resources::unmanaged_resource_ptr> material_overrides() const;
    resources::unmanaged_resource_ptr material_override(std::string const&) const;
    void publish_materials(std::map<std::string,resources::unmanaged_resource_ptr> const&);
    void reload_modified_textures();
    unsigned publish_texture_source(std::string const& virtual_path,std::string const& physical_path);
    std::string texture_source(std::string const& virtual_path) const;
    texture_reload_statistics texture_reload_status() const;
    terrain_binding_statistics terrain_binding_status() const;
};
class XRAY_RENDER_VULKAN_API scene_resource final : public base_scene {
    particle::world_ptr const m_particles;
    friend class scene_registry;
    void close_particles();
public:
    std::shared_ptr<scene_registry> const owner;
    bool const test_geometry;
    bool const terrain_enabled;
    terrain_memory_profile const terrain_memory;
    float const particle_fixed_timestep;
    std::unique_ptr<scene_models> models;
    u64 temporal_revision=1;
    std::vector<scene_light> lights;
    resources::unmanaged_resource_ptr sky_material;
    scene_resource(std::shared_ptr<scene_registry>, scene_configuration const&, particle::world_ptr = {});
    // Copies ownership only; simulation and instance mutations remain on the cook thread.
    particle::world_ptr particle_world_handle() const;
    bool particles_enabled() const {return bool(m_particles);}
    ~scene_resource() override;
};
class XRAY_RENDER_VULKAN_API view_resource final : public base_scene_view {
    mutable std::mutex m_mutex;
    math::float4x4 m_view, m_projection, m_culling_view;
    scene_view_mode m_mode = unlit_view_mode;
    lighting_environment m_environment;
    postprocess_settings m_postprocess;
    editor_selection_settings m_selection;
public:
    u64 const temporal_id;
    std::shared_ptr<scene_registry> const owner;
    explicit view_resource(std::shared_ptr<scene_registry>);
    // Render-thread binding; queued UI batches retain immutable texture snapshots.
    resources::unmanaged_resource_ptr ui_font_texture;
    ~view_resource() override;
    void set_view(math::float4x4 const&, bool culling = true);
    void set_projection(math::float4x4 const&);
    std::array<math::float4x4,3> camera() const;
    void set_mode(scene_view_mode);
    scene_view_mode mode() const;
    void set_environment(lighting_environment const&);
    lighting_environment environment() const;
    void set_postprocess(postprocess_settings const&);
    void enable_postprocess(bool);
    postprocess_settings postprocess() const;
    void set_selection(editor_selection_settings const&);
    editor_selection_settings selection() const;
};
class XRAY_RENDER_VULKAN_API output_resource final : public base_output_window {
    platform::native_surface_handle m_window;
    std::shared_ptr<presentation_output> m_presentation;
public:
    u64 const temporal_id;
    std::shared_ptr<scene_registry> const owner;
    output_resource(std::shared_ptr<scene_registry>, platform::native_surface_handle);
    ~output_resource() override;
    platform::native_surface_handle window() const { return m_window; }
    std::shared_ptr<presentation_output> presentation() const { return m_presentation; }
    void request_close();
    bool closed() const;
    math::uint2 client_size() const;
};
// Registration and finalization run on the renderer's resource-dispatch thread.
XRAY_RENDER_VULKAN_API std::shared_ptr<scene_registry> register_scene_resources(platform::native_surface_handle, std::shared_ptr<presentation_output> output = {});
XRAY_RENDER_VULKAN_API bool finalize_scene_resources();
} } }
#endif
