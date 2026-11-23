#pragma once
#include <xray/editor/world/engine.h>
#include <xray/editor/world/sdk_scene_bridge.h>
#include <xray/particle/world.h>
#include <xray/render/facade/decal_properties.h>
#include <xray/render/facade/editor_runtime.h>
#include <xray/render/facade/terrain_base.h>
#include <xray/render/vulkan/decal_resources.h>
#include <xray/render/vulkan/skeletal_resources.h>
#include <xray/render/vulkan/terrain_resources.h>
#include <xray/render/vulkan/vegetation_resources.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <map>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace xray::editor::hosting {
inline math::float4x4 orthonormalize_rigid(math::float4x4 const &m) {
  auto safe_norm = [](math::float3 v, math::float3 fallback) {
    float l = v.length();
    return (std::isfinite(l) && l > 1e-5f) ? v * (1.f / l) : fallback;
  };
  math::float3 x = safe_norm(m.i.xyz(), {1, 0, 0});
  math::float3 z = safe_norm(
      m.k.xyz() - x * math::dot_product(m.k.xyz(), x),
      std::abs(x.y) < 0.9f
          ? safe_norm(math::cross_product(x, {0, 1, 0}), {0, 0, 1})
          : safe_norm(math::cross_product({1, 0, 0}, x), {0, 0, 1}));
  math::float3 y = math::cross_product(z, x);
  if (math::dot_product(math::cross_product(x, y), z) < 0)
    y = -y;
  y = safe_norm(y, {0, 1, 0});
  z = math::cross_product(x, y);
  math::float4x4 out = math::float4x4().identity();
  out.i = {x.x, x.y, x.z, 0.f};
  out.j = {y.x, y.y, y.z, 0.f};
  out.k = {z.x, z.y, z.z, 0.f};
  out.c = {m.c.x, m.c.y, m.c.z, 1.f};
  return out;
}

