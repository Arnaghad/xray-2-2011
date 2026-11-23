#include <stdexcept>
#include <algorithm>
#include "pch.h"
#include <xray/render/engine/world_dx11.h>
#include <xray/render/core/sources/rhi/rhi_device.h>
#include "vulkan_backend.h"
#include "vulkan_diagnostics.h"
#include "vulkan_smoke_geometry.h"
#include "vulkan_smoke_materials.h"
#include "vulkan_scene_runtime.h"
#include <xray/render/vulkan/particle_resources.h>
#include <xray/resources.h>
#include <xray/particle/api.h>

namespace xray { namespace render { namespace engine {
void world::select_model(scene_ptr const& scene,render_model_instance_ptr const& model,bool selected) {
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->select_model(scene,model,selected);
}
void world::set_editor_selection(scene_view_ptr const& view,editor_selection_settings const& settings) {
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->view(view).set_selection(settings);
}
void world::apply_model_edits(scene_ptr const& scene,std::vector<model_scene_edit> const& edits){if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->apply_model_edits(scene,edits);}
void world::add_decal(scene_ptr const& scene,u32 id,decal_properties const& p){
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->decal_add(scene,id,p);}
void world::update_decal(scene_ptr const& scene,u32 id,decal_properties const& p){
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->decal_update(scene,id,p);}
void world::remove_decal(scene_ptr const& scene,u32 id){
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->decal_remove(scene,id);}
void world::add_speedtree_instance(scene_ptr const& scene,speedtree_instance_ptr const& value,float4x4 const& matrix,bool) {
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->add_tree(scene,value,matrix);
}
void world::update_speedtree_instance(scene_ptr const& scene,speedtree_instance_ptr const& value,float4x4 const& matrix,bool) {
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->update_tree(scene,value,matrix);
}
void world::remove_speedtree_instance(scene_ptr const& scene,speedtree_instance_ptr const& value,bool) {
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->remove_tree(scene,value);
}
void world::populate_speedtree_forest(scene_ptr const& scene) {
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->populate_trees(scene);
}
void world::set_speedtree_instance_material(speedtree_instance_ptr const& value,fs_new::virtual_path_string const& surface,resources::unmanaged_resource_ptr material) {
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->tree_material(value,surface,material);
}
void world::set_speedtree_instance_visible(speedtree_instance_ptr const& value,bool visible) {
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene runtime unavailable");m_scene_runtime->tree_visible(value,visible);
}

world::world()
    : m_renderer(nullptr), m_frame_id(0), m_initialized(false), m_enable_terrain_debug_mode(false) {}

world::~world() { clear_resources(); }

bool world::initialize_game(HWND window_handle) {
    if (m_game_resources || !resources::is_initialized() || !initialize(window_handle)) return false;
    try {
        particle::initialize();
        m_game_resources = true;
        return true;
    } catch (std::exception const& e) {
        vulkan::diagnostic("error", "Game particle lifecycle initialization failed: %s", e.what());
        clear_resources();
        return false;
    }
}

bool world::finalize_game_resources() {
    if (!m_game_resources) return true;
    // clear_resources closes worlds before GPU teardown; dependencies can still
    // be retained by logic callbacks. Never unregister their cooks prematurely.
    if (m_initialized || m_scene_runtime || !vulkan::finalize_scene_resources() || !particle::try_finalize()) return false;
    m_game_resources = false;
    return true;
}

bool world::initialize(HWND window_handle) {
    if (m_initialized || !window_handle || !IsWindow(window_handle)) return false;
    auto& device = rhi::rhi_device::ref();
    if (!device.initialize(std::make_unique<vulkan::vulkan_backend>(), window_handle)) return false;
    try {
        m_primary_output=dynamic_cast<vulkan::vulkan_backend*>(device.get_backend())->output();
        if (resources::is_initialized()) {
            auto* backend = dynamic_cast<vulkan::vulkan_backend*>(device.get_backend());
            m_scene_runtime = std::make_unique<vulkan::scene_runtime>(window_handle,backend->output());
        }
    } catch (std::exception const& e) {
        vulkan::diagnostic("error", "Scene lifecycle initialization failed: %s", e.what());
        device.shutdown(); return false;
    }
    m_window_handle = window_handle;
    m_initialized = true;
    m_frame_id = m_presented_frame_id = 0;
    return true;
}

void world::clear_resources() {
    m_scene_runtime.reset();
    m_smoke_materials.reset();
    m_smoke_geometry.reset();
    rhi::rhi_device::ref().shutdown();
    // clear_resources may precede logic/module teardown. A subsequent clear
    // (including the destructor) unregisters cooks once their users have drained.
    vulkan::finalize_scene_resources();
    m_initialized = false;
    m_window_handle = nullptr;
    m_primary_output.reset();
    m_output_close_requested=false;
    finalize_game_resources();
}

void world::end_frame() {
    if (!m_initialized) throw std::runtime_error("Vulkan world is not initialized");
    auto& device = rhi::rhi_device::ref();
    auto* backend=dynamic_cast<vulkan::vulkan_backend*>(device.get_backend());
    if (!backend) {clear_resources();throw std::runtime_error("Vulkan backend is unavailable");}
    bool any_presented=false;
    try {
        // Retire leases even when no scene targets this CPU frame. HWND owners
        // may destroy their windows once the closed flag becomes visible.
        bool closed_output=m_output_close_requested;m_output_close_requested=false;
        for(auto const& output:backend->outputs()) if(output->close_requested){backend->close_output(output);closed_output=true;}
        auto destinations=m_scene_runtime ? m_scene_runtime->queued_outputs() : std::vector<std::shared_ptr<vulkan::presentation_output>>{};
        if(destinations.empty() && !closed_output && m_primary_output && !m_primary_output->closed && !m_primary_output->close_requested)
            destinations.push_back(m_primary_output);
        if(m_scene_runtime)m_scene_runtime->begin_draw_frame();
        for(auto const& output:destinations) {
            backend->adopt_output(output);backend->select_output(output);
            if(m_smoke_geometry || m_smoke_materials || m_scene_runtime)backend->clear_render_target(rhi::format::r8g8b8a8_unorm,.025f,.025f,.04f,1.f);
            else backend->clear_render_target(rhi::format::r8g8b8a8_unorm,.035f,.24f,.13f,1.f);
            bool started=device.begin_frame();
            if(!device.get_backend())throw std::runtime_error("Vulkan frame begin failed");
            auto presented=u32(output->presented_frames);
            if(started && m_smoke_geometry)m_smoke_geometry->draw(presented);
            if(started && m_smoke_materials)m_smoke_materials->draw(presented);
            if(m_scene_runtime) {
                if(started)m_scene_runtime->draw(*backend,presented,output);
                else m_scene_runtime->suspend(output);
            }
            if(started) {
                if(!device.end_frame() || !device.present())throw std::runtime_error("Vulkan frame submit/present failed");
                any_presented=true;
            }
        }
        if(m_scene_runtime)m_scene_runtime->finish_draw_frame();
        // Preserve the existing main-view capture API while it remains open.
        if(m_primary_output && !m_primary_output->closed)backend->select_output(m_primary_output);
    } catch(std::exception const& e) {
        vulkan::diagnostic("error", "Render frame failed: %s", e.what());
        clear_resources();
        throw;
    } catch(...) {
        clear_resources();
        throw;
    }
    // Retire CPU command frames even while minimized, so logic never waits
    // forever for a GPU presentation that intentionally did not happen.
    ++m_frame_id;
    if (any_presented) ++m_presented_frame_id;
}

u32 world::frame_id() { return m_frame_id; }

bool world::initialize_geometry_smoke() {
    if (!m_initialized || m_smoke_geometry || m_smoke_materials) return false;
    try {
        m_smoke_geometry = std::make_unique<vulkan::smoke_geometry>(*rhi::rhi_device::ref().get_backend());
        return true;
    } catch (std::exception const& error) {
        vulkan::diagnostic("error", "Geometry smoke initialization failed: %s", error.what());
        return false;
    }
}

bool world::initialize_material_smoke() {
    if (!m_initialized || m_smoke_geometry || m_smoke_materials) return false;
    try {
        m_smoke_materials = std::make_unique<vulkan::smoke_materials>(*rhi::rhi_device::ref().get_backend());
        return true;
    } catch (std::exception const& e) { vulkan::diagnostic("error", "Material smoke initialization failed: %s", e.what()); return false; }
}

void world::close_output(render_output_window_ptr const& output) {
    if(!m_scene_runtime)throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    auto& resource=m_scene_runtime->output(output);
    resource.request_close();
    // A close can overtake the first draw. No GPU object exists for an unadopted
    // lease, but its HWND owner still needs an acknowledgement on this thread.
    auto* backend=dynamic_cast<vulkan::vulkan_backend*>(rhi::rhi_device::ref().get_backend());
    if(!backend)throw std::runtime_error("Vulkan backend is unavailable");
    auto lease=resource.presentation();auto owned=backend->outputs();
    if(std::find(owned.begin(),owned.end(),lease)==owned.end())lease->closed=true;
    m_output_close_requested=true;
}

math::uint2 world::window_client_size(render_output_window_ptr const& output) {
    if (output) {
        if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
        return m_scene_runtime->output(output).client_size();
    }
    RECT rect = {};
    if (!m_window_handle || !GetClientRect(m_window_handle, &rect)) return math::uint2(0, 0);
    return math::uint2(static_cast<u32>(rect.right - rect.left), static_cast<u32>(rect.bottom - rect.top));
}

void world::draw_scene(scene_ptr const& scene, scene_view_ptr const& view, render_output_window_ptr const& output,
    viewport_type const& viewport, std::function<void(bool)> const& on_draw_scene) {
    if (!m_initialized || !m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->queue(scene,view,output,viewport,on_draw_scene);
}
particle::world_ptr world::particle_world_handle(scene_ptr const& scene) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    return m_scene_runtime->scene(scene).particle_world_handle();
}
void world::play_particle_system(scene_ptr const& scene,particle::particle_system_instance_ptr instance,
    bool use_transform,bool always_looping,math::float4x4 const& transform) {
    auto particles=particle_world_handle(scene);
    // Validate before play changes emitter placement, self-retention or list membership.
    if (use_transform) vulkan::validate_particle_transform(transform);
    particles->play(instance,transform,use_transform,always_looping);
}
void world::stop_particle_system(scene_ptr const& scene,particle::particle_system_instance_ptr instance) {
    particle_world_handle(scene)->stop(instance,0);
}
void world::remove_particle_system_instance(particle::particle_system_instance_ptr instance,scene_ptr const& scene) {
    particle_world_handle(scene)->remove(instance);
}
void world::update_particle_system_instance(particle::particle_system_instance_ptr instance,scene_ptr const& scene,
    math::float4x4 const& transform,bool visible,bool paused) {
    auto particles=particle_world_handle(scene);
    vulkan::validate_particle_transform(transform);
    // World/type/owner-thread checks in set_transform precede every mutation;
    // the accepted scene adapter cannot reject the already validated placement.
    particles->set_transform(instance,transform);
    particles->set_visible(instance,visible);
    particles->set_paused(instance,paused);
}
void world::draw_lines(scene_ptr const& scene,debug_vertices_type const& vertices,debug_indices_type const& indices) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->debug(scene,{vertices.begin(),vertices.size()},{indices.begin(),indices.size()},false);
}
void world::draw_triangles(scene_ptr const& scene,debug_vertices_type const& vertices,debug_indices_type const& indices) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->debug(scene,{vertices.begin(),vertices.size()},{indices.begin(),indices.size()},true);
}
void world::draw_ui_vertices(ui_vertices_type vertices,u32 const& count,int primitives,int points) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    if (!vertices && count) throw std::runtime_error("UI vertex pointer is null");
    m_scene_runtime->ui({vertices,count},primitives,points);
}
void world::draw_ui_vertices(scene_view_ptr const& view,ui_vertices_type vertices,u32 count,int primitives,int points,ui::draw_options const& options) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    if (!vertices && count) throw std::runtime_error("UI vertex pointer is null");
    m_scene_runtime->ui(view,{vertices,count},primitives,points,options);
}
void world::set_ui_font_texture(scene_view_ptr const& view,resources::unmanaged_resource_ptr const& texture) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->set_font_texture(view,texture);
}
void world::set_view_matrix(scene_view_ptr const& view, float4x4 const& matrix) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->view(view).set_view(matrix);
}
#ifndef MASTER_GOLD
void world::set_view_matrix_only(scene_view_ptr const& view, float4x4 const& matrix) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->view(view).set_view(matrix,false);
}
#endif
void world::set_projection_matrix(scene_view_ptr const& view, float4x4 const& matrix) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->view(view).set_projection(matrix);
}
void world::set_view_mode(scene_view_ptr view, scene_view_mode mode) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->view(view).set_mode(mode);
}
void world::set_lighting_environment(scene_view_ptr const& view,lighting_environment const& environment) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->view(view).set_environment(environment);
}
void world::set_postprocess_settings(scene_view_ptr const& view,postprocess_settings const& settings) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->view(view).set_postprocess(settings);
}
void world::enable_post_process(scene_view_ptr view,bool enabled) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->view(view).enable_postprocess(enabled);
}
void world::set_sky_material(scene_ptr const& scene,resources::unmanaged_resource_ptr material) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->set_sky_material(scene,material);
}
void world::reload_modified_textures() {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->reload_modified_textures();
}
void world::setup_view_and_output(scene_view_ptr const& view, render_output_window_ptr const& output, viewport_type const& viewport) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->validate(view,output,viewport);
}
void world::add_model(scene_ptr const& scene, render_model_instance_ptr const& model, float4x4 const& transform, bool apply_transform) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->add_model(scene,model,transform,apply_transform);
}
void world::update_model(scene_ptr const& scene, render_model_instance_ptr const& model, float4x4 const& transform) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->update_model(scene,model,transform);
}
void world::update_model_vertex_buffer(render_model_instance_ptr const& model, vectora<buffer_fragment> const& fragments) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->update_model_buffer(model,fragments,false);
}
void world::update_skeleton(render_model_instance_ptr const& model,math::float4x4* matrices,u32 count) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->update_skeleton(model,matrices,count);
}
void world::update_model_index_buffer(render_model_instance_ptr const& model, vectora<buffer_fragment> const& fragments) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->update_model_buffer(model,fragments,true);
}
void world::remove_model(scene_ptr const& scene, render_model_instance_ptr const& model) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->remove_model(scene,model);
}
void world::terrain_add_cell(scene_ptr const& scene,render_model_instance_ptr const& model) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable"); m_scene_runtime->terrain_add(scene,model);
}
void world::terrain_remove_cell(scene_ptr const& scene,render_model_instance_ptr const& model) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable"); m_scene_runtime->terrain_remove(scene,model);
}
void world::terrain_update_cell_buffer(scene_ptr const& scene,render_model_instance_ptr const& model,vectora<terrain_buffer_fragment> const& fragments,float4x4 const& transform) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable"); m_scene_runtime->terrain_update(scene,model,fragments,transform);
}
void world::terrain_update_cell_aabb(scene_ptr const& scene,render_model_instance_ptr const& model,math::aabb const& bounds) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable"); m_scene_runtime->terrain_update_bounds(scene,model,bounds);
}
void world::terrain_add_cell_texture(scene_ptr const& scene,render_model_instance_ptr const& model,fs_new::virtual_path_string const& name,u32 slot) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");m_scene_runtime->terrain_add_texture(scene,model,name,slot);
}
void world::terrain_remove_cell_texture(scene_ptr const& scene,render_model_instance_ptr const& model,u32 slot) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");m_scene_runtime->terrain_remove_texture(scene,model,slot);
}
void world::terrain_exchange_texture(scene_ptr const& scene,fs_new::virtual_path_string const& old_name,fs_new::virtual_path_string const& name) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");m_scene_runtime->terrain_exchange_texture(scene,old_name,name);
}
void world::set_model_visible(render_model_instance_ptr const& model, fs_new::virtual_path_string const& surface, bool visible) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->set_visible(model,surface,visible);
}
void world::set_model_material(render_model_instance_ptr const& model, fs_new::virtual_path_string const& surface, resources::unmanaged_resource_ptr material) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->set_material(model,surface,material);
}
void world::add_light(scene_ptr const& scene,u32 id,render::light_props const& props) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->add_light(scene,id,props);
}
void world::update_light(scene_ptr const& scene,u32 id,render::light_props const& props) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->update_light(scene,id,props);
}
void world::remove_light(scene_ptr const& scene,u32 id) {
    if (!m_scene_runtime) throw std::runtime_error("Vulkan scene lifecycle is unavailable");
    m_scene_runtime->remove_light(scene,id);
}
} } }
