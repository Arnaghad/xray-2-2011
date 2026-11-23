#ifndef XRAY_VULKAN_SCENE_RUNTIME_H_INCLUDED
#define XRAY_VULKAN_SCENE_RUNTIME_H_INCLUDED
#include <xray/render/vulkan/scene_resources.h>
#include "vulkan_smoke_materials.h"
#include "vulkan_static_renderer.h"
#include "vulkan_gbuffer_renderer.h"
#include "vulkan_taa.h"
#include "vulkan_debug_renderer.h"
#include "vulkan_ui_renderer.h"
#include "vulkan_terrain_renderer.h"
#include "vulkan_particle_renderer.h"
#include "vulkan_selection_renderer.h"
#include <xray/render/facade/editor_scene_edits.h>
#include <chrono>
#include <vector>
#include <map>

namespace xray { namespace render { namespace vulkan {
using viewport_type = math::rectangle<math::float2>;
class terrain_binding_runtime;
class scene_runtime {
    struct queued_draw {
        scene_ptr scene;
        scene_view_ptr view;
        render_output_window_ptr output;
        frame_constants camera;
        viewport_type viewport;
        std::vector<model_draw> models;
        scene_view_mode mode = unlit_view_mode;
        gbuffer_camera_data gbuffer_camera;
        std::vector<scene_light> lights;
        lighting_environment environment;
        std::vector<model_draw> casters;
        std::vector<scene_shadow_plan> shadows;
        sky_draw sky;
        std::vector<model_draw> transparent;
        postprocess_settings postprocess;
        u64 temporal_signature=0;
        std::vector<std::shared_ptr<debug_batch const>> debug;
        std::vector<std::shared_ptr<ui_batch const>> ui;
        std::vector<terrain_draw> terrain;
        std::vector<std::shared_ptr<particle_billboard_batch const>> particles;
        std::vector<decal_draw> decals;
        std::vector<model_draw> distortions;
        std::vector<model_draw> selected;
        editor_selection_settings selection;
    };
    std::shared_ptr<scene_registry> m_owner;
    std::shared_ptr<terrain_binding_runtime> m_terrain_commands;
    std::vector<queued_draw> m_draws;
    std::unique_ptr<smoke_materials> m_geometry;
    std::unique_ptr<static_renderer> m_models;
    struct output_renderers {
        std::shared_ptr<presentation_output> output;
        std::unique_ptr<gbuffer_renderer> gbuffer;
        std::unique_ptr<scene_shadows> shadows;
        std::unique_ptr<taa_renderer> taa;
        std::unique_ptr<selection_renderer> selection;
        bool touched=false,used_gbuffer=false,used_shadows=false,used_taa=false;
        u32 presented=0;
    };
    std::map<presentation_output*,output_renderers> m_outputs;
    output_renderers& renderers(std::shared_ptr<presentation_output> const&);
    bool m_used_debug=false,m_used_ui=false,m_used_particles=false;
    std::unique_ptr<debug_renderer> m_debug;
    std::vector<std::shared_ptr<debug_batch const>>* m_debug_capture=nullptr;
    scene_ptr m_debug_scene;
    bool m_debug_depth=true;
    size_t m_debug_bytes=0,m_debug_batches=0;
    std::unique_ptr<ui_renderer> m_ui;
    std::unique_ptr<terrain_renderer> m_terrain;
    std::unique_ptr<particle_renderer> m_particles;
    std::vector<std::shared_ptr<particle_billboard_batch const>> m_particle_frame;
    std::vector<particle::world_ptr> m_particle_ticked;
    std::chrono::steady_clock::time_point m_particle_clock;
    bool m_particle_clock_started=false,m_particle_frame_open=false;
    float m_particle_delta=0;
    size_t m_tree_frame_bytes=0;
    void prepare_trees(scene_resource&,math::float4x4 const&);
    void clear_particle_frame();
    float particle_frame_delta();
    std::chrono::steady_clock::time_point m_material_clock = std::chrono::steady_clock::now();
    float material_phase() const {
        return std::fmod(std::chrono::duration<float>(std::chrono::steady_clock::now() - m_material_clock).count(), 86400.f);
    }
    std::vector<std::shared_ptr<ui_batch const>>* m_ui_capture=nullptr;
    scene_view_ptr m_ui_view;
    size_t m_ui_bytes=0,m_ui_batches=0;
public:
    explicit scene_runtime(HWND,std::shared_ptr<presentation_output>);
    ~scene_runtime();
    view_resource& view(scene_view_ptr const&) const;
    output_resource& output(render_output_window_ptr const&) const;
    scene_resource& scene(scene_ptr const&) const;
    void decal_add(scene_ptr const&,u32,decal_properties const&);
    void decal_update(scene_ptr const&,u32,decal_properties const&);
    void decal_remove(scene_ptr const&,u32);
    void add_tree(scene_ptr const&,speedtree_instance_ptr const&,math::float4x4 const&);
    void update_tree(scene_ptr const&,speedtree_instance_ptr const&,math::float4x4 const&);
    void remove_tree(scene_ptr const&,speedtree_instance_ptr const&);
    void populate_trees(scene_ptr const&);
    void tree_material(speedtree_instance_ptr const&,fs_new::virtual_path_string const&,resources::unmanaged_resource_ptr const&);
    void tree_visible(speedtree_instance_ptr const&,bool);
    static_model_resource& model(render_model_instance_ptr const&) const;
    terrain_instance_resource& terrain_model(render_model_instance_ptr const&) const;
    void terrain_add(scene_ptr const&,render_model_instance_ptr const&);
    void terrain_remove(scene_ptr const&,render_model_instance_ptr const&);
    void terrain_update(scene_ptr const&,render_model_instance_ptr const&,vectora<terrain_buffer_fragment> const&,math::float4x4 const&);
    void terrain_update_bounds(scene_ptr const&,render_model_instance_ptr const&,math::aabb const&);
    void terrain_add_texture(scene_ptr const&,render_model_instance_ptr const&,fs_new::virtual_path_string const&,u32);
    void terrain_remove_texture(scene_ptr const&,render_model_instance_ptr const&,u32);
    void terrain_exchange_texture(scene_ptr const&,fs_new::virtual_path_string const&,fs_new::virtual_path_string const&);
    void add_model(scene_ptr const&, render_model_instance_ptr const&, math::float4x4 const&, bool apply_transform = true);
    void update_model(scene_ptr const&, render_model_instance_ptr const&, math::float4x4 const&);
    void update_model_buffer(render_model_instance_ptr const&, vectora<buffer_fragment> const&, bool indices);
    void update_skeleton(render_model_instance_ptr const&,math::float4x4 const*,u32);
    void remove_model(scene_ptr const&, render_model_instance_ptr const&);
    void select_model(scene_ptr const&,render_model_instance_ptr const&,bool);
    void apply_model_edits(scene_ptr const&,std::vector<model_scene_edit> const&);
    void set_visible(render_model_instance_ptr const&, fs_new::virtual_path_string const&, bool);
    void set_material(render_model_instance_ptr const&, fs_new::virtual_path_string const&, resources::unmanaged_resource_ptr const&);
    void set_sky_material(scene_ptr const&,resources::unmanaged_resource_ptr const&);
    void add_light(scene_ptr const&,u32,light_props const&);
    void update_light(scene_ptr const&,u32,light_props const&);
    void remove_light(scene_ptr const&,u32);
    void debug(scene_ptr const&,std::span<vertex_colored const>,std::span<u16 const>,bool triangles);
    void ui(scene_view_ptr const&,std::span<render::ui::vertex const>,int,int,render::ui::draw_options const&);
    void ui(std::span<render::ui::vertex const> vertices,int primitives,int points) { ui(m_ui_view,vertices,primitives,points,{}); }
    void set_font_texture(scene_view_ptr const&,resources::unmanaged_resource_ptr const&);
    void queue(scene_ptr const&, scene_view_ptr const&, render_output_window_ptr const&,
        viewport_type const&, std::function<void(bool)> const&);
    std::vector<std::shared_ptr<presentation_output>> queued_outputs() const;
    void begin_draw_frame();
    void draw(rhi::rhi_backend&,u32,std::shared_ptr<presentation_output> const&);
    void suspend(std::shared_ptr<presentation_output> const&);
    void finish_draw_frame();
    void discard();
    void reload_modified_textures() { m_owner->reload_modified_textures(); }
    void validate(scene_view_ptr const&, render_output_window_ptr const&, viewport_type const&) const;
};
} } }
#endif