class sdk_runtime {
  struct runtime_tree {
    render::speedtree_instance_ptr tree;
    math::float4x4 transform;
    resources::unmanaged_resource_ptr bark, frond, leaf;
  };
  struct runtime_particle {
    particle::particle_system_instance_ptr instance;
    math::float4x4 transform;
    bool visible = true, paused = false;
  };
  struct runtime_terrain_input {
    std::string virtual_path;
    std::vector<std::uint8_t> bytes;
  };
  struct runtime_particle_input {
    std::string uri;
    math::float4x4 transform;
    bool visible = true, paused = false;
  };
  struct candidate {
    unsigned pending = 0;
    bool failed = false, cancelled = false;
    render::scene_ptr scene;
    render::scene_view_ptr view;
    std::vector<std::pair<render::render_model_instance_ptr, math::float4x4>>
        models;
    std::vector<std::pair<render::render_model_instance_ptr, math::float4x4>>
        skinned_models;
    std::map<std::uint64_t, render::render_model_instance_ptr> object_models;
    std::vector<runtime_terrain_input> terrain_inputs;
    std::vector<render::render_model_instance_ptr> terrain_models;
    std::vector<runtime_tree> trees;
    std::vector<runtime_particle_input> particle_inputs;
    std::vector<runtime_particle> particles;
    std::vector<render::decal_properties> decals;
    resources::unmanaged_resource_ptr sky_material;
    std::vector<std::shared_ptr<std::string>> retained_strings;
    std::vector<render::light_props> lights;
    std::vector<render::editor_runtime_body> bodies;
    std::vector<render::editor_runtime_terrain_body> terrain_bodies;
    sdk_runtime_settings settings{};
  };
  std::shared_ptr<candidate> prepared;
  bool active = false;
  u64 generation = 0;
  sdk_runtime_status last{1, sizeof(last)};
  static render::editor_runtime_request
  settings(sdk_runtime_settings const &s) {
    if (s.version != 1 || s.size != sizeof(s) || s.reserved || (s.flags & ~3u))
      throw std::runtime_error("Invalid runtime settings");
    render::editor_runtime_request r;
    r.simulate = s.flags & 1;
    r.paused = s.flags & 2;
    std::memcpy(&r.camera, s.camera, sizeof(r.camera));
    std::memcpy(&r.projection, s.projection, sizeof(r.projection));
    for (auto const &matrix : {r.camera, r.projection})
      for (auto const &row : matrix.elements)
        for (float f : row)
          if (!std::isfinite(f))
            throw std::runtime_error("Nonfinite runtime view");
    if (r.camera.e03 || r.camera.e13 || r.camera.e23 || r.camera.e33 != 1 ||
        std::abs(r.camera.i.xyz().length() - 1) > 1e-3f ||
        std::abs(r.camera.j.xyz().length() - 1) > 1e-3f ||
        std::abs(r.camera.k.xyz().length() - 1) > 1e-3f ||
        std::abs(math::dot_product(r.camera.i.xyz(), r.camera.j.xyz())) >
            1e-3f ||
        std::abs(math::dot_product(r.camera.i.xyz(), r.camera.k.xyz())) >
            1e-3f ||
        std::abs(math::dot_product(r.camera.j.xyz(), r.camera.k.xyz())) >
            1e-3f ||
        r.projection.e00 <= 0 || r.projection.e11 <= 0 ||
        r.projection.e23 != 1 || r.projection.e33 != 0)
      throw std::runtime_error("Invalid runtime camera/projection");
    return r;
  }
  void publish(render::editor_runtime_request const &r) {
    last = {1,         sizeof(last), r.state,   r.state == 4 ? 1u : 0u,
            r.ticks,   r.frames,     r.seconds, r.logic_thread,
            r.objects, generation,   0};
  }

public:
  bool running() const { return active; }
  template <class Objects, class TerrainCells>
  int request(sdk_scene_request &r, render::control_level::control_world &level,
              editor::engine &engine, memory::base_allocator &allocator,
              Objects const &objects, TerrainCells const &terrain_cells,
              resources::unmanaged_resource_ptr const &live_sky_material) {
    if (r.count || r.query || !r.result)
      return -1;
    if (r.operation == 37) {
      if (r.data || !active)
        return -1;
      render::editor_runtime_request request;
      request.operation = 6;
      if (!engine.editor_runtime(request))
        return -1;
      *static_cast<sdk_runtime_physics_status *>(r.result) = {
          1,
          sizeof(sdk_runtime_physics_status),
          request.physics_bodies,
          request.physics_dynamic,
          request.physics_minimum_y,
          request.physics_maximum_y,
          request.ticks};
      return 0;
    }
    if (r.operation == 30) {
      if (!r.data || active || prepared)
        return -1;
      auto input = *static_cast<sdk_runtime_settings const *>(r.data);
      settings(input);
      for (auto const &[id, o] : objects)
        if ((o.value.flags & 1) && !o.value.kind && o.ext.world_kind == 0)
          throw std::runtime_error(
              "Runtime cannot execute an unresolved/unsupported object; repair "
              "its dependency first");
      auto next = std::make_shared<candidate>();
      next->settings = input;
      next->sky_material = live_sky_material;
      for (auto const &cell : terrain_cells) {
        if (cell.collision) {
          auto verts = cell.collision->vertices();
          auto inds = cell.collision->indices();
          render::editor_runtime_terrain_body tb;
          tb.vertices.assign(verts.begin(), verts.end());
          tb.indices.assign(inds.begin(), inds.end());
          tb.transform =
              cell.data ? cell.data->transform : math::float4x4().identity();
          next->terrain_bodies.push_back(std::move(tb));
        }
        if (!cell.bytes.empty()) {
          next->terrain_inputs.push_back({cell.virtual_path, cell.bytes});
        }
      }
      bool any_visible_particle = false;
      for (auto const &[id, o] : objects)
        if ((o.value.flags & 1) && o.ext.world_kind == 5 &&
            !o.primary_uri.empty()) {
          any_visible_particle = true;
          next->particle_inputs.push_back(
              {o.primary_uri, o.transform, (o.ext.flags & 1) != 0,
               (o.ext.flags & 4) != 0 || (o.ext.flags & 2) == 0});
        }
      next->terrain_models.resize(next->terrain_inputs.size());
      next->particles.resize(next->particle_inputs.size());

      render::vulkan::scene_configuration scene_cfg;
      scene_cfg.features.m_create_terrain = !next->terrain_inputs.empty();
      scene_cfg.features.m_create_particle_world = any_visible_particle;
      scene_cfg.terrain_memory =
          scene_cfg.features.m_create_terrain
              ? render::terrain_memory_profile::extended_384_mib
              : render::terrain_memory_profile::standard_256_mib;
      scene_cfg.particle_fixed_timestep =
          any_visible_particle ? (1.f / 60.f) : 0.f;
      auto scene_user = std::make_shared<resources::user_data_variant>();
      scene_user->set(scene_cfg);
      resources::user_data_variant const *scene_users[] = {scene_user.get(),
                                                           nullptr};

      resources::creation_request create[] = {
          {"sdk_runtime_scene", const_buffer("", 1), resources::scene_class},
          {"sdk_runtime_view", const_buffer("", 1),
           resources::scene_view_class}};
      ++next->pending;
      resources::query_create_resources(
          create, 2,
          [next, scene_user, any_visible_particle, &allocator,
           &renderer = level.editor_scene_renderer()](
              resources::queries_result &q) {
            if (next->cancelled) {
              --next->pending;
              return;
            }
            if (!q.is_successful()) {
              next->failed = true;
              --next->pending;
              return;
            }
            next->scene = dynamic_cast<render::base_scene *>(
                q[0].get_unmanaged_resource().c_ptr());
            next->view = dynamic_cast<render::base_scene_view *>(
                q[1].get_unmanaged_resource().c_ptr());
            if (!next->scene || !next->view) {
              next->failed = true;
              --next->pending;
              return;
            }
            for (size_t ci = 0; ci < next->terrain_inputs.size(); ++ci) {
              auto const &ti = next->terrain_inputs[ci];
              ++next->pending;
              auto proj = std::make_shared<std::string>(
                  "resources/projects/alex");
              next->retained_strings.push_back(proj);
              auto cook_user =
                  std::make_shared<resources::user_data_variant>();
              cook_user->set(render::terrain_cook_user_data{
                  next->scene, proj->c_str(),
                  render::terrain_memory_profile::extended_384_mib});
              resources::user_data_variant const *t_users[] = {cook_user.get()};
              resources::creation_request t_req(
                  ti.virtual_path.c_str(),
                  const_buffer(ti.bytes.data(), ti.bytes.size()),
                  resources::terrain_instance_class);
              resources::query_create_resources(
                  &t_req, 1,
                  [next, ci, cook_user](resources::queries_result &tq) {
                    --next->pending;
                    if (next->cancelled)
                      return;
                    if (!tq.is_successful()) {
                      next->failed = true;
                      return;
                    }
                    auto *inst =
                        dynamic_cast<render::vulkan::terrain_instance_resource *>(
                            tq[0].get_unmanaged_resource().c_ptr());
                    if (!inst) {
                      next->failed = true;
                      return;
                    }
                    next->terrain_models[ci] = inst;
                  },
                  &allocator, t_users, nullptr, assert_on_fail_false);
            }
            if (any_visible_particle) {
              auto pworld = renderer.particle_world_handle(next->scene);
              if (!pworld) {
                next->failed = true;
                --next->pending;
                return;
              }
              for (size_t pi = 0; pi < next->particle_inputs.size(); ++pi) {
                auto const &pin = next->particle_inputs[pi];
                ++next->pending;
                auto puser = std::make_shared<resources::user_data_variant>();
                puser->set(pworld);
                resources::query_resource(
                    pin.uri.c_str(),
                    resources::particle_system_instance_class,
                    [next, pi, pin, puser](resources::queries_result &pq) {
                      --next->pending;
                      if (next->cancelled)
                        return;
                      if (!pq.is_successful()) {
                        next->failed = true;
                        return;
                      }
                      particle::particle_system_instance_ptr inst =
                          pq[0].get_unmanaged_resource();
                      if (!inst) {
                        next->failed = true;
                        return;
                      }
                      next->particles[pi] = {inst, pin.transform, pin.visible,
                                             pin.paused};
                    },
                    &allocator, puser.get(), nullptr, assert_on_fail_false);
              }
            }
            --next->pending;
          },
          &allocator, scene_users, nullptr, assert_on_fail_false);
      for (auto const &[id, o] : objects)
        if (o.value.flags & 1) {
          if (auto *asset =
                  o.model
                      ? dynamic_cast<render::vulkan::static_model_resource *>(
                            o.model.c_ptr())
                      : nullptr;
              asset && asset->data && asset->data->inverse_bind_pose.empty()) {
            ++next->pending;
            resources::user_data_variant clone;
            clone.set(render::vulkan::static_model_clone_request{o.model});
            resources::user_data_variant const *user = &clone;
            resources::creation_request create(
                "sdk_runtime_instance", const_buffer("", 1),
                resources::static_render_model_instance_class);
            auto data = level.registry()->model_override(asset->data->identity);
            if (!data)
              data = asset->data;
            resources::query_create_resources(
                &create, 1,
                [next, id, transform = o.transform, source = o.model,
                 physics = data->physics](resources::queries_result &q) {
                  --next->pending;
                  if (next->cancelled)
                    return;
                  if (!q.is_successful()) {
                    next->failed = true;
                    return;
                  }
                  auto *model =
                      dynamic_cast<render::vulkan::static_model_resource *>(
                          q[0].get_unmanaged_resource().c_ptr());
                  if (!model || model == source.c_ptr()) {
                    next->failed = true;
                    return;
                  }
                  next->models.emplace_back(model, transform);
                  next->object_models[id] = model;
                  if (!physics.primitives.empty())
                    next->bodies.push_back({model, transform, physics});
                },
                &allocator, &user, nullptr, assert_on_fail_false);
          } else if (o.ext.world_kind == 2 && !o.primary_uri.empty()) {
            ++next->pending;
            resources::query_resource(
                o.primary_uri.c_str(),
                resources::skeleton_render_model_instance_class,
                [next, id, transform = o.transform](
                    resources::queries_result &q) {
                  --next->pending;
                  if (next->cancelled)
                    return;
                  if (!q.is_successful()) {
                    next->failed = true;
                    return;
                  }
                  auto *model =
                      dynamic_cast<render::vulkan::static_model_resource *>(
                          q[0].get_unmanaged_resource().c_ptr());
                  if (!model || !model->data || model->data->surfaces.empty() ||
                      model->data->inverse_bind_pose.empty()) {
                    next->failed = true;
                    return;
                  }
                  next->skinned_models.emplace_back(model, transform);
                  next->object_models[id] = model;
                },
                &allocator, nullptr, nullptr, assert_on_fail_false);
          } else if (o.ext.world_kind == 4 && !o.primary_uri.empty()) {
            size_t ti = next->trees.size();
            next->trees.push_back({nullptr, o.transform, o.tree_bark_mat,
                                   o.tree_frond_mat, o.tree_leaf_mat});
            ++next->pending;
            resources::query_resource(
                o.primary_uri.c_str(), resources::speedtree_instance_class,
                [next, ti](resources::queries_result &q) {
                  --next->pending;
                  if (next->cancelled)
                    return;
                  if (!q.is_successful()) {
                    next->failed = true;
                    return;
                  }
                  auto *tree = dynamic_cast<render::speedtree_instance *>(
                      q[0].get_unmanaged_resource().c_ptr());
                  if (!tree) {
                    next->failed = true;
                    return;
                  }
                  next->trees[ti].tree = tree;
                },
                &allocator, nullptr, nullptr, assert_on_fail_false);
          } else if (o.ext.world_kind == 6 && o.decal_material) {
            render::decal_properties dp;
            dp.transform = orthonormalize_rigid(o.transform);
            dp.material = o.decal_material;
            dp.width_height_far_distance = {
                std::clamp(o.ext.decal_width, 0.001f, 128.f),
                std::clamp(o.ext.decal_height, 0.001f, 128.f),
                std::clamp(o.ext.decal_depth, 0.001f, 128.f)};
            dp.alpha_angle = -1.f;
            dp.clip_angle = -1.f;
            dp.projection_on_terrain_geometry = (o.ext.flags & 1) != 0;
            dp.projection_on_static_geometry = (o.ext.flags & 2) != 0;
            if (!dp.projection_on_terrain_geometry &&
                !dp.projection_on_static_geometry)
              dp.projection_on_terrain_geometry = true;
            dp.projection_on_speedtree_geometry =
                dp.projection_on_skeleton_geometry =
                    dp.projection_on_particle_geometry = false;
            next->decals.push_back(dp);
          } else if (o.value.kind == 2) {
            render::light_props light = {};
            unsigned lt = o.ext.world_kind == 3 ? o.ext.light_type : 0u;
            light.type = lt == 4                ? render::light_type_parallel
                         : (lt == 1 || lt == 6) ? render::light_type_spot
                                                : render::light_type_point;
            light.transform = o.transform;
            light.color = o.value.color;
            light.range = std::max(0.05f, o.value.range);
            light.intensity = std::clamp(o.value.intensity, 0.f, 16.f);
            light.diffuse_influence_factor = std::clamp(
                o.ext.world_kind == 3 ? o.ext.diffuse_factor : 1.f, 0.f, 1.f);
            light.specular_influence_factor = std::clamp(
                o.ext.world_kind == 3 ? o.ext.specular_factor : 1.f, 0.f, 1.f);
            light.attenuation_power = std::max(
                0.1f, o.ext.world_kind == 3 ? o.ext.attenuation_power : 2.f);
            light.spot_umbra_angle = std::clamp(
                o.ext.world_kind == 3 ? o.ext.spot_umbra_angle : 0.39f, 0.01f,
                3.0f);
            light.spot_penumbra_angle = std::clamp(
                std::max(light.spot_umbra_angle + 0.02f,
                         o.ext.world_kind == 3 ? o.ext.spot_penumbra_angle
                                               : 0.78f),
                0.03f, 3.1f);
            light.spot_falloff = std::max(
                0.05f, o.ext.world_kind == 3 ? o.ext.spot_falloff : 1.f);
            light.does_cast_shadows = false;
            light.shadower = false;
            light.use_with_lpv = false;
            next->lights.push_back(light);
          }
        }
      prepared = next;
      ++generation;
      last = {1,
              sizeof(last),
              1,
              0,
              0,
              0,
              0,
              0,
              unsigned(objects.size()),
              generation,
              0};
    } else if (r.operation == 31) {
      if (r.data)
        return -1;
      resources::dispatch_callbacks();
      if (active) {
        render::editor_runtime_request request;
        request.operation = 3;
        if (!engine.editor_runtime(request))
          return -1;
        publish(request);
      } else if (prepared)
        last.state = prepared->pending ? 1 : prepared->failed ? 6 : 2;
    } else if (r.operation == 32) {
      if (r.data || active || !prepared || prepared->pending ||
          prepared->failed)
        return -1;
      auto request = settings(prepared->settings);
      request.operation = 0;
      request.scene = prepared->scene;
      request.view = prepared->view;
      request.output = level.editor_output();
      request.objects = unsigned(
          prepared->models.size() + prepared->skinned_models.size() +
          prepared->lights.size() + prepared->trees.size() +
          prepared->particles.size() + prepared->decals.size());
      request.bodies = prepared->bodies;
      request.terrain_bodies = prepared->terrain_bodies;
      auto *editor_view = static_cast<render::vulkan::view_resource *>(
          level.editor_view().c_ptr());
      request.environment = editor_view->environment();
      request.postprocess = editor_view->postprocess();
      if (!prepared->trees.empty() || !prepared->particles.empty() ||
          !prepared->decals.empty()) {
        request.postprocess.msaa_samples = 1;
        request.postprocess.taa_enabled = false;
        request.postprocess.fxaa_enabled = false;
      }
      auto &renderer = level.editor_scene_renderer();
      for (auto const &tm : prepared->terrain_models)
        if (tm)
          renderer.terrain_add_cell(prepared->scene, tm);
      for (auto const &[model, transform] : prepared->models)
        renderer.add_model(prepared->scene, model, transform);
      for (auto const &[model, transform] : prepared->skinned_models)
        renderer.add_model(prepared->scene, model, transform);
      for (auto const &t : prepared->trees)
        if (t.tree) {
          renderer.add_speedtree_instance(prepared->scene, t.tree, t.transform,
                                          false);
          renderer.set_speedtree_instance_visible(t.tree, true);
          auto *ti =
              static_cast<render::vulkan::tree_instance *>(t.tree.c_ptr());
          auto *tm = static_cast<render::vulkan::static_model_resource *>(
              ti->model.c_ptr());
          for (auto const &s : tm->data->surfaces) {
            if (s.name == "branch" && t.bark)
              renderer.set_speedtree_instance_material(
                  t.tree, fs_new::virtual_path_string("branch"), t.bark);
            else if (s.name == "frond" && t.frond)
              renderer.set_speedtree_instance_material(
                  t.tree, fs_new::virtual_path_string("frond"), t.frond);
            else if ((s.name == "leafmesh" || s.name == "leafcard") && t.leaf)
              renderer.set_speedtree_instance_material(
                  t.tree, fs_new::virtual_path_string(s.name.c_str()), t.leaf);
          }
        }
      for (auto const &p : prepared->particles)
        if (p.instance) {
          renderer.play_particle_system(prepared->scene, p.instance,
                                        p.transform);
          renderer.update_particle_system_instance(
              prepared->scene, p.instance, p.transform, p.visible, p.paused);
        }
      unsigned decal_id = 1;
      for (auto const &dp : prepared->decals)
        renderer.add_decal(prepared->scene, decal_id++, dp);
      if (prepared->sky_material)
        renderer.set_sky_material(prepared->scene, prepared->sky_material);
      unsigned id = 1;
      for (auto const &light : prepared->lights)
        renderer.add_light(prepared->scene, id++, light);
      if (!engine.editor_runtime(request))
        throw std::runtime_error(
            "Game world does not support authored runtime sessions");
      active = true;
      level.runtime_rendering(true);
      publish(request);
    } else if (r.operation == 33) {
      if (!r.data || !active)
        return -1;
      auto request =
          settings(*static_cast<sdk_runtime_settings const *>(r.data));
      request.operation = 1;
      if (!engine.editor_runtime(request))
        return -1;
      publish(request);
    } else if (r.operation == 34) {
      if (r.data)
        return -1;
      close(level, engine);
    } else if (r.operation == 35) {
      if (r.data || !active)
        return -1;
      render::editor_runtime_request request;
      request.operation = 4;
      if (!engine.editor_runtime(request))
        return -1;
      publish(request);
    } else if (r.operation == 36) {
      if (!r.data || !active || *static_cast<u32 const *>(r.data) > 1)
        return -1;
      render::editor_runtime_request request;
      request.operation = 5;
      request.paused = !*static_cast<u32 const *>(r.data);
      if (!engine.editor_runtime(request))
        return -1;
      publish(request);
    } else if (r.operation == 38) {
      if (!r.data || !active || !prepared)
        return -1;
      auto const &pose = *static_cast<sdk_runtime_pose const *>(r.data);
      if (pose.version != 1 || pose.size != sizeof(sdk_runtime_pose) ||
          (pose.flags & ~3u) != 0 || pose.flags == 0)
        return -1;
      auto found = prepared->object_models.find(pose.object);
      if (found == prepared->object_models.end() || !found->second)
        return -1;
      if (pose.flags & 1u) {
        for (float f : pose.transform)
          if (!std::isfinite(f))
            return -1;
        math::float4x4 xf;
        std::memcpy(&xf, pose.transform, sizeof(xf));
        level.editor_scene_renderer().update_model(prepared->scene,
                                                   found->second, xf);
      }
      if (pose.flags & 2u) {
        if (!pose.matrices || pose.count == 0 || pose.count > 80)
          return -1;
        auto *model = dynamic_cast<render::vulkan::static_model_resource *>(
            found->second.c_ptr());
        if (!model || !model->data ||
            model->data->inverse_bind_pose.size() != pose.count)
          return -1;
        std::vector<math::float4x4> matrices(pose.count), palette;
        std::memcpy(matrices.data(), pose.matrices,
                    matrices.size() * sizeof(math::float4x4));
        render::vulkan::validate_skeleton_pose(matrices.data(), pose.count);
        palette.reserve(pose.count);
        for (unsigned i = 0; i < pose.count; ++i)
          palette.push_back(model->data->inverse_bind_pose[i] * matrices[i]);
        for (auto const &surface : model->data->surfaces) {
          if (!surface.skin)
            return -1;
          render::vulkan::skin_skeletal_mesh(*surface.skin, palette);
        }
        level.editor_scene_renderer().update_skeleton(
            found->second, matrices.data(), pose.count);
      }
    } else
      return -1;
    *static_cast<sdk_runtime_status *>(r.result) = last;
    return 0;
  }
  void close(render::control_level::control_world &level,
             editor::engine &engine) {
    if (active) {
      render::editor_runtime_request request;
      request.operation = 2;
      if (!engine.editor_runtime(request))
        throw std::runtime_error("Runtime stop rejected");
      publish(request);
    }
    active = false;
    level.runtime_rendering(false);
    if (prepared)
      prepared->cancelled = true;
    prepared.reset();
    last.state = 5;
  }
};
} // namespace xray::editor::hosting
