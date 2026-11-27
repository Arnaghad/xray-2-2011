#include "pch.h"
#include "vulkan_scene_runtime.h"
#include "vulkan_diagnostics.h"
#include "vulkan_frustum.h"
#include "vulkan_presentation_output.h"
#include "vulkan_scene_particles.h"
#include "vulkan_terrain_bindings.h"
#include "vulkan_transparency.h"
#include <cmath>
#include <set>
#include <stdexcept>
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/vulkan/editor_view_modes.h>
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/vulkan/vegetation_resources.h>

namespace xray {
namespace render {
namespace vulkan {
scene_runtime::scene_runtime(platform::native_surface_handle window,
                             std::shared_ptr<presentation_output> output)
    : m_owner(register_scene_resources(window, std::move(output))),
      m_terrain_commands(std::make_shared<terrain_binding_runtime>(m_owner)) {}
scene_runtime::~scene_runtime() {
  m_owner->close();
  m_terrain_commands.reset();
  m_draws.clear();
  clear_particle_frame();
  m_particles.reset();
  m_debug.reset();
  m_ui.reset();
  m_terrain.reset();
  m_outputs.clear();
  m_geometry.reset();
  m_models.reset();
  finalize_scene_resources();
}
float scene_runtime::particle_frame_delta() {
  if (!m_particle_frame_open) {
    auto now = std::chrono::steady_clock::now();
    m_particle_delta =
        m_particle_clock_started
            ? std::min(
                  .25f,
                  std::chrono::duration<float>(now - m_particle_clock).count())
            : 0;
    m_particle_clock = now;
    m_particle_clock_started = m_particle_frame_open = true;
  }
  return m_particle_delta;
}
void scene_runtime::clear_particle_frame() {
  m_particle_frame.clear();
  m_particle_ticked.clear();
  m_particle_frame_open = false;
}
void scene_runtime::discard() {
  m_draws.clear();
  clear_particle_frame();
  m_particles.reset();
  m_tree_frame_bytes = 0;
  m_debug.reset();
  m_ui.reset();
  m_debug_bytes = m_debug_batches = m_ui_bytes = m_ui_batches = 0;
  for (auto &[key, value] : m_outputs)
    if (value.taa)
      value.taa->invalidate();
}
scene_resource &scene_runtime::scene(scene_ptr const &value) const {
  auto *result = dynamic_cast<scene_resource *>(value.c_ptr());
  if (!result || result->owner != m_owner || !m_owner->accepting())
    throw std::runtime_error(
        "Scene does not belong to the active Vulkan world");
  return *result;
}
static_model_resource &
scene_runtime::model(render_model_instance_ptr const &value) const {
  auto *result = dynamic_cast<static_model_resource *>(value.c_ptr());
  if (!result || result->owner != m_owner || !m_owner->accepting())
    throw std::runtime_error(
        "Model does not belong to the active Vulkan world");
  return *result;
}
namespace {
void valid_transform(math::float4x4 const &matrix) {
  for (unsigned r = 0; r < 4; ++r)
    for (unsigned c = 0; c < 4; ++c)
      if (!std::isfinite(matrix.elements[r][c]))
        throw std::runtime_error("Non-finite model transform");
  if (matrix.elements[0][3] != 0 || matrix.elements[1][3] != 0 ||
      matrix.elements[2][3] != 0 || matrix.elements[3][3] != 1)
    throw std::runtime_error("Model transform must be affine");
}
auto find_model(std::vector<model_instance_entry> &items,
                render_model_instance_ptr const &model) {
  return std::find_if(
      items.begin(), items.end(),
      [&](model_instance_entry const &item) { return item.model == model; });
}
} // namespace
void scene_runtime::add_model(scene_ptr const &target,
                              render_model_instance_ptr const &value,
                              math::float4x4 const &transform,
                              bool apply_transform) {
  auto &items = scene(target).models->items;
  auto &resource = model(value);
  auto const &matrix = apply_transform ? transform : resource.transform;
  valid_transform(matrix);
  if (find_model(items, value) != items.end())
    throw std::runtime_error("Model is already in this scene");
  items.push_back({value, matrix});
  resource.transform = matrix;
  ++scene(target).temporal_revision;
}
void scene_runtime::update_model(scene_ptr const &target,
                                 render_model_instance_ptr const &value,
                                 math::float4x4 const &transform) {
  auto &items = scene(target).models->items;
  model(value);
  valid_transform(transform);
  auto it = find_model(items, value);
  if (it == items.end())
    throw std::runtime_error("Cannot update a model outside this scene");
  it->transform = transform;
  model(value).transform = transform;
  ++scene(target).temporal_revision;
}
void scene_runtime::remove_model(scene_ptr const &target,
                                 render_model_instance_ptr const &value) {
  auto &items = scene(target).models->items;
  model(value);
  auto it = find_model(items, value);
  if (it == items.end())
    throw std::runtime_error("Cannot remove a model outside this scene");
  items.erase(it);
  ++scene(target).temporal_revision;
}
void scene_runtime::select_model(scene_ptr const &target,
                                 render_model_instance_ptr const &value,
                                 bool selected) {
  auto &items = scene(target).models->items;
  model(value);
  auto found = find_model(items, value);
  if (found == items.end())
    throw std::runtime_error("Cannot select a model outside this scene");
  found->selected = selected;
}
void scene_runtime::apply_model_edits(
    scene_ptr const &target, std::vector<model_scene_edit> const &edits) {
  auto &destination = scene(target);
  decltype(destination.models->items) next;
  next.reserve(edits.size());
  std::set<render_model_instance *> members;
  std::vector<std::pair<static_model_resource *, math::float4x4>> transforms;
  transforms.reserve(edits.size());
  for (auto const &edit : edits) {
    auto &resource = model(edit.model);
    valid_transform(edit.transform);
    if (!members.insert(edit.model.c_ptr()).second)
      throw std::runtime_error("Duplicate model in ordered scene batch");
    if (edit.visible)
      next.push_back({edit.model, edit.transform, edit.selected});
    transforms.emplace_back(&resource, edit.transform);
  }
  for (auto const &item : destination.models->items)
    if (!members.contains(item.model.c_ptr()))
      throw std::runtime_error("Ordered scene batch omitted an existing model");
  // All allocations and validation precede this commit. A complete batch is
  // published in authored order by one command, including selection/visibility.
  // Removing/re-appending a restored model changes coplanar fragment ownership.
  destination.models->items.swap(next);
  for (auto const &[resource, transform] : transforms)
    resource->transform = transform;
  ++destination.temporal_revision;
}
void scene_runtime::update_model_buffer(
    render_model_instance_ptr const &value,
    vectora<buffer_fragment> const &fragments, bool indices) {
  model(value).update_buffer(fragments, indices);
  ++model(value).temporal_revision;
}
terrain_instance_resource &
scene_runtime::terrain_model(render_model_instance_ptr const &value) const {
  auto *result = dynamic_cast<terrain_instance_resource *>(value.c_ptr());
  if (!result || result->owner != m_owner || !m_owner->accepting())
    throw std::runtime_error(
        "Terrain instance does not belong to the active Vulkan world");
  return *result;
}
void scene_runtime::terrain_add(scene_ptr const &target,
                                render_model_instance_ptr const &value) {
  auto &destination = scene(target);
  auto &resource = terrain_model(value);
  auto &items = destination.models->terrain;
  if (!destination.terrain_enabled)
    throw std::runtime_error("Scene terrain capability is disabled");
  if (terrain_limits(resource.memory_profile()).cell_bytes >
      terrain_limits(destination.terrain_memory).cell_bytes)
    throw std::runtime_error(
        "Terrain instance memory profile exceeds its scene profile");
  if (find_model(items, value) != items.end())
    throw std::runtime_error("Terrain cell is already in this scene");
  size_t vertices = resource.cell()->samples.size();
  for (auto const &item : items)
    vertices += terrain_model(item.model).cell()->samples.size();
  if (items.size() >= 64 || vertices > 262144)
    throw std::runtime_error(
        "Terrain scene exceeds 64 cells / 262144 vertices");
  items.push_back({value, resource.cell()->transform});
  m_terrain_commands->cancel(target);
  ++scene(target).temporal_revision;
}
void scene_runtime::terrain_remove(scene_ptr const &target,
                                   render_model_instance_ptr const &value) {
  auto &items = scene(target).models->terrain;
  terrain_model(value);
  auto it = find_model(items, value);
  if (it == items.end())
    throw std::runtime_error("Cannot remove terrain outside this scene");
  m_terrain_commands->cancel(target);
  items.erase(it);
  ++scene(target).temporal_revision;
}
void scene_runtime::terrain_update(
    scene_ptr const &target, render_model_instance_ptr const &value,
    vectora<terrain_buffer_fragment> const &fragments,
    math::float4x4 const &transform) {
  auto &items = scene(target).models->terrain;
  auto &resource = terrain_model(value);
  auto it = find_model(items, value);
  if (it == items.end())
    throw std::runtime_error("Cannot update terrain outside this scene");
  validate_gbuffer_transform(transform);
  if (fragments.empty() || fragments.size() > 4096)
    throw std::runtime_error("Terrain requires 1..4096 fragments");
  struct copied_edit {
    std::vector<std::vector<terrain_sample>> data;
    std::vector<terrain_cell_fragment> edits;
  };
  auto copy = std::make_shared<copied_edit>();
  auto &data = copy->data;
  auto &edits = copy->edits;
  size_t total = 0;
  data.reserve(fragments.size());
  edits.reserve(fragments.size());
  for (auto const &f : fragments) {
    if (f.rect.max.x <= f.rect.min.x || f.rect.max.y <= f.rect.min.y ||
        f.rect.max.x > resource.cell()->row_size ||
        f.rect.max.y > resource.cell()->row_size ||
        f.buffer.size() !=
            size_t(f.rect.max.x - f.rect.min.x) * (f.rect.max.y - f.rect.min.y))
      throw std::runtime_error("Invalid terrain facade rectangle");
    total += f.buffer.size();
    if (total > resource.cell()->samples.size())
      throw std::runtime_error("Terrain facade edits exceed cell budget");
    data.emplace_back();
    auto &samples = data.back();
    samples.reserve(f.buffer.size());
    for (auto const &source : f.buffer) {
      if (source.tex_coord0.x != source.tex_coord1.x ||
          source.tex_coord0.y != source.tex_coord1.y ||
          source.tex_coord0.x != source.tex_coord2.x ||
          source.tex_coord0.y != source.tex_coord2.y)
        throw std::runtime_error("Terrain facade adapter requires one shared "
                                 "shift, not independent UV sets");
      samples.push_back({source.height,
                         {source.tex_coord0.x, source.tex_coord0.y},
                         {source.tex_id0, source.tex_id1, source.tex_id2},
                         {source.alpha0, source.alpha1, source.alpha2},
                         source.color});
    }
    edits.push_back({f.rect.min.x, f.rect.min.y, f.rect.max.x - f.rect.min.x,
                     f.rect.max.y - f.rect.min.y, samples});
  }
  auto apply = [owner = m_owner, target, value, copy, transform] {
    if (!owner->accepting())
      throw std::runtime_error("Terrain update session is closed");
    auto &items =
        static_cast<scene_resource *>(target.c_ptr())->models->terrain;
    auto it = find_model(items, value);
    if (it == items.end())
      throw std::runtime_error("Cannot update terrain outside this scene");
    auto &resource = *static_cast<terrain_instance_resource *>(value.c_ptr());
    if (resource.game_geometry_locked())
      for (unsigned r = 0; r < 4; ++r)
        for (unsigned c = 0; c < 4; ++c)
          if (it->transform.elements[r][c] != transform.elements[r][c])
            throw std::runtime_error("Cooked game terrain placement is "
                                     "immutable; collision recook is required");
    auto previous = resource.cell();
    resource.update_cell(copy->edits);
    bool changed = previous != resource.cell();
    for (unsigned r = 0; r < 4; ++r)
      for (unsigned c = 0; c < 4; ++c)
        changed =
            changed || it->transform.elements[r][c] != transform.elements[r][c];
    it->transform = transform;
    return changed;
  };
  if (m_terrain_commands->busy(target)) {
    m_terrain_commands->defer(target,
                              total * sizeof(terrain_sample) +
                                  fragments.size() *
                                      (sizeof(terrain_cell_fragment) +
                                       sizeof(std::vector<terrain_sample>)),
                              [apply] { apply(); });
  } else if (apply())
    ++scene(target).temporal_revision;
}
void scene_runtime::terrain_update_bounds(
    scene_ptr const &target, render_model_instance_ptr const &value,
    math::aabb const &bounds) {
  auto &items = scene(target).models->terrain;
  auto &resource = terrain_model(value);
  if (find_model(items, value) == items.end())
    throw std::runtime_error("Cannot update terrain bounds outside this scene");
  // Culling is resolved at queue time; conservative metadata cannot change
  // rendered geometry or shadow depth fitting.
  if (m_terrain_commands->busy(target)) {
    m_terrain_commands->defer(
        target, sizeof(bounds), [owner = m_owner, target, value, bounds] {
          if (!owner->accepting())
            throw std::runtime_error("Terrain bounds session is closed");
          auto &items =
              static_cast<scene_resource *>(target.c_ptr())->models->terrain;
          if (find_model(items, value) == items.end())
            throw std::runtime_error(
                "Cannot update terrain bounds outside this scene");
          static_cast<terrain_instance_resource *>(value.c_ptr())
              ->update_bounds(bounds);
        });
  } else
    resource.update_bounds(bounds);
}
void scene_runtime::terrain_add_texture(scene_ptr const &target,
                                        render_model_instance_ptr const &value,
                                        fs_new::virtual_path_string const &name,
                                        u32 slot) {
  m_terrain_commands->add(target, value, name.c_str(), slot);
}
void scene_runtime::terrain_remove_texture(
    scene_ptr const &target, render_model_instance_ptr const &value, u32 slot) {
  m_terrain_commands->remove(target, value, slot);
}
void scene_runtime::terrain_exchange_texture(
    scene_ptr const &target, fs_new::virtual_path_string const &old_name,
    fs_new::virtual_path_string const &name) {
  m_terrain_commands->exchange(target, old_name.c_str(), name.c_str());
}
void scene_runtime::update_skeleton(render_model_instance_ptr const &value,
                                    math::float4x4 const *matrices, u32 count) {
  model(value).update_skeleton(matrices, count);
}
void scene_runtime::set_visible(render_model_instance_ptr const &value,
                                fs_new::virtual_path_string const &surface,
                                bool visible) {
  auto &resource = model(value);
  resource.surfaces[resource.surface_index(surface)].visible = visible;
  ++resource.temporal_revision;
}
void scene_runtime::set_material(
    render_model_instance_ptr const &value,
    fs_new::virtual_path_string const &surface,
    resources::unmanaged_resource_ptr const &material) {
  auto &resource = model(value);
  auto index = resource.surface_index(surface);
  auto *result = dynamic_cast<material_resource *>(material.c_ptr());
  if (material && (!result || result->owner != m_owner))
    throw std::runtime_error(
        "Material does not belong to the active Vulkan world");
  resource.surfaces[index].material = material;
  resource.surfaces[index].material_override = true;
  ++resource.temporal_revision;
}
view_resource &scene_runtime::view(scene_view_ptr const &value) const {
  auto *result = dynamic_cast<view_resource *>(value.c_ptr());
  if (!result || result->owner != m_owner || !m_owner->accepting())
    throw std::runtime_error("View does not belong to the active Vulkan world");
  return *result;
}
output_resource &
scene_runtime::output(render_output_window_ptr const &value) const {
  auto *result = dynamic_cast<output_resource *>(value.c_ptr());
  if (!result || result->owner != m_owner || !m_owner->accepting())
    throw std::runtime_error(
        "Output does not belong to the active Vulkan world");
  return *result;
}
void scene_runtime::validate(scene_view_ptr const &v,
                             render_output_window_ptr const &o,
                             viewport_type const &rect) const {
  view(v);
  output(o);
  for (float x : {rect.min.x, rect.min.y, rect.max.x, rect.max.y})
    if (!std::isfinite(x) || x < 0 || x > 1)
      throw std::runtime_error("Viewport must be finite and normalized");
  if (rect.min.x >= rect.max.x || rect.min.y >= rect.max.y)
    throw std::runtime_error("Viewport is empty or inverted");
}
void scene_runtime::queue(scene_ptr const &s, scene_view_ptr const &v,
                          render_output_window_ptr const &o,
                          viewport_type const &rect,
                          std::function<void(bool)> const &callback) {
  if (m_debug_capture)
    throw std::runtime_error("Reentrant Vulkan draw_scene is unsupported");
  validate(v, o, rect);
  auto &output_state = renderers(output(o).presentation());
  auto &m_gbuffer = output_state.gbuffer;
  auto &m_shadows = output_state.shadows;
  auto &target = scene(s);
  auto matrices = view(v).camera();
  auto mode = view(v).mode();
  auto postprocess = view(v).postprocess();
  if (postprocess.enabled &&
      (postprocess.ssao_enabled || postprocess.distortion_enabled) &&
      (mode != lit_view_mode || postprocess.msaa_samples != 1 ||
       postprocess.taa_enabled || postprocess.fxaa_enabled))
    throw std::runtime_error(
        "SSAO/distortion support final lit 1x views with post-AA off");
  if (!target.models->trees.empty() && mode == lit_view_mode &&
      postprocess.enabled &&
      (postprocess.msaa_samples != 1 || postprocess.taa_enabled ||
       postprocess.fxaa_enabled))
    throw std::runtime_error(
        "Fixed-LOD tree profile requires 1x with post-AA off");
  if (target.particles_enabled()) {
    if (mode != unlit_view_mode && mode != lit_view_mode)
      throw std::runtime_error(
          "Particle scenes support unlit and final lit views only");
    if (mode == lit_view_mode && postprocess.enabled &&
        (postprocess.msaa_samples != 1 || postprocess.taa_enabled ||
         postprocess.fxaa_enabled))
      throw std::runtime_error("Particle scenes require 1x with post-AA off");
    validate_particle_transform(matrices[0]);
  }
  // These are CPU render phases, not GPU completion notifications. Run them
  // before the channel retires the frame, including when the window is
  // minimized.
  std::vector<std::shared_ptr<debug_batch const>> debug;
  std::vector<std::shared_ptr<ui_batch const>> ui;
  if (callback) {
    m_debug_capture = &debug;
    m_debug_scene = s;
    m_ui_capture = &ui;
    m_ui_view = v;
    try {
      m_debug_depth = true;
      callback(true);
      m_debug_depth = false;
      callback(false);
    } catch (...) {
      m_debug_capture = nullptr;
      m_debug_scene = nullptr;
      m_debug_depth = true;
      m_ui_capture = nullptr;
      m_ui_view = nullptr;
      throw;
    }
    m_debug_capture = nullptr;
    m_debug_scene = nullptr;
    m_debug_depth = true;
    m_ui_capture = nullptr;
    m_ui_view = nullptr;
  }
  size_t debug_bytes=0; for (auto const& batch : debug) debug_bytes+=batch->bytes();
    if (debug_bytes>debug_frame_bytes-m_debug_bytes || debug.size()>debug_frame_batches-m_debug_batches)
        throw std::runtime_error("Debug frame budget exceeded (8 MiB / 256 batches)");
    if (!debug.empty() && !m_debug) m_debug=std::make_unique<debug_renderer>(*rhi::rhi_device::ref().get_backend());
    auto material_versions=m_owner->material_overrides();
    auto model_versions=m_owner->model_overrides();
    auto surface_material=[&](static_model_resource const& model,size_t index){
        auto const& surface=model.surfaces[index];if(surface.material_override)return surface.material;
        auto found=model_versions.find(model.data->identity);if(found==model_versions.end())return surface.material;
        auto const& version=*found->second;
        if(version.surfaces.size()!=model.surfaces.size() || version.surfaces[index].name!=model.data->surfaces[index].name)throw std::runtime_error("Authored model surface layout changed");
        return version.surfaces[index].material;
    };
    auto resolve_material=[&](resources::unmanaged_resource_ptr const& original){
        auto* material=dynamic_cast<material_resource*>(original.c_ptr());if(!material)return original;
        auto found=material_versions.find(material->virtual_path);return found==material_versions.end()?original:found->second;
    };
    bool taa=mode==lit_view_mode && postprocess.enabled && postprocess.taa_enabled;
    u64 temporal_signature=14695981039346656037ull;
    auto hash=[&](auto const& value) {
        auto bytes=reinterpret_cast<unsigned char const*>(&value);
        for (size_t i=0;i<sizeof(value);++i) { temporal_signature^=bytes[i]; temporal_signature*=1099511628211ull; }
    };
    auto hash_pointer=[&](auto pointer) { hash(reinterpret_cast<uintptr_t>(pointer)); };
    std::vector<std::shared_ptr<terrain_texture_set const>> terrain_textures;
    for (auto const& item : target.models->terrain) terrain_textures.push_back(terrain_model(item.model).textures());
    if (taa) {
        for (auto const& batch : debug) {
            hash(batch->triangles); hash(batch->depth);
            hash(batch->vertices.size()); hash(batch->indices.size());
            for (auto const& vertex : batch->vertices) { hash(vertex.position); hash(vertex.color); }
            for (auto index : batch->indices) hash(index);
        }
        hash(target.temporal_revision); hash_pointer(s.c_ptr()); hash_pointer(target.sky_material.c_ptr());
        auto environment=view(v).environment(); hash(environment.ambient); hash(environment.fog_color);
        hash(environment.fog_near); hash(environment.fog_distance); hash(environment.fog_enabled);
        for (auto const& light : target.lights) {
            hash(light.id); hash(light.type); hash(light.direction); hash(light.position); hash(light.color);
            for (float value : {light.intensity,light.diffuse,light.specular,light.range,light.attenuation_power,light.inner_cosine,light.outer_cosine,
                light.spot_falloff,light.shadow_transparency,light.shadow_depth_bias}) hash(value);
            hash(light.casts_shadows); hash(light.shadow_resolution); hash(light.shadow_cascades); hash(light.shadow_face_mask);
        }
        for (auto const& item : target.models->items) {
            auto& resource=model(item.model); hash_pointer(item.model.c_ptr());hash(resource.temporal_revision);hash(resource.skin_revision);
            for (auto const& row : resource.transform.elements) for (float value : row) hash(value);
            for (size_t index=0;index<resource.surfaces.size();++index) {auto const& surface=resource.surfaces[index];auto binding=surface_material(resource,index);
                hash(surface.visible); hash_pointer(surface.mesh.get()); hash_pointer(binding.c_ptr());
                if (!binding) continue;
                auto* material=static_cast<material_resource*>(resolve_material(binding).c_ptr()); hash_pointer(material->data.get());
                for (auto const& texture : {material->data->texture,material->textures.normal,material->textures.roughness,material->textures.specular,material->textures.detail_normal,
                    material->textures.emissive,material->textures.detail_diffuse,material->textures.variation_mask,material->textures.fresnel,material->textures.anisotropy_dir,material->textures.diffuse_power,
                    material->textures.bump,material->textures.translucency,material->textures.whirl,material->textures.transparency,
                    material->textures.cubemap,material->textures.cubemap_mask,material->textures.subdermal,material->textures.ao,
                    material->textures.back_illumination,material->textures.scattering_amount,material->textures.scattering_depth})
                    if (texture) hash_pointer(static_cast<texture_resource*>(texture.c_ptr())->snapshot().get());
            }
        }
        // Include off-camera cells: their geometry can still affect visible shadows.
        size_t terrain_index=0;
        for (auto const& item : target.models->terrain) {
            auto& instance=terrain_model(item.model); hash_pointer(item.model.c_ptr()); hash_pointer(instance.cell().get());
            for (auto const& row : item.transform.elements) for (float value : row) hash(value);
            auto const& textures=*terrain_textures[terrain_index++];hash(textures.diffuse.size());
            for (auto const* role : {&textures.diffuse,&textures.normal,&textures.specular}) for (auto const& image : *role) hash_pointer(image.get());
        }
    }
  bool gbuffer = !editor_forward_mode(mode);
  bool material_pass = mode == material_albedo_view_mode ||
                       mode == material_specular_view_mode ||
                       editor_material_mode(mode) || lighting_mode(mode) ||
                       !target.models->decals.empty();
  if (!target.models->decals.empty() &&
      (!gbuffer || (postprocess.enabled &&
                    (postprocess.msaa_samples != 1 || postprocess.taa_enabled ||
                     postprocess.fxaa_enabled))))
    throw std::runtime_error(
        "Decals require a deferred view, 1x and post-AA off");
  if (!target.models->decals.empty()) {
    auto const &camera = matrices[0];
    for (unsigned a = 0; a < 3; ++a)
      for (unsigned b = 0; b < 3; ++b) {
        double dot = 0;
        for (unsigned c = 0; c < 3; ++c)
          dot += double(camera.elements[a][c]) * camera.elements[b][c];
        if (std::abs(dot - (a == b ? 1. : 0.)) > 1e-4)
          throw std::runtime_error("Decals require a rigid camera");
      }
    if (((camera.i.xyz() ^ camera.j.xyz()) | camera.k.xyz()) <= 0)
      throw std::runtime_error("Reflected decal camera");
  }
  bool shadows =
      lighting_mode(mode) &&
      std::any_of(target.lights.begin(), target.lights.end(),
                  [](auto const &light) { return light.casts_shadows; });
  auto culling_projection = matrices[1];
  auto output_size = output(o).client_size();
  if (taa) {
    float width = (rect.max.x - rect.min.x) * output_size.x,
          height = (rect.max.y - rect.min.y) * output_size.y;
    for (unsigned r = 0; r < 4; ++r) {
      culling_projection.elements[r][0] *= width / (width + 2);
      culling_projection.elements[r][1] *= height / (height + 2);
    }
  }
  // Validate visible terrain snapshots and their combined view budget before
  // preparing any GPU renderer/targets, including a suspended GBuffer.
  std::vector<terrain_draw> terrain;
  size_t terrain_bytes = 0, terrain_index = 0;
  for (auto const &item : target.models->terrain) {
    auto &instance = terrain_model(item.model);
    auto cell = instance.cell();
    auto const &textures = *terrain_textures[terrain_index++];
    local_frustum frustum(item.transform, matrices[2], culling_projection);
    auto bounds = instance.local_bounds();
    if (!frustum.intersects({bounds.min.x, bounds.min.y, bounds.min.z},
                            {bounds.max.x, bounds.max.y, bounds.max.z}))
      continue;
    terrain_draw draw = {item.model,       cell, item.transform,
                         textures.diffuse, {},   {}};
    draw.memory_profile = textures.memory_profile;
    if (gbuffer) {
      draw.normal = textures.normal;
      draw.specular = textures.specular;
      validate_gbuffer_transform(draw.transform);
    }
    validate_terrain_draw(draw);
    auto limit = terrain_limits(target.terrain_memory).queued_view_bytes;
    for (auto const *images : {&draw.diffuse, &draw.normal, &draw.specular})
      for (auto const &image : *images)
        for (auto const &mip : image->mips) {
          if (mip.size() > limit - terrain_bytes)
            throw std::runtime_error("Queued terrain arrays exceed " +
                                     std::to_string(limit / (1024 * 1024)) +
                                     " MiB");
          terrain_bytes += mip.size();
        }
    terrain.push_back(std::move(draw));
  }
  if (target.test_geometry && !m_geometry)
    m_geometry = std::make_unique<smoke_materials>(
        *rhi::rhi_device::ref().get_backend());
  gbuffer_camera_data gbuffer_parameters;
  if (gbuffer) {
    if (target.test_geometry)
      throw std::runtime_error(
          "GBuffer modes require the static-model scene profile");
    gbuffer_parameters = gbuffer_camera(matrices[1]);
    validate_gbuffer_transform(matrices[0]);
    if (!m_gbuffer)
      m_gbuffer = std::make_unique<gbuffer_renderer>(
          *rhi::rhi_device::ref().get_backend());
    m_gbuffer->prepare(mode, shadows);
  }
  frame_constants frame = {};
  sky_draw sky;
  if (mode == lit_view_mode && target.sky_material) {
    auto const &config =
        static_cast<sky_material_resource *>(target.sky_material.c_ptr())->data;
    sky.texture =
        static_cast<texture_resource *>(config.texture.c_ptr())->snapshot();
    if (taa)
      hash_pointer(sky.texture.get());
    auto basis = matrices[0];
    basis.elements[3][0] = basis.elements[3][1] = basis.elements[3][2] = 0;
    math::float4x4 inverse_view, inverse_projection;
    if (!inverse_view.try_invert(basis) ||
        !math::try_invert4x4(matrices[1], inverse_projection))
      throw std::runtime_error(
          "Sky requires invertible affine view and projection");
    auto clip_to_sky =
        math::mul4x4(math::mul4x4(inverse_projection, inverse_view),
                     math::create_rotation_y(config.rotation));
    for (unsigned row = 0; row < 4; ++row)
      for (unsigned col = 0; col < 4; ++col) {
        float value = clip_to_sky.elements[col][row];
        if (!std::isfinite(value))
          throw std::runtime_error("Sky camera transform is non-finite");
        sky.constants.world[row * 4 + col] = value;
      }
    for (unsigned c = 0; c < 3; ++c)
      sky.constants.tint[c] = config.color[c] * config.intensity;
    sky.constants.tint[3] = 1;
    m_gbuffer->prepare_sky(sky.texture);
  }
  // X-Ray uses row-vector matrices; the HLSL fixture multiplies column vectors.
  for (unsigned row = 0; row < 4; ++row)
    for (unsigned col = 0; col < 4; ++col) {
      frame.view[row * 4 + col] = matrices[0].elements[col][row];
      frame.projection[row * 4 + col] = matrices[1].elements[col][row];
    }
    std::vector<model_draw> models,casters,transparent,selected;
    auto selection=view(v).selection();
    static_culling_statistics culling;
    bool const drawable_submission=output_size.x && output_size.y;
    if(!drawable_submission)postprocess.ssao_enabled=postprocess.distortion_enabled=false;
    std::vector<model_draw> distortions;
    if(drawable_submission && mode==lit_view_mode && postprocess.enabled && (postprocess.ssao_enabled || postprocess.distortion_enabled))m_gbuffer->prepare_effects();
    if(editor_effect_mode(mode))m_gbuffer->prepare_effects();
    if(drawable_submission)prepare_trees(target,matrices[0]);
    for (auto const& item : target.models->items) {
        // A minimized submission has no tree GPU work, even if restored before end_frame.
        // The next drawable submission captures its current camera and leaf geometry.
        if(!drawable_submission && std::any_of(target.models->trees.begin(),target.models->trees.end(),[&](auto const& value){
            return static_cast<tree_instance const*>(value.c_ptr())->model==item.model;}))continue;
        auto& resource=model(item.model);
        ++culling.models_tested;
        local_frustum frustum(resource.transform,matrices[2],culling_projection);
        bool camera_visible=frustum.intersects(resource.geometry_minimum,resource.geometry_maximum);
        if (!camera_visible) { ++culling.models_culled; if (!shadows) continue; }
        std::vector<std::string> const* active_lod_surfaces=nullptr;
        if (camera_visible && resource.data->lods.size()>=2) {
            float min_y=1.f, max_y=-1.f; bool any_front=false;
            for (unsigned corner=0;corner<8;++corner) {
                math::float3 local_pt(
                    (corner&1)?resource.geometry_maximum[0]:resource.geometry_minimum[0],
                    (corner&2)?resource.geometry_maximum[1]:resource.geometry_minimum[1],
                    (corner&4)?resource.geometry_maximum[2]:resource.geometry_minimum[2]);
                math::float3 world_pt=resource.transform.transform_position(local_pt);
                math::float3 view_pt=matrices[2].transform_position(world_pt);
                float clip_y=view_pt.x*culling_projection.elements[0][1]+view_pt.y*culling_projection.elements[1][1]+view_pt.z*culling_projection.elements[2][1]+culling_projection.elements[3][1];
                float clip_w=view_pt.x*culling_projection.elements[0][3]+view_pt.y*culling_projection.elements[1][3]+view_pt.z*culling_projection.elements[2][3]+culling_projection.elements[3][3];
                if (clip_w>1e-4f) {
                    any_front=true;
                    float ndc_y=std::clamp(clip_y/clip_w,-1.f,1.f);
                    min_y=std::min(min_y,ndc_y); max_y=std::max(max_y,ndc_y);
                }
            }
            float coverage=any_front ? std::clamp((max_y-min_y)*0.5f,0.f,1.f) : 0.f;
            active_lod_surfaces=&resource.data->lods.back().surfaces;
            for (auto const& lod : resource.data->lods) {
                if (coverage>=lod.min_screen_coverage) { active_lod_surfaces=&lod.surfaces; break; }
            }
        }
        for (size_t i=0;i<resource.surfaces.size();++i) {
            auto const& surface=resource.surfaces[i];
            if (!surface.visible) { if (camera_visible) ++culling.surfaces_hidden; continue; }
            if (active_lod_surfaces && std::find(active_lod_surfaces->begin(),active_lod_surfaces->end(),resource.data->surfaces[i].name)==active_lod_surfaces->end()) continue;
            auto active_material=resolve_material(surface_material(resource,i));
            auto const* mat_ptr=active_material ? static_cast<material_resource*>(active_material.c_ptr()) : nullptr;
            u8 const stages=mat_ptr ? mat_ptr->textures.stages_mask : material_stage_gbuffer;
            bool const is_distortion=mat_ptr && mat_ptr->distortion.enabled;
            bool const is_opaque=!mat_ptr || mat_ptr->blend==material_blend_mode::opaque;
            bool const has_g_stage=!is_distortion && ((stages & (material_stage_gbuffer|material_stage_lighting)) || is_opaque);
            bool const has_f_stage=!is_distortion && !is_opaque && ((stages & material_stage_forward) || !has_g_stage);
            bool const forward_view_active=(mode==lit_view_mode || mode==lit_with_histogram_view_mode || editor_forward_mode(mode));
            bool const need_forward=has_f_stage && forward_view_active;
            if (!has_g_stage && !need_forward && !is_distortion) continue;
            if (camera_visible) ++culling.surfaces_tested;
            auto const& mesh=surface.mesh;
            bool surface_visible=camera_visible && frustum.intersects(mesh->minimum,mesh->maximum);
            if (camera_visible && !surface_visible) ++culling.surfaces_culled;
            if (!has_g_stage && need_forward && !surface_visible) continue;
            if (!surface_visible && !shadows) continue;
            if (surface_visible) ++culling.surfaces_submitted;
            std::shared_ptr<texture_data const> texture,normal,roughness,specular,detail;
            std::shared_ptr<texture_data const> emissive,detail_diffuse,variation_mask,fresnel,anisotropy_dir,diffuse_power;
            std::shared_ptr<texture_data const> bump,translucency,whirl,transparency;
            std::shared_ptr<texture_data const> cubemap,cubemap_mask,subdermal,ao,back_illum,scat_amt,scat_depth;
            bool cast_shadow=true;
            if (mat_ptr) {
                auto const& material=mat_ptr->data;
                cast_shadow=material->cast_shadow;
                if (material->texture) texture=static_cast<texture_resource*>(material->texture.c_ptr())->snapshot();
                if (gbuffer && has_g_stage && mat_ptr->textures.normal) normal=static_cast<texture_resource*>(mat_ptr->textures.normal.c_ptr())->linear_snapshot();
                if (gbuffer && has_g_stage && mat_ptr->textures.detail_normal) detail=static_cast<texture_resource*>(mat_ptr->textures.detail_normal.c_ptr())->linear_snapshot();
                if (gbuffer && has_g_stage && mat_ptr->textures.roughness) roughness=static_cast<texture_resource*>(mat_ptr->textures.roughness.c_ptr())->linear_snapshot();
                if (gbuffer && has_g_stage && mat_ptr->textures.fresnel) fresnel=static_cast<texture_resource*>(mat_ptr->textures.fresnel.c_ptr())->linear_snapshot();
                if (gbuffer && has_g_stage && mat_ptr->textures.anisotropy_dir) anisotropy_dir=static_cast<texture_resource*>(mat_ptr->textures.anisotropy_dir.c_ptr())->linear_snapshot();
                if ((gbuffer || material_pass) && has_g_stage && mat_ptr->textures.bump) bump=static_cast<texture_resource*>(mat_ptr->textures.bump.c_ptr())->linear_snapshot();
                if (gbuffer && has_g_stage && mat_ptr->textures.translucency) translucency=static_cast<texture_resource*>(mat_ptr->textures.translucency.c_ptr())->linear_snapshot();
                if (material_pass && has_g_stage && mat_ptr->textures.specular) specular=static_cast<texture_resource*>(mat_ptr->textures.specular.c_ptr())->snapshot();
                if (material_pass && has_g_stage && mat_ptr->textures.emissive) emissive=static_cast<texture_resource*>(mat_ptr->textures.emissive.c_ptr())->snapshot();
                if (material_pass && has_g_stage && mat_ptr->textures.detail_diffuse) detail_diffuse=static_cast<texture_resource*>(mat_ptr->textures.detail_diffuse.c_ptr())->snapshot();
                if (material_pass && has_g_stage && mat_ptr->textures.variation_mask) variation_mask=static_cast<texture_resource*>(mat_ptr->textures.variation_mask.c_ptr())->snapshot();
                if (material_pass && has_g_stage && mat_ptr->textures.diffuse_power) diffuse_power=static_cast<texture_resource*>(mat_ptr->textures.diffuse_power.c_ptr())->linear_snapshot();
                if (material_pass && has_g_stage && mat_ptr->textures.whirl) whirl=static_cast<texture_resource*>(mat_ptr->textures.whirl.c_ptr())->snapshot();
                if (need_forward && mat_ptr->textures.transparency) transparency=static_cast<texture_resource*>(mat_ptr->textures.transparency.c_ptr())->linear_snapshot();
                if ((material_pass || need_forward) && mat_ptr->textures.cubemap) cubemap=static_cast<texture_resource*>(mat_ptr->textures.cubemap.c_ptr())->snapshot();
                if (material_pass && mat_ptr->textures.cubemap_mask) cubemap_mask=static_cast<texture_resource*>(mat_ptr->textures.cubemap_mask.c_ptr())->linear_snapshot();
                if (material_pass && mat_ptr->textures.subdermal) subdermal=static_cast<texture_resource*>(mat_ptr->textures.subdermal.c_ptr())->snapshot();
                if (material_pass && mat_ptr->textures.ao) ao=static_cast<texture_resource*>(mat_ptr->textures.ao.c_ptr())->linear_snapshot();
                if (material_pass && mat_ptr->textures.back_illumination) back_illum=static_cast<texture_resource*>(mat_ptr->textures.back_illumination.c_ptr())->snapshot();
                if (material_pass && mat_ptr->textures.scattering_amount) scat_amt=static_cast<texture_resource*>(mat_ptr->textures.scattering_amount.c_ptr())->linear_snapshot();
                if (material_pass && mat_ptr->textures.scattering_depth) scat_depth=static_cast<texture_resource*>(mat_ptr->textures.scattering_depth.c_ptr())->linear_snapshot();
            }
            model_draw draw={item.model,mesh,resource.transform,surface.visible,active_material,std::move(texture),std::move(normal),std::move(roughness),std::move(specular),std::move(detail),
                std::move(emissive),std::move(detail_diffuse),std::move(variation_mask),std::move(fresnel),std::move(anisotropy_dir),std::move(diffuse_power),
                std::move(bump),std::move(translucency),std::move(whirl),std::move(transparency),
                std::move(cubemap),std::move(cubemap_mask),std::move(subdermal),std::move(ao),std::move(back_illum),std::move(scat_amt),std::move(scat_depth),
                resource.data->surfaces[i].skin,resource.bone_palette,resource.skin_revision};
            if(selection.enabled && item.selected && surface_visible)selected.push_back(draw);
            if(!resource.data->inverse_bind_pose.empty() || std::any_of(target.models->trees.begin(),target.models->trees.end(),[&](auto const& tree){
                return static_cast<tree_instance const*>(tree.c_ptr())->model==item.model;}))draw.decal_receiver=0;
            if(is_distortion){
                if(draw.decal_receiver==0)throw std::runtime_error("Distortion supports immutable static geometry only");
                if(drawable_submission && surface_visible && ((mode==lit_view_mode && postprocess.enabled && postprocess.distortion_enabled) || mode==distortion_only_view_mode)){
                    validate_gbuffer_transform(draw.transform);distortions.push_back(std::move(draw));}continue;
            }
            if (need_forward && editor_forward_mode(mode) && mode!=unlit_view_mode && !has_g_stage){if(surface_visible)models.push_back(std::move(draw));continue;}
            if (need_forward && surface_visible) { validate_gbuffer_transform(draw.transform); transparent.push_back(has_g_stage ? draw : std::move(draw)); }
            if (!has_g_stage) continue;
            if (shadows && cast_shadow) { validate_gbuffer_transform(draw.transform); casters.push_back(draw); }
            if (surface_visible) models.push_back(std::move(draw));
        }
    }
  if (!models.empty()) {
    if (gbuffer)
      for (auto const &item : models)
        validate_gbuffer_transform(item.transform);
    if (!m_models)
      m_models = std::make_unique<static_renderer>(
          *rhi::rhi_device::ref().get_backend());
    m_models->prepare(models, gbuffer, material_pass);
    if (editor_forward_mode(mode) && mode != unlit_view_mode &&
        mode != wireframe_view_mode && mode != wireframe_two_sided_view_mode)
      m_models->prepare_diagnostic();
  }
  if (!transparent.empty()) {
    sort_transparent_draws(transparent, matrices[0]);
    if (!m_models)
      m_models = std::make_unique<static_renderer>(
          *rhi::rhi_device::ref().get_backend());
    u32 forward_samples =
        (gbuffer && mode == lit_view_mode && postprocess.enabled)
            ? postprocess.msaa_samples
            : 1;
    bool forward_has_depth = gbuffer && (mode == lit_view_mode ||
                                         mode == lit_with_histogram_view_mode);
    m_models->prepare(transparent, false, false, false, true, false,
                      forward_has_depth, forward_samples);
  }
  if (!distortions.empty()) {
    size_t count = distortions.size(), bytes = 0, images = 0;
    std::vector<void const *> meshes, textures;
    auto account = [&](auto const &list) {
      for (auto const &draw : list) {
        if (std::find(meshes.begin(), meshes.end(), draw.mesh.get()) ==
            meshes.end()) {
          meshes.push_back(draw.mesh.get());
          bytes += draw.mesh->vertices.size() * sizeof(static_vertex) +
                   draw.mesh->indices.size() * sizeof(u16);
        }
        if (std::find(textures.begin(), textures.end(), draw.texture.get()) ==
            textures.end()) {
          textures.push_back(draw.texture.get());
          for (auto const &mip : draw.texture->mips)
            images += mip.size();
        }
      }
    };
    account(distortions);
    for (auto const &previous : m_draws) {
      count += previous.distortions.size();
      account(previous.distortions);
    }
    if (count > 64 || bytes > 8u * 1024u * 1024u ||
        images > 64u * 1024u * 1024u)
      throw std::runtime_error("Distortion frame exceeds 64 surfaces / 8 MiB "
                               "geometry / 64 MiB images");
    if (!m_models)
      m_models = std::make_unique<static_renderer>(
          *rhi::rhi_device::ref().get_backend());
    m_models->prepare(distortions, false, false, false, false, true);
  }
  if (shadows)
    for (auto const &item : target.models->terrain) {
      auto cell = terrain_model(item.model).cell();
      validate_gbuffer_transform(item.transform);
      // Opaque terrain shares the depth-only mesh path, including off-camera
      // casters. Retain the immutable mesh, not the mutable terrain instance's
      // next cell version.
      casters.push_back({item.model,
                         cell->mesh,
                         item.transform,
                         true,
                         {},
                         {},
                         {},
                         {},
                         {},
                         {}});
    }
  auto lights = target.lights;
  std::vector<scene_shadow_plan> shadow_plans(lights.size());
  if (shadows) {
    if (!m_shadows)
      m_shadows = std::make_unique<scene_shadows>(
          *rhi::rhi_device::ref().get_backend());
    if (!casters.empty()) {
      if (!m_models)
        m_models = std::make_unique<static_renderer>(
            *rhi::rhi_device::ref().get_backend());
      m_models->prepare(casters, false, false, true);
    }
    for (size_t i = 0; i < lights.size(); ++i)
      if (lights[i].casts_shadows)
        shadow_plans[i] = lights[i].type == scene_light_type::spot
                              ? plan_spot_shadows(lights[i], matrices[0])
                          : lights[i].type == scene_light_type::point
                              ? plan_point_shadows(lights[i], matrices[0])
                              : plan_directional_shadows(lights[i], matrices[0],
                                                         matrices[1], casters);
  }
  if (lighting_mode(mode) &&
      std::any_of(lights.begin(), lights.end(), [](auto const &light) {
        return light.type != scene_light_type::directional;
      })) {
    // Range and cone angles are world distances/angles, so local lights require
    // a rigid view.
    for (unsigned a = 0; a < 3; ++a)
      for (unsigned b = 0; b < 3; ++b) {
        double dot = 0;
        for (unsigned c = 0; c < 3; ++c)
          dot +=
              double(matrices[0].elements[a][c]) * matrices[0].elements[b][c];
        if (std::abs(dot - (a == b ? 1.0 : 0.0)) > 1e-4)
          throw std::runtime_error(
              "Local lighting requires a rigid camera view");
      }
  }
  if (lighting_mode(mode))
    for (auto &light : lights) {
      if (light.type != scene_light_type::directional) {
        std::array<float, 3> position = {};
        for (unsigned c = 0; c < 3; ++c) {
          double value = matrices[0].elements[3][c];
          for (unsigned r = 0; r < 3; ++r)
            value += double(light.position[r]) * matrices[0].elements[r][c];
          if (!std::isfinite(value) || std::abs(value) > 1e6)
            throw std::runtime_error(
                "View-space light position exceeds supported bounds");
          position[c] = float(value);
        }
        light.position = position;
      }
      if (light.type == scene_light_type::point)
        continue;
      std::array<double, 3> direction = {};
      double length = 0;
      for (unsigned c = 0; c < 3; ++c) {
        for (unsigned r = 0; r < 3; ++r)
          direction[c] +=
              double(light.direction[r]) * matrices[0].elements[r][c];
        length += direction[c] * direction[c];
      }
      if (!std::isfinite(length) || length < 1e-12)
        throw std::runtime_error("View collapses the light direction");
      for (unsigned i = 0; i < 3; ++i)
        light.direction[i] = float(direction[i] / std::sqrt(length));
    }
    if (!terrain.empty()) {
        if (!m_terrain) m_terrain=std::make_unique<terrain_renderer>(*rhi::rhi_device::ref().get_backend());
        m_terrain->prepare(terrain);
    }
    auto debug_count=debug.size();
    size_t ui_bytes=0; for (auto const& batch : ui) ui_bytes+=batch->bytes(); auto ui_count=ui.size();
    if (!ui.empty()) {
        if (!m_ui) m_ui=std::make_unique<ui_renderer>(*rhi::rhi_device::ref().get_backend());
        m_ui->prepare(ui);
    }
    std::vector<std::shared_ptr<particle_billboard_batch const>> particles,next_particle_frame=m_particle_frame;
    if (target.particles_enabled()) {
        auto world=target.particle_world_handle();
        if (std::find(m_particle_ticked.begin(),m_particle_ticked.end(),world)==m_particle_ticked.end()) {
            if (m_particle_ticked.size()>=particle_frame_batches) throw std::runtime_error("Particle frame exceeds 64 scene worlds");
            float elapsed=particle_frame_delta();
            m_particle_ticked.push_back(world);
            world->tick(target.particle_fixed_timestep ? target.particle_fixed_timestep : elapsed,matrices[0]);
        }
        if(drawable_submission)particles=snapshot_scene_particles(*world,m_owner,matrices[0]);
        next_particle_frame.insert(next_particle_frame.end(),particles.begin(),particles.end());
        validate_particle_batches(next_particle_frame);
        if (!particles.empty()) {
            if (!m_particles) m_particles=std::make_unique<particle_renderer>(*rhi::rhi_device::ref().get_backend());
            m_particles->prepare(particles);
        }
    }
    std::vector<decal_draw> decals;
    if(drawable_submission)for(auto const& entry:target.models->decals){
        validate_decal(entry.properties,m_owner);auto const& material=*static_cast<decal_material_resource*>(entry.properties.material.c_ptr());
        auto cubemap=material.textures.cubemap ? static_cast<texture_resource*>(material.textures.cubemap.c_ptr())->snapshot() : nullptr;
        decals.push_back({entry.properties,static_cast<texture_resource*>(material.base.texture.c_ptr())->snapshot(),static_cast<texture_resource*>(material.textures.normal.c_ptr())->linear_snapshot(),std::move(cubemap)});
    }
    if(!decals.empty()){
        size_t count=decals.size(),bytes=0;std::vector<std::shared_ptr<texture_data const>> images;
        auto account=[&](auto const& list){for(auto const& draw:list)for(auto const& data:{draw.diffuse,draw.normal,draw.cubemap})if(data && std::find(images.begin(),images.end(),data)==images.end()){
            images.push_back(data);for(auto const& mip:data->mips)bytes+=mip.size();}};
        account(decals);for(auto const& previous:m_draws){count+=previous.decals.size();account(previous.decals);}
        if(count>64 || bytes>64u*1024u*1024u)throw std::runtime_error("Decal frame exceeds 64 projections / 64 MiB textures");
        m_gbuffer->prepare_decals(decals);
    }
  m_draws.push_back({s,
                     v,
                     o,
                     frame,
                     rect,
                     std::move(models),
                     mode,
                     gbuffer_parameters,
                     std::move(lights),
                     view(v).environment(),
                     std::move(casters),
                     std::move(shadow_plans),
                     std::move(sky),
                     std::move(transparent),
                     postprocess,
                     temporal_signature,
                     std::move(debug),
                     std::move(ui),
                     std::move(terrain),
                     std::move(particles),
                     std::move(decals),
                     std::move(distortions)});
  if (!selected.empty()) {
    if (!m_models)
      m_models = std::make_unique<static_renderer>(
          *rhi::rhi_device::ref().get_backend());
    m_models->prepare_mask(selected);
    if (!output_state.selection)
      output_state.selection = std::make_unique<selection_renderer>(
          *rhi::rhi_device::ref().get_backend());
    m_draws.back().selected = std::move(selected);
    m_draws.back().selection = selection;
  }
  m_particle_frame.swap(next_particle_frame);
  m_debug_bytes += debug_bytes;
  m_debug_batches += debug_count;
  m_ui_bytes += ui_bytes;
  m_ui_batches += ui_count;
  record_static_culling(culling);
}
scene_runtime::output_renderers &
scene_runtime::renderers(std::shared_ptr<presentation_output> const &output) {
  auto &state = m_outputs[output.get()];
  state.output = output;
  return state;
}
std::vector<std::shared_ptr<presentation_output>>
scene_runtime::queued_outputs() const {
  std::vector<std::shared_ptr<presentation_output>> result;
  for (auto const &command : m_draws) {
    auto target = output(command.output).presentation();
    if (!target->closed && !target->close_requested &&
        std::find(result.begin(), result.end(), target) == result.end())
      result.push_back(target);
  }
  return result;
}
void scene_runtime::begin_draw_frame() {
  m_used_debug = m_used_ui = m_used_particles = false;
  for (auto &[key, value] : m_outputs)
    value.touched = value.used_gbuffer = value.used_shadows = value.used_taa =
        false;
  if (m_debug)
    m_debug->begin_frame();
  if (m_ui)
    m_ui->begin_frame();
  if (m_particles)
    m_particles->begin_frame();
}
void scene_runtime::suspend(
    std::shared_ptr<presentation_output> const &output) {
  auto it = m_outputs.find(output.get());
  if (it != m_outputs.end() && it->second.taa)
    it->second.taa->invalidate();
}
void scene_runtime::draw(
    rhi::rhi_backend &backend, u32 presented,
    std::shared_ptr<presentation_output> const &destination) {
  auto &state = renderers(destination);
  state.touched = true;
  state.presented = presented;
  auto &m_gbuffer = state.gbuffer;
  auto &m_shadows = state.shadows;
  auto &m_taa = state.taa;
  bool used_gbuffer = false, used_shadows = false, used_taa = false;
  bool used_debug = false, used_ui = false, used_particles = false;
  for (auto const &command : m_draws) {
    if (output(command.output).presentation() != destination)
      continue;
    auto const *scene = static_cast<scene_resource *>(command.scene.c_ptr());
    auto size = output(command.output).client_size();
    if (!size.x || !size.y)
      continue;
    auto rect = command.viewport;
    bool gbuffer = !editor_forward_mode(command.mode);
    auto camera = command.camera;
    auto gbuffer_camera = command.gbuffer_camera;
    auto sky = command.sky;
    std::function<rhi::rhi_texture_handle(rhi::rhi_texture_handle,
                                          rhi::rhi_texture_handle,
                                          rhi::rhi_shader_handle)>
        temporal;
    bool covered = std::ceil(rect.max.x * size.x - .5f) >
                       std::ceil(rect.min.x * size.x - .5f) &&
                   std::ceil(rect.max.y * size.y - .5f) >
                       std::ceil(rect.min.y * size.y - .5f);
    if (covered && command.mode == lit_view_mode &&
        command.postprocess.enabled && command.postprocess.taa_enabled) {
      if (!m_taa)
        m_taa = std::make_unique<taa_renderer>(backend);
      taa_renderer::key identity = {view(command.view).temporal_id,
                                    output(command.output).temporal_id,
                                    rect.min.x,
                                    rect.min.y,
                                    rect.max.x,
                                    rect.max.y};
      auto context =
          m_taa->begin(identity, size.x, size.y, presented,
                       command.temporal_signature, camera, command.postprocess);
      camera = context.camera;
      gbuffer_camera = context.gbuffer;
      used_taa = true;
      if (sky.texture)
        for (unsigned r = 0; r < 4; ++r)
          sky.constants.world[r * 4 + 3] -=
              context.jitter_x * sky.constants.world[r * 4] +
              context.jitter_y * sky.constants.world[r * 4 + 1];
      temporal = [renderer = m_taa.get(), context](auto color, auto depth,
                                                   auto vs) {
        return renderer->render(context, color, depth, vs);
      };
    }
    if (m_models && (m_models->has_skinning(command.models) ||
                     m_models->has_skinning(command.casters) ||
                     m_models->has_skinning(command.transparent) ||
                     m_models->has_skinning(command.selected))) {
      backend.end_render_pass();
      m_models->run_skinning(command.models);
      m_models->run_skinning(command.casters);
      m_models->run_skinning(command.transparent);
      m_models->run_skinning(command.selected);
      backend.begin_presentation_pass(true);
    }
    if (gbuffer) {
      m_gbuffer->begin(size.x, size.y,
                       command.mode == lit_view_mode &&
                               command.postprocess.enabled
                           ? command.postprocess.msaa_samples
                           : 1);
      used_gbuffer = true;
    }
    float x = rect.min.x * size.x, y = rect.min.y * size.y;
    float w = (rect.max.x - rect.min.x) * size.x,
          h = (rect.max.y - rect.min.y) * size.y;
    backend.set_viewport(x, y, w, h);
    // Clip to the normalized viewport using integer pixel bounds.
    auto left = u32(std::floor(x)), top = u32(std::floor(y));
    backend.set_scissor(left, top, u32(std::ceil(x + w)) - left,
                        u32(std::ceil(y + h)) - top);
    if (scene->test_geometry)
      m_geometry->draw(presented, &command.camera);
    float mat_phase = material_phase();
    if (!command.models.empty())
      m_models->draw(command.models, camera,
                     gbuffer ? gbuffer_camera.far_plane : 0, false, false,
                     false, false, mat_phase, false,
                     gbuffer ? unlit_view_mode : command.mode, size.x, size.y);
    if (!command.terrain.empty())
      m_terrain->draw(command.terrain, camera,
                      gbuffer ? gbuffer_camera.far_plane : 0);
    if (command.mode == material_albedo_view_mode ||
        command.mode == material_specular_view_mode ||
        editor_material_mode(command.mode) || lighting_mode(command.mode) ||
        !command.decals.empty()) {
      m_gbuffer->begin_material();
      backend.set_viewport(x, y, w, h);
      backend.set_scissor(left, top, u32(std::ceil(x + w)) - left,
                          u32(std::ceil(y + h)) - top);
      if (!command.models.empty())
        m_models->draw(command.models, camera, gbuffer_camera.far_plane, true,
                       false, false, false, mat_phase);
      if (!command.terrain.empty())
        m_terrain->draw(command.terrain, camera, gbuffer_camera.far_plane,
                        true);
    }
    forward_lighting_context fl;
    fl.ambient_color = {command.environment.ambient[0],
                        command.environment.ambient[1],
                        command.environment.ambient[2],
                        gbuffer ? gbuffer_camera.far_plane : 1000.f};
    fl.viewport = {float(size.x), float(size.y), rect.min.x, rect.min.y};
    for (auto const &l : command.lights) {
      if (l.type == scene_light_type::directional) {
        fl.light_direction = {l.direction[0], l.direction[1], l.direction[2],
                              1.f};
        fl.light_color = {l.color[0] * l.diffuse, l.color[1] * l.diffuse,
                          l.color[2] * l.diffuse, l.intensity};
        break;
      }
    }
    if (fl.light_direction[3] == 0.f && !command.lights.empty()) {
      auto const &l = command.lights.front();
      fl.light_direction = {l.direction[0], l.direction[1], l.direction[2],
                            1.f};
      fl.light_color = {l.color[0] * l.diffuse, l.color[1] * l.diffuse,
                        l.color[2] * l.diffuse, l.intensity};
    }
    std::function<void(rhi::rhi_texture_handle, u32)> forward;
    if (!command.decals.empty()) {
      math::float4x4 camera_view;
      for (unsigned r = 0; r < 4; ++r)
        for (unsigned c = 0; c < 4; ++c)
          camera_view.elements[c][r] = camera.view[r * 4 + c];
      m_gbuffer->draw_decals(command.decals, gbuffer_camera, camera_view, rect,
                             size.x, size.y);
    }
    if (!command.particles.empty() && !gbuffer) {
      m_particles->draw(command.particles, camera);
      used_particles = true;
    }
    if (!command.transparent.empty() && !gbuffer)
      m_models->draw(command.transparent, camera, 0, false, false, true, false,
                     mat_phase, false, unlit_view_mode, 0, 0, 0, 1, fl);
    if (!command.debug.empty() && command.mode == unlit_view_mode) {
      m_debug->draw(command.debug, camera);
      used_debug = true;
    }
    if (!command.transparent.empty() || !command.debug.empty() ||
        !command.particles.empty())
      forward = [&](rhi::rhi_texture_handle scene_depth, u32 depth_samples) {
        if (!command.transparent.empty())
          m_models->draw(command.transparent, camera, 0, false, false, true,
                         false, mat_phase, false, unlit_view_mode, 0, 0,
                         scene_depth, depth_samples, fl);
        if (!command.particles.empty()) {
          m_particles->draw(command.particles, camera);
          used_particles = true;
        }
        if (m_models && (m_models->has_skinning(command.models) || m_models->has_skinning(command.casters) || m_models->has_skinning(command.transparent) || m_models->has_skinning(command.selected))) {
            backend.end_render_pass();
            m_models->run_skinning(command.models);
            m_models->run_skinning(command.casters);
            m_models->run_skinning(command.transparent);
            m_models->run_skinning(command.selected);
            backend.begin_presentation_pass(true);
        }
        if (!command.debug.empty()) {
          m_debug->draw(command.debug, camera);
          used_debug = true;
        }
      };
    if (gbuffer)
      m_gbuffer->finish(
          command.mode, gbuffer_camera, rect, size.x, size.y, command.lights,
          command.environment,
          [&](size_t i) {
            used_shadows = true;
            return m_shadows->render(command.shadows.at(i), command.casters,
                                     m_models.get());
          },
          sky, forward, command.postprocess, temporal,
          command.distortions.empty()
              ? std::function<void()>{}
              : std::function<void()>{[&] {
                  m_models->draw(command.distortions, camera, 0, false, false,
                                 false, true,
                                 command.postprocess.distortion_time);
                }});
    if (!command.selected.empty())
      state.selection->draw(command.selected, *m_models, camera,
                            command.selection, rect, size.x, size.y);
    if (!command.debug.empty() && command.mode != unlit_view_mode &&
        command.mode != lit_view_mode &&
        command.mode != lit_with_histogram_view_mode) {
      m_debug->draw(command.debug, camera, true);
      used_debug = true;
    }
  }
  // UI is a display-space overlay: never feed it into scene history or HDR
  // passes.
  for (auto const &command : m_draws)
    if (output(command.output).presentation() == destination &&
        !command.ui.empty()) {
      auto size = output(command.output).client_size();
      if (!size.x || !size.y)
        continue;
      auto const &rect = command.viewport;
      m_ui->draw(command.ui,
                 {rect.min.x * size.x, rect.min.y * size.y,
                  (rect.max.x - rect.min.x) * size.x,
                  (rect.max.y - rect.min.y) * size.y},
                 size.x, size.y);
      used_ui = true;
    }
  state.used_gbuffer = used_gbuffer;
  state.used_shadows = used_shadows;
  state.used_taa = used_taa;
  m_used_debug |= used_debug;
  m_used_ui |= used_ui;
  m_used_particles |= used_particles;
}
void scene_runtime::finish_draw_frame() {
  m_draws.clear();
  clear_particle_frame();
  m_tree_frame_bytes = 0;
  m_debug_bytes = m_debug_batches = m_ui_bytes = m_ui_batches = 0;
  if (m_used_debug)
    m_debug->collect();
  else
    m_debug.reset();
  if (m_used_ui)
    m_ui->collect();
  else
    m_ui.reset();
  if (m_used_particles)
    m_particles->collect();
  else
    m_particles.reset();
  if (m_models)
    m_models->collect();
  if (m_terrain)
    m_terrain->collect();
  for (auto it = m_outputs.begin(); it != m_outputs.end();) {
    auto &state = it->second;
    if (state.output->closed || state.output->close_requested) {
      it = m_outputs.erase(it);
      continue;
    }
    if (state.touched) {
      if (state.gbuffer) {
        state.gbuffer->collect_sky();
        state.gbuffer->collect_decals();
      }
      if (!state.used_gbuffer)
        state.gbuffer.reset();
      if (!state.used_shadows)
        state.shadows.reset();
      if (state.used_taa)
        state.taa->collect(state.presented);
      else
        state.taa.reset();
    }
    ++it;
  }
}

void scene_runtime::debug(scene_ptr const &target,
                          std::span<vertex_colored const> vertices,
                          std::span<u16 const> indices, bool triangles) {
  scene(target);
  if (!m_debug_capture || target != m_debug_scene)
    throw std::runtime_error(
        "Debug primitives require the matching draw_scene CPU callback");
  auto batch = snapshot_debug(vertices, indices, triangles, m_debug_depth);
  if (batch.indices.empty())
    return;
  size_t bytes = m_debug_bytes + batch.bytes();
  for (auto const &item : *m_debug_capture)
    bytes += item->bytes();
  if (bytes > debug_frame_bytes ||
      m_debug_batches + m_debug_capture->size() >= debug_frame_batches)
    throw std::runtime_error(
        "Debug frame budget exceeded (8 MiB / 256 batches)");
  m_debug_capture->push_back(
      std::make_shared<debug_batch const>(std::move(batch)));
}
void scene_runtime::set_sky_material(
    scene_ptr const &value, resources::unmanaged_resource_ptr const &material) {
  auto &target = scene(value);
  auto *sky = dynamic_cast<sky_material_resource *>(material.c_ptr());
  if (material && (!sky || sky->owner != m_owner))
    throw std::runtime_error("Sky material belongs to another session or type");
  target.sky_material = material;
  ++target.temporal_revision;
}
void scene_runtime::set_font_texture(
    scene_view_ptr const &target,
    resources::unmanaged_resource_ptr const &value) {
  auto &destination = view(target);
  auto *resource = dynamic_cast<texture_resource *>(value.c_ptr());
  if (value && (!resource || resource->owner != m_owner))
    throw std::runtime_error(
        "UI font texture belongs to another session or type");
  if (resource) {
    auto const &desc = resource->snapshot()->description;
    if (desc.type != rhi::texture_type::type_2d || desc.array_layers != 1 ||
        desc.samples != 1)
      throw std::runtime_error("UI requires a single-sample 2D texture");
  }
  destination.ui_font_texture = value;
}
void scene_runtime::ui(scene_view_ptr const &target,
                       std::span<render::ui::vertex const> vertices,
                       int primitives, int points,
                       render::ui::draw_options const &options) {
  if (!m_ui_capture || target != m_ui_view || m_debug_depth)
    throw std::runtime_error(
        "UI requires the matching scene-view overlay callback");
  auto &destination = view(target);
  auto batch = snapshot_ui(vertices, primitives, points, options);
  if (batch.indices.empty())
    return;
  if (batch.mode != 2) {
    auto value = options.texture   ? options.texture
                 : batch.mode == 0 ? destination.ui_font_texture
                                   : resources::unmanaged_resource_ptr{};
    auto *resource = dynamic_cast<texture_resource *>(value.c_ptr());
    if (!resource || resource->owner != m_owner)
      throw std::runtime_error("UI requires a texture from the active session "
                               "(bind the font atlas explicitly)");
    batch.texture =
        batch.mode == 0 ? resource->linear_snapshot() : resource->snapshot();
    auto const &desc = batch.texture->description;
    if (desc.type != rhi::texture_type::type_2d || desc.array_layers != 1 ||
        desc.samples != 1)
      throw std::runtime_error("UI requires a single-sample 2D texture");
  }
  batch.options.texture = nullptr;
  size_t bytes = m_ui_bytes + batch.bytes();
  for (auto const &item : *m_ui_capture)
    bytes += item->bytes();
  if (bytes > ui_frame_bytes ||
      m_ui_batches + m_ui_capture->size() >= ui_frame_batches)
    throw std::runtime_error("UI frame budget exceeded (8 MiB / 256 batches)");
  m_ui_capture->push_back(std::make_shared<ui_batch const>(std::move(batch)));
}
} // namespace vulkan
} // namespace render
} // namespace xray
