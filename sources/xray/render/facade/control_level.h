#pragma once
#include <xray/engine/engine_user.h>
#include <xray/engine/capture_control_bus.h>
#include <xray/render/vulkan/platform_surface.h>
#include <xray/render/facade/editor_renderer.h>
#include <xray/render/facade/editor_runtime.h>
#include <xray/engine/m8_control_protocol.h>
#include <xray/engine/m9_performance_protocol.h>
#include <xray/input_event_queue.h>
#include <xray/input/mouse.h>
#include <xray/particle/world.h>
#include <xray/configs_checked_binary.h>
#include <xray/configs_lua_config.h>
#include <xray/render/engine/world.h>
#include <xray/resources_pinned_ptr.h>
#include <xray/render/world.h>
#include <xray/render/facade/game_renderer.h>
#include <xray/render/facade/scene_renderer.h>
#include <xray/render/facade/model.h>
#include <xray/render/facade/terrain_base.h>
#include <xray/render/facade/decal_properties.h>
#include <xray/render/facade/postprocess_settings.h>
#include <xray/render/facade/lighting_environment.h>
#include <xray/render/vulkan/scene_resources.h>
#include <xray/render/vulkan/terrain_resources.h>
#include <xray/render/vulkan/model_resources.h>
#include <xray/render/vulkan/sky_resources.h>
#include <xray/render/facade/ui_renderer.h>
#include <xray/render/facade/debug_renderer.h>
#include <xray/animation/sources/skeleton_cook.h>
#include <xray/animation/sources/skeleton_animation_cook.h>
#include <xray/animation/sources/cubic_spline_skeleton_animation_cook.h>
#include <xray/animation/animation_player.h>
#include <xray/animation/instant_interpolator.h>
#include <xray/animation/linear_interpolator.h>
#include <xray/animation/mixing_animation_lexeme.h>
#include <xray/animation/mixing_animation_lexeme_parameters.h>
#include <xray/animation/mixing_weight_lexeme.h>
#include <xray/animation/mixing_expression.h>
#include <xray/animation/mixing_math.h>
#include <cmath>
#include <chrono>
#include <iostream>
#include <memory>
#include <set>
#include <stdexcept>
#include <vector>
#include <algorithm>
#include <cstring>
#include <cstdlib>
#include <SDL3/SDL_scancode.h>

namespace xray::render::control_level {
enum class level_profile { control, test_range };

namespace {
using namespace xray;using namespace render;
namespace checked=configs::checked;
namespace protocol=xray::engine::m8_control;
using value=configs::binary_config_value;
void require(bool condition,pcstr message) {if (!condition) throw std::runtime_error(message);}
std::string text(value const& parent,pcstr key) {return pcstr(checked::field(parent,key,configs::t_string));}
math::float3 position(value const& parent,pcstr key) {
    auto v=math::float3(checked::field(parent,key,configs::t_float3));
    require(std::isfinite(v.x) && std::isfinite(v.y) && std::isfinite(v.z),"Nonfinite scene vector");return v;
}

// Shared manifest level, with the caller's game/editor facade and cook owner.
// Legacy NPC/RTP/UI scenes do not consume this format.
class control_world final : public engine_user::world {
    engine_user::engine& engine;
    render::world& api;
    memory::base_allocator* allocator;
    u32 const owner=threading::current_thread_id();
    bool stopping=false,ready=false,failed=false,enabled=true,decal_added=false;
    unsigned pending=0,ticks=0;
    unsigned generation=0;
    bool const workflow,editor,output_workflow,sdk_capture,sdk_blank_scene,test_range;
    u64 sdk_revision=0;
    unsigned cancelled_preview_queries=0;
    bool actor_attached=false,terrain_attached=true,unloading=false,held_transaction=false,late_returned=false;
    bool tree_translated=false,decal_translated=false,actor_transitioning=false,replay_loading=false;
    unsigned replay_ticks=0;
    uintptr_t awaiting=0;
    protocol::shot current_shot=protocol::shot::overview;
    std::chrono::steady_clock::time_point unload_deadline;
    std::shared_ptr<vulkan::scene_registry> unload_registry;
    scene_ptr pending_scene;
    particle::world_ptr pending_world;
    scene_ptr target;scene_view_ptr view;render_output_window_ptr output;
    scene_ptr sdk_project_scene;
    bool sdk_runtime_suppressed=false;
    math::float4x4 sdk_project_projection=math::float4x4().identity();
    scene_view_mode sdk_view_mode=lit_view_mode;
    bool sdk_view_configured=false,sdk_view_dirty=false,sdk_freeze=false,sdk_frozen_camera=false,sdk_postprocess=true,sdk_ssao=false,sdk_distortion=false,sdk_fxaa=false;
    scene_ptr preview_scene;scene_view_ptr preview_view;render_output_window_ptr preview_output;
    render_model_instance_ptr preview_model,woodbox_model;
    platform::native_surface_handle preview_window{};
    bool preview_loading=false,preview_closing=false,preview_close_queued=false;
    bool preview_main_paused=false;
    unsigned preview_main_redraws=0;
    std::shared_ptr<vulkan::scene_registry> preview_registry;
    bool preview_releasing=false;
    std::unique_ptr<checked::checked_config> project;
    std::vector<terrain_model_ptr> terrain;
    struct terrain_origin {terrain_model_ptr model;std::shared_ptr<vulkan::terrain_cell_data const> cell;};
    std::vector<terrain_origin> terrain_origins;
    terrain_model_ptr editor_cell;
    std::vector<std::string> editor_layers;
    unsigned editor_dominant=0;
    bool editor_props_hidden=false,editor_props_moved=false,editor_overlay=false,editor_tail=false,editor_isolated=false;
    bool editor_action_applied=false,editor_binding_pending=false;
    unsigned editor_binding_operation=0;
    u64 editor_binding_expected=0;
    vulkan::terrain_binding_statistics editor_binding_before;
    std::chrono::steady_clock::time_point editor_binding_deadline;
    struct placed_model {render_model_instance_ptr value;math::float4x4 transform;bool opaque,attached=true;};
    std::vector<placed_model> models;
    struct range_target {
        render_model_instance_ptr model;
        math::float4x4 upright;
        float half_width, height;
        bool hit=false;
    };
    std::vector<range_target> range_targets;
    std::vector<render_model_instance_ptr> range_held_models;
    render_model_instance_ptr range_flash;
    bool range_flash_attached=false;
    static constexpr float range_flash_duration=.085f;
    static constexpr float range_recoil_duration=.19f;
    float range_shot_age=range_recoil_duration;
    std::array<bool,4> range_movement{};
    float range_yaw=math::pi,range_pitch=-.17f;
    std::chrono::steady_clock::time_point range_last_tick{};
    std::vector<speedtree_instance_ptr> trees;
    std::vector<math::float4x4> tree_placements;
    particle::world_ptr particles;
    std::vector<particle::particle_system_instance_ptr> emitters;
    std::vector<math::float4x4> emitter_placements;
    std::vector<std::string> emitter_ids;
    std::unique_ptr<decal_properties> decal_state;
    postprocess_settings base_settings;
    std::vector<resources::unmanaged_resource_ptr> materials;
    skeleton_model_ptr actor;
    std::array<animation::skeleton_animation_ptr,2> clips;
    animation::skeleton_cook skeleton_cook;
    animation::cubic_spline_skeleton_animation_cook cubic_cook;
    animation::skeleton_animation_cook clip_cook;
    std::unique_ptr<animation::animation_player> player;
    math::float4x4 actor_transform=math::float4x4().identity(),camera=math::float4x4().identity();
    float fov=60,near_plane=.1f,far_plane=256;
    math::float2 main_projection_size={960,640};
    scene_renderer& scene() {return editor ? api.editor_renderer().scene() : api.game_renderer().scene();}
    pcstr owner_label() const {return editor ? "editor_thread=" : "logic_thread=";}
    void fail(std::exception const& error) {
        if (failed) return;
        failed=true;stopping=true;
        std::cerr<<"M8 control level failed: "<<error.what()<<" "<<owner_label()<<owner<<'\n';engine.exit(2);
    }
    template<class F> auto callback(F operation,std::string context="scene/view/output") {
        ++pending;
        return [this,operation,context](resources::queries_result& result) {
            try {
                require(threading::current_thread_id()==owner,"Control callback changed owner thread");
                if(stopping && (output_workflow || sdk_capture) && context=="model preview scene/view/output") {
                    require(!result.is_successful(),"Pending preview published resources after shutdown");
                    ++cancelled_preview_queries;
                    std::cerr<<"M8 outputs pending preview rejected: editor_thread="<<owner<<'\n';
                }
                if (!stopping) {
                    if (!result.is_successful()) throw std::runtime_error("Control resource query failed: "+context);
                    operation(result);
                }
            } catch (std::exception const& error) {fail(error);}
            --pending;
        };
    }
    template<class F> void query(std::string const& path,resources::class_id_enum id,F operation,resources::user_data_variant const* data=nullptr) {
        resources::query_resource(path.c_str(),id,callback(operation,path),allocator,data,nullptr,assert_on_fail_false);
    }
    void create_blank_scene() {
        vulkan::scene_configuration config{};
        if (test_range) config.features.m_create_terrain=true;
        resources::user_data_variant data,window;data.set(config);window.set(engine.get_render_window_handle());
        resources::user_data_variant const* users[]={&data,nullptr,&window};
        resources::creation_request requests[]={{"m8_game_scene",const_buffer("",1),resources::scene_class},
            {"m8_game_view",const_buffer("",1),resources::scene_view_class},{"m8_game_output",const_buffer("",1),resources::render_output_window_class}};
        resources::query_create_resources(requests,3,callback([this](resources::queries_result& q) {
            target=dynamic_cast<base_scene*>(q[0].get_unmanaged_resource().c_ptr());view=dynamic_cast<base_scene_view*>(q[1].get_unmanaged_resource().c_ptr());
            output=dynamic_cast<base_output_window*>(q[2].get_unmanaged_resource().c_ptr());require(target && view && output,"Scene resources missing");
            if (test_range) load_test_range();
        }),allocator,users,nullptr,assert_on_fail_false);
    }
    void place_range_model(pcstr asset,math::float3 position,float yaw=0,float half_width=0,float height=0) {
        auto transform=math::create_rotation_y(yaw)*math::create_translation(position);
        query(asset,resources::static_render_model_instance_class,[this,transform,half_width,height](resources::queries_result& q) {
            render_model_instance_ptr model=dynamic_cast<render_model_instance*>(q[0].get_unmanaged_resource().c_ptr());
            require(bool(model),"Test range model missing");
            scene().add_model(target,model,transform);
            models.push_back({model,transform,true});
            if (half_width>0) range_targets.push_back({model,transform,half_width,height});
        });
    }
    void load_test_range() {
        vulkan::texture_configuration sky_texture{};
        sky_texture.srgb=true;
        sky_texture.source_cube=true;
        sky_texture.source_dimension=3;
        resources::user_data_variant texture_data;
        texture_data.set(sky_texture);
        query("resources/textures/test/range_sky.tga",resources::texture_class,[this](resources::queries_result& q) {
            vulkan::sky_material_configuration sky{};
            sky.texture=q[0].get_unmanaged_resource();
            sky.intensity=4.0f;
            resources::user_data_variant material_data;
            material_data.set(sky);
            resources::user_data_variant const* user=&material_data;
            resources::creation_request request("test_range_sky",const_buffer("",1),resources::material_class);
            resources::query_create_resources(&request,1,callback([this](resources::queries_result& materials_result) {
                auto sky_material=materials_result[0].get_unmanaged_resource();
                scene().set_sky_material(target,sky_material);
                materials.push_back(sky_material);
            },"test range sky"),allocator,&user,nullptr,assert_on_fail_false);
        },&texture_data);

        for (pcstr path : {"resources/models/item/weapon/ak74/ak74_4tir1.model/render",
                          "resources/models/test_range/sleeves.model/render"}) {
            query(path,resources::static_render_model_instance_class,[this](resources::queries_result& q) {
                render_model_instance_ptr model=dynamic_cast<render_model_instance*>(q[0].get_unmanaged_resource().c_ptr());
                require(bool(model),"Missing first-person model");
                range_held_models.push_back(model);
            });
        }
        query("resources/models/test_range/shot_flash.model/render",resources::static_render_model_instance_class,
            [this](resources::queries_result& q) {
                range_flash=dynamic_cast<render_model_instance*>(q[0].get_unmanaged_resource().c_ptr());
                require(bool(range_flash),"Missing test range muzzle flash model");
            });

        constexpr pcstr terrain_root="resources/projects/bar_shooting_gallery";
        resources::user_data_variant data;
        data.set(terrain_cook_user_data{target,terrain_root,terrain_memory_profile::standard_256_mib});
        query(std::string(terrain_root)+"/terrain/0_0.terr",resources::terrain_model_class,[this](resources::queries_result& q) {
            terrain_model_ptr model=dynamic_cast<terrain_model_instance*>(q[0].get_unmanaged_resource().c_ptr());
            require(model && model->m_render_model,"Test range terrain missing");
            scene().terrain_add_cell(target,model->m_render_model);
            terrain.push_back(model);
        },&data);

        constexpr pcstr color="resources/models/props/shooting_gallery/target_color.model/render";
        constexpr pcstr paper="resources/models/props/shooting_gallery/target_bw.model/render";
        constexpr pcstr can="resources/models/props/shooting_gallery/canister_yellow.model/render";
        constexpr pcstr concrete="resources/models/generic/construction/concrete_block_simple.model/render";
        constexpr pcstr box="resources/models/props/box/woodbox_mil.model/render";
        constexpr pcstr barrel="resources/models/props/barrel/barrel_metal03_radiation.model/render";
        for (float z=-8; z>=-28; z-=2.05f) {
            place_range_model(concrete,{26.65f,.02f,z});
            place_range_model(concrete,{37.35f,.02f,z});
        }
        for (float x=27.8f;x<37.1f;x+=2.03f) {
            place_range_model(concrete,{x,.02f,-28.3f},math::pi_d2);
            place_range_model(concrete,{x,.74f,-28.3f},math::pi_d2);
        }
        place_range_model(barrel,{27.45f,.02f,-8.8f});
        place_range_model(barrel,{36.55f,.02f,-8.8f});

        place_range_model(box,{32,.02f,-11});
        place_range_model(color,{32,.49f,-11},0,.33f,.675f);
        place_range_model(can,{30.05f,.02f,-12},0,.09f,.465f);
        place_range_model(can,{33.95f,.02f,-12},0,.09f,.465f);
        for (float x:{29.8f,34.2f}) {
            place_range_model(box,{x,.02f,-17});
            place_range_model(box,{x,.49f,-17});
            place_range_model(paper,{x,.96f,-17},math::pi,.4f,.80f);
        }
        for (float x:{28.1f,32.f,35.9f}) {
            place_range_model(concrete,{x,.02f,-24},math::pi_d2);
            place_range_model(concrete,{x,.74f,-24},math::pi_d2);
            place_range_model(color,{x,1.47f,-24},0,.33f,.675f);
        }
    }
    void start_test_range() {
        require(terrain.size()==1 && range_targets.size()==8 && range_held_models.size()==2 && range_flash,"Incomplete test range");
        camera.c.xyz()={32,1.72f,-5.6f};
        range_yaw=math::pi;range_pitch=-.165f;range_shot_age=range_recoil_duration;
        if (xray::engine::capture_control_bus::has_flag(engine.get_render_window_handle(),xray::engine::m9_performance::hold_property)) {
            if (auto direction=std::getenv("XRAY_TEST_RANGE_BENCHMARK_VIEW");direction && !std::strcmp(direction,"sky"))
                range_pitch=math::pi_d4;
        }
        range_movement.fill(false);
        orient_range_camera();
        range_last_tick=std::chrono::steady_clock::now();
        fov=56;
        for (auto const& model:range_held_models) {
            auto transform=range_held_transform();
            scene().add_model(target,model,transform);
            models.push_back({model,transform,true});
        }
        lighting_environment environment{};
        environment.ambient={.58f,.61f,.66f};
        scene().set_lighting_environment(view,environment);
        light_props light={};
        light.type=light_type_parallel;
        light.transform=math::create_rotation_x(.8f)*math::create_rotation_y(math::pi-.42f);
        light.intensity=1.35f;
        light.range=90;
        light.color=0xffffe6cb;
        light.diffuse_influence_factor=light.specular_influence_factor=1;
        light.does_cast_shadows=true;
        light.sun_shadow_map_size=512;
        light.num_sun_cascades=3;
        scene().add_light(target,1,light);
        scene().set_postprocess_settings(view,base_settings);
        scene().set_view_mode(view,lit_view_mode);
        ready=true;
        std::cerr<<"Test range ready: targets="<<range_targets.size()<<" props="<<models.size()<<" held=2\n";
    }
    void orient_range_camera() {
        float horizontal=std::cos(range_pitch);
        camera.k.xyz()={std::sin(range_yaw)*horizontal,std::sin(range_pitch),std::cos(range_yaw)*horizontal};
        camera.i.xyz()={std::cos(range_yaw),0,-std::sin(range_yaw)};
        camera.j.xyz()=math::cross_product(camera.k.xyz(),camera.i.xyz());
    }
    math::float4x4 range_held_transform() const {
        constexpr float attack=.025f;
        float rise=std::min(range_shot_age/attack,1.f);
        float recovery=1.f-std::clamp((range_shot_age-attack)/(range_recoil_duration-attack),0.f,1.f);
        float kick=rise*recovery*recovery;
        return math::create_rotation_x(-.075f*kick)*
            math::create_translation(math::float3(.19f,-.22f,.47f-.058f*kick))*camera;
    }
    void update_range_viewmodel() {
        auto held=range_held_transform();
        for (auto const& model:range_held_models) scene().update_model(target,model,held);
        if (range_shot_age<range_flash_duration) {
            float scale=1.15f-.65f*(range_shot_age/range_flash_duration);
            // Local center of the muzzle on ak74_4tir1.
            auto flash=math::create_scale(math::float3(scale,scale,scale))*
                math::create_translation(math::float3(0,.042f,.605f))*held;
            if (range_flash_attached) scene().update_model(target,range_flash,flash);
            else scene().add_model(target,range_flash,flash);
            range_flash_attached=true;
        } else if (range_flash_attached) {
            scene().remove_model(target,range_flash);
            range_flash_attached=false;
        }
    }
    void move_range_player(float strafe,float forward,float seconds) {
        float length=std::hypot(strafe,forward);
        if (!length) return;
        auto direction=camera.i.xyz()*strafe+math::float3(std::sin(range_yaw),0,std::cos(range_yaw))*forward;
        auto destination=camera.c.xyz()+direction*(3.4f*seconds/length);
        camera.c.x=std::clamp(destination.x,27.6f,36.4f);
        camera.c.z=std::clamp(destination.z,-9.f,-3.4f);
    }
    bool fire_at_range() {
        range_shot_age=0;
        auto eye=camera.c.xyz();
        auto direction=camera.k.xyz();
        if (std::abs(direction.z)<.0001f) return false;
        range_target* selected=nullptr;
        float closest=far_plane;
        for (auto& candidate:range_targets) {
            auto center=candidate.upright.c.xyz();
            float distance=(center.z-eye.z)/direction.z;
            auto contact=eye+direction*distance;
            if (!candidate.hit && distance>near_plane && distance<closest &&
                std::abs(contact.x-center.x)<=candidate.half_width && contact.y>=center.y && contact.y<=center.y+candidate.height) {
                closest=distance;
                selected=&candidate;
            }
        }
        if (!selected) return false;
        selected->hit=true;
        scene().update_model(target,selected->model,math::create_rotation_x(-math::pi_d2)*selected->upright);
        return true;
    }
    void reset_range_targets() {
        for (auto& item:range_targets) if (item.hit) {
            item.hit=false;
            scene().update_model(target,item.model,item.upright);
        }
    }
    void update_test_range() {
        auto window=engine.get_render_window_handle();
        if (xray::engine::capture_control_bus::has_flag(window,"XRAY_VULKAN_TEST_CAPTURE")) {
            if (xray::engine::capture_control_bus::has_flag(window,xray::engine::m9_performance::hold_property)) return;
            if (ticks==15) {
                require(fire_at_range(),"Test range capture target did not register");
                range_shot_age=.025f;
                update_range_viewmodel();
            }
            if (ticks) return;
            auto start=camera;
            move_range_player(1,1,.25f);
            require(camera.c.x<start.c.x && camera.c.z<start.c.z,"Test range movement did not move the player");
            float pitch=range_pitch;range_pitch=.55f;orient_range_camera();
            require(!fire_at_range(),"Test range miss registered as a hit");
            range_pitch=pitch;camera=start;
            require(fire_at_range(),"Test range center target did not register");
            require(std::count_if(range_targets.begin(),range_targets.end(),[](auto const& item){return item.hit;})==1,"Test range hit count differs");
            reset_range_targets();
            require(std::none_of(range_targets.begin(),range_targets.end(),[](auto const& item){return item.hit;}),"Test range reset did not restore targets");
            update_range_viewmodel();
            require(range_flash_attached,"Test range shot did not attach the flash");
            auto neutral=range_held_transform().c.xyz();
            range_shot_age=.025f;
            update_range_viewmodel();
            require(range_flash_attached && (range_held_transform().c.xyz()-neutral).length()>.04f,"Test range recoil did not move the weapon");
            range_shot_age=range_recoil_duration;
            update_range_viewmodel();
            require(!range_flash_attached && (range_held_transform().c.xyz()-neutral).length()<.001f,"Test range shot effect did not clear");
            std::cerr<<"Test range smoke: movement, aim, miss, hit and reset passed\n";
            std::cerr<<"Test range smoke: flash and recoil appeared and cleared\n";
            return;
        }
        auto now=std::chrono::steady_clock::now();
        float elapsed=std::chrono::duration<float>(now-range_last_tick).count();
        float seconds=std::clamp(elapsed,0.f,.05f);
        range_last_tick=now;
        range_shot_age=std::min(range_recoil_duration,range_shot_age+elapsed);
        input::normalized_input_event event;
        constexpr std::array movement_keys={SDL_SCANCODE_W,SDL_SCANCODE_S,SDL_SCANCODE_A,SDL_SCANCODE_D};
        while (input::pop_normalized_input_event(event)) {
            if (event.kind==input::normalized_event_kind::focus_lost) range_movement.fill(false);
            if (event.kind==input::normalized_event_kind::key_down || event.kind==input::normalized_event_kind::key_up) {
                for (size_t i=0;i<movement_keys.size();++i)
                    if (event.code==movement_keys[i]) range_movement[i]=event.kind==input::normalized_event_kind::key_down;
            }
            if (event.kind==input::normalized_event_kind::key_down) {
                if (event.code==SDL_SCANCODE_ESCAPE) {
                    std::cerr<<"Test range exit: Escape\n";
                    engine.exit(0);
                    return;
                }
                if (event.code==SDL_SCANCODE_R) reset_range_targets();
            }
            if (event.kind==input::normalized_event_kind::mouse_motion) {
                range_yaw=std::remainder(range_yaw+event.x*.0022f,math::pi_x2);
                range_pitch=std::clamp(range_pitch-event.y*.0022f,-1.47f,1.47f);
                orient_range_camera();
            }
            if (event.kind==input::normalized_event_kind::mouse_button_down && event.code==input::mouse_button_left)
                fire_at_range();
        }
        move_range_player(float(range_movement[3])-float(range_movement[2]),
                          float(range_movement[0])-float(range_movement[1]),seconds);
        update_range_viewmodel();
    }
    void draw_range_score() {
        float x=18;
        for (auto const& item:range_targets) {
            u32 color=item.hit ? 0xff5dd6a7 : 0xffeab35e;
            ui::vertex marker[]={{x,28,0,color,0,1},{x,18,0,color,0,0},
                {x+18,28,0,color,1,1},{x+18,18,0,color,1,0}};
            api.game_renderer().ui().draw_vertices(view,marker,0,2);
            x+=24;
        }
        auto size=main_projection_size;
        auto rectangle=[this](float left,float top,float right,float bottom,u32 color) {
            ui::vertex vertices[]={{left,bottom,0,color,0,1},{left,top,0,color,0,0},
                {right,bottom,0,color,1,1},{right,top,0,color,1,0}};
            api.game_renderer().ui().draw_vertices(view,vertices,0,2);
        };
        float cx=std::floor(size.x*.5f),cy=std::floor(size.y*.5f);
        auto stroke=[&](float l,float t,float r,float b) {
            rectangle(cx+l-1,cy+t-1,cx+r+1,cy+b+1,0xff181b1e);
            rectangle(cx+l,cy+t,cx+r,cy+b,0xfff5f8fc);
        };
        stroke(-13,-1,-4,1);stroke(4,-1,13,1);stroke(-1,-13,1,-4);stroke(-1,4,1,13);
    }
    void read_project(resources::queries_result& result) {
        resources::pinned_ptr_const<u8> bytes(result[0].get_managed_resource());
        project=std::make_unique<checked::checked_config>(const_buffer(bytes.c_ptr(),bytes.size()),*allocator);
        auto const& p=checked::field(project->root(),"vulkan_m8_scene",configs::t_table_named);
        require(u32(checked::field(p,"version",configs::t_integer))==1 && text(p,"profile")=="vulkan_m8_control_v1","Unknown render level profile");
        require(u32(checked::field(p,"terrain_memory_mib",configs::t_integer))==384,"Wrong terrain memory profile");
        require(checked::field(p,"terrain",configs::t_table_indexed).size()==2 && checked::field(p,"objects",configs::t_table_indexed).size()==8,"Content selection differs");
        std::set<std::string> ids;
        for (auto const& object:p["objects"]) {require(ids.insert(text(object,"id")).second,"Duplicate object");checked::validate_path(text(object,"asset"));position(object,"translation");}
        auto const& c=checked::field(p,"camera",configs::t_table_named);
        camera.i.xyz()=position(c,"right");camera.j.xyz()=position(c,"up");camera.k.xyz()=position(c,"forward");camera.c.xyz()=position(c,"eye");
        fov=float(checked::field(c,"vertical_fov_degrees",configs::t_float));near_plane=float(checked::field(c,"near",configs::t_float));far_plane=float(checked::field(c,"far",configs::t_float));
        require(near_plane>0 && far_plane>near_plane && fov>1 && fov<179,"Invalid stored projection");
        vulkan::scene_configuration config;config.features.m_create_terrain=true;config.features.m_create_particle_world=true;
        config.terrain_memory=terrain_memory_profile::extended_384_mib;config.particle_fixed_timestep=1.f/60;
        resources::user_data_variant data,window;data.set(config);window.set(engine.get_render_window_handle());
        resources::user_data_variant const* users[]={&data,nullptr,&window};
        resources::creation_request requests[]={{"m8_game_scene",const_buffer("",1),resources::scene_class},
            {"m8_game_view",const_buffer("",1),resources::scene_view_class},{"m8_game_output",const_buffer("",1),resources::render_output_window_class}};
        resources::query_create_resources(requests,3,callback([this](resources::queries_result& q) {
            target=dynamic_cast<base_scene*>(q[0].get_unmanaged_resource().c_ptr());view=dynamic_cast<base_scene_view*>(q[1].get_unmanaged_resource().c_ptr());
            output=dynamic_cast<base_output_window*>(q[2].get_unmanaged_resource().c_ptr());require(target && view && output,"Scene resources missing");load_content();
        }),allocator,users,nullptr,assert_on_fail_false);
    }
    void load_emitter(value const& object) {
        auto id=text(object,"id"),asset=text(object,"asset");auto transform=math::create_translation(position(object,"translation"));
        resources::user_data_variant data;data.set(particles);query(asset,resources::particle_system_instance_class,[this,transform,id](resources::queries_result& q) {
            particle::particle_system_instance_ptr emitter=q[0].get_unmanaged_resource();scene().play_particle_system(target,emitter,transform);
            emitters.push_back(emitter);emitter_placements.push_back(transform);emitter_ids.push_back(id);
        },&data);
    }
    void load_content() {
        auto const& p=project->root()["vulkan_m8_scene"];
        for (auto const& entry:p["terrain"]) {
            auto root=std::make_shared<std::string>(text(entry,"project"));auto memory=u32(checked::field(entry,"memory_mib",configs::t_integer));
            require(memory==256 || memory==384,"Unknown terrain profile");
            // The legacy typed payload holds a pcstr. Retain its owner from
            // submission through callback, including shutdown before translation.
            resources::user_data_variant data;data.set(terrain_cook_user_data{target,root->c_str(),memory==384 ? terrain_memory_profile::extended_384_mib : terrain_memory_profile::standard_256_mib});
            query(text(entry,"path"),resources::terrain_model_class,[this,root](resources::queries_result& q) {
                terrain_model_ptr model=dynamic_cast<terrain_model_instance*>(q[0].get_unmanaged_resource().c_ptr());
                require(model && model->m_render_model && model->m_collision_geom,"Terrain collision wrapper missing");
                if(editor) {
                    auto* instance=dynamic_cast<vulkan::terrain_instance_resource*>(model->m_render_model.c_ptr());require(instance,"Terrain instance missing");
                    auto cell=static_cast<vulkan::terrain_mesh_resource*>(instance->asset.c_ptr())->data->cell;
                    terrain_origins.push_back({model,cell});
                    if(root->find("bar_shooting_gallery")!=std::string::npos) {
                        editor_cell=model;editor_layers=cell->textures;std::vector<u64> weights(editor_layers.size());
                        for(auto const& sample:cell->samples)for(unsigned i=0;i<3;++i)weights[sample.texture_ids[i]]+=sample.weights[i];
                        editor_dominant=unsigned(std::max_element(weights.begin(),weights.end())-weights.begin());
                    }
                }
                scene().terrain_add_cell(target,model->m_render_model);terrain.push_back(model);
            },&data);
        }
        particles=scene().particle_world_handle(target);
        for (auto const& object:p["objects"]) {
            auto role=text(object,"role"),asset=text(object,"asset");auto transform=math::create_translation(position(object,"translation"));
            if (role=="static") query(asset,resources::static_render_model_instance_class,[this,transform,asset](resources::queries_result& q) {
                render_model_instance_ptr model=dynamic_cast<render_model_instance*>(q[0].get_unmanaged_resource().c_ptr());require(bool(model),"Static missing");scene().add_model(target,model,transform);models.push_back({model,transform,true});
                if(asset=="resources/models/props/box/woodbox_mil.model/render")woodbox_model=model;
            });
            else if (role=="actor") {
                actor_transform=transform;query(asset,resources::skeleton_model_instance_class,[this](resources::queries_result& q) {
                    actor=dynamic_cast<skeleton_model_instance*>(q[0].get_unmanaged_resource().c_ptr());require(actor && actor->m_skeleton && actor->m_render_model,"Actor wrapper missing");scene().add_model(target,actor->m_render_model,actor_transform);actor_attached=true;
                });
                auto const& list=checked::field(object,"clips",configs::t_table_indexed);require(list.size()==2,"Two clips required");unsigned index=0;
                for (auto const& clip:list) {auto slot=index++;query(std::string("resources/animations/")+pcstr(clip),resources::animation_class,[this,slot](resources::queries_result& q) {clips[slot]=static_cast_resource_ptr<animation::skeleton_animation_ptr>(q[0].get_managed_resource());});}
            }
            else if (role=="vegetation") query(asset,resources::speedtree_instance_class,[this,transform](resources::queries_result& q) {
                speedtree_instance_ptr tree=dynamic_cast<speedtree_instance*>(q[0].get_unmanaged_resource().c_ptr());require(bool(tree),"Tree missing");scene().add_speedtree_instance(target,tree,transform,false);trees.push_back(tree);tree_placements.push_back(transform);
            });
            else if (role=="particles") load_emitter(object);
            else if (role=="decals") query(asset,resources::material_class,[this,transform](resources::queries_result& q) {
                auto material=q[0].get_unmanaged_resource();materials.push_back(material);decal_state=std::make_unique<decal_properties>();auto& decal=*decal_state;decal.material=material;
                decal.transform=math::create_rotation_x(math::pi_d2)*transform;decal.width_height_far_distance={.4f,.4f,.1f};
                decal.projection_on_speedtree_geometry=decal.projection_on_skeleton_geometry=decal.projection_on_particle_geometry=false;scene().add_decal(target,1,decal);decal_added=true;
            });
            else if (role=="distortion") {
                auto path=text(object,"model"),surface=text(object,"surface");
                query(asset,resources::material_class,[this,transform,path,surface](resources::queries_result& q) {
                    auto material=q[0].get_unmanaged_resource();materials.push_back(material);
                    query(path,resources::static_render_model_instance_class,[this,transform,surface,material](resources::queries_result& q) {
                        render_model_instance_ptr model=dynamic_cast<render_model_instance*>(q[0].get_unmanaged_resource().c_ptr());require(bool(model),"Field model missing");
                        scene().set_model_material(model,fs_new::virtual_path_string(surface.c_str()),material);scene().add_model(target,model,transform);models.push_back({model,transform,false});
                    });
                });
            } else throw std::runtime_error("Unsupported control object role");
        }
    }
    void reset_player(bool transition) {
        using namespace animation;using namespace animation::mixing;player=std::make_unique<animation_player>();
        if(transition) {
            alignas(16) char storage[animation_player::stack_buffer_size];mutable_buffer buffer(storage,sizeof(storage));linear_interpolator fade(.25f);
            animation_lexeme first(animation_lexeme_parameters(buffer,"m8-idle",clips[0]).weight_interpolator(fade));
            player->set_target_and_tick(first,0);player->tick(500);
            animation_lexeme second(animation_lexeme_parameters(buffer,"m8-play",clips[1]).weight_interpolator(fade));
            player->set_target_and_tick(second,500);return;
        }
        alignas(16) char storage[animation_player::stack_buffer_size];mutable_buffer buffer(storage,sizeof(storage));instant_interpolator instant;
        animation_lexeme first(animation_lexeme_parameters(buffer,"m8-idle",clips[0]).weight_interpolator(instant));
        animation_lexeme second(animation_lexeme_parameters(buffer,"m8-play",clips[1]).weight_interpolator(instant));
        weight_lexeme a(buffer,.35f,instant),b(buffer,.65f,instant);player->set_target_and_tick(first*a+second*b,0);
    }
    void start() {
        if (test_range) {start_test_range();return;}
        if (sdk_blank_scene) {
            lighting_environment environment{};
            environment.ambient = { 0.35f, 0.35f, 0.38f };
            scene().set_lighting_environment(view, environment);
            base_settings = {};
            base_settings.ssao_enabled = false;
            base_settings.distortion_enabled = false;
            scene().set_postprocess_settings(view, base_settings);
            scene().set_view_mode(view, lit_view_mode);
            ready = true;
            std::cerr << "SDK blank host scene ready: " << owner_label() << owner << " generation=" << generation << '\n';
            return;
        }
        require(terrain.size()==2 && models.size()==3 && trees.size()==1 && emitters.size()==2 && actor && clips[0] && clips[1] && materials.size()==2,"Incomplete content");
        reset_player(false);
        scene().populate_speedtree_forest(target);
        auto const& lighting=checked::field(project->root()["vulkan_m8_scene"],"lighting",configs::t_table_named);
        auto ambient=position(lighting,"ambient");lighting_environment environment;environment.ambient={ambient.x,ambient.y,ambient.z};scene().set_lighting_environment(view,environment);
        unsigned light_id=1;
        for (auto const& entry:checked::field(lighting,"lights",configs::t_table_indexed)) {
            light_props light={};auto kind=text(entry,"type");
            require(kind=="parallel" || kind=="point" || kind=="spot","Unsupported control light");
            light.type=kind=="parallel" ? light_type_parallel : kind=="point" ? light_type_point : light_type_spot;
            light.transform=math::create_rotation_x(float(checked::field(entry,"pitch",configs::t_float)))
                *math::create_rotation_y(float(checked::field(entry,"yaw",configs::t_float)))*math::create_translation(position(entry,"translation"));
            light.intensity=float(checked::field(entry,"intensity",configs::t_float));light.range=float(checked::field(entry,"range",configs::t_float));
            light.color=0xffffffff;light.diffuse_influence_factor=light.specular_influence_factor=1;light.attenuation_power=2;
            light.spot_umbra_angle=.5f;light.spot_penumbra_angle=1;light.spot_falloff=1;
            light.does_cast_shadows=true;light.sun_shadow_map_size=512;light.num_sun_cascades=4;light.shadow_map_size_index=1;
            for(auto& side:light.shadow_distribution_sides)side=true;
            scene().add_light(target,light_id++,light);
        }
        base_settings={};
        base_settings.ssao_enabled=bool(checked::field(project->root()["vulkan_m8_scene"],"ssao",configs::t_boolean));
        base_settings.distortion_enabled=bool(checked::field(project->root()["vulkan_m8_scene"],"distortion",configs::t_boolean));
        scene().set_postprocess_settings(view,base_settings);scene().set_view_mode(view,lit_view_mode);
        ready=true;std::cerr<<"M8 content ready: terrain=2 static/field=3 actor=1 clips=2 tree=1 particles=2 decals=1 "<<owner_label()<<owner<<" generation="<<generation<<'\n';
    }
    math::float4x4 inspection_camera(bool moved=false) const {
        // Explicit inspection path, not a runtime bounds fit. Both cameras use
        // the same fixed authored object placements and a forward-Z basis.
        math::float3 eye(24+(moved?3.f:0.f),9,-13),at(23,1,-33);
        auto forward=math::normalize(at-eye),right=math::normalize(math::cross_product(math::float3(0,1,0),forward));
        auto result=math::float4x4().identity();result.i.xyz()=right;result.j.xyz()=math::cross_product(forward,right);
        result.k.xyz()=forward;result.c.xyz()=eye;return result;
    }
    void render_frame(unsigned actor_time,math::float4x4 const& active_camera) {
        if (player && actor) {
            player->tick(actor_time);std::vector<math::float4x4> pose(actor->m_skeleton->get_non_root_bones_count());
            player->compute_bones_matrices(*actor->m_skeleton,pose.data(),pose.data()+pose.size());scene().update_skeleton(actor->m_render_model,pose.data(),u32(pose.size()));
            if (actor_attached) scene().update_model(target,actor->m_render_model,player->get_object_transform()*actor_transform);
        }
        auto dimensions=engine.get_render_window_size();
        if(dimensions.x>0 && dimensions.y>0)main_projection_size=dimensions;
        else {require(editor,"Suspended control output");dimensions=main_projection_size;}
        auto p=math::float4x4().identity();
        p.e00=1/std::tan(fov*math::pi/360)/(dimensions.x/dimensions.y);p.e11=1/std::tan(fov*math::pi/360);
        p.e22=far_plane/(far_plane-near_plane);p.e23=1;p.e32=-near_plane*far_plane/(far_plane-near_plane);p.e33=0;
        if(sdk_project_scene)p=sdk_project_projection;
        auto actual_view=math::invert4x3(sdk_project_scene?camera:active_camera);
        if(sdk_project_scene && sdk_view_configured && sdk_freeze && sdk_frozen_camera)scene().set_view_matrix_only(view,actual_view);
        else scene().set_view_matrix(view,actual_view);
        if(sdk_project_scene && sdk_view_configured) {
            sdk_frozen_camera=sdk_freeze;
            if(sdk_view_dirty) {
                auto settings=base_settings;settings.enabled=sdk_postprocess;
                // Lit-only effects must not leak into diagnostic views or a
                // disabled postprocess profile (the Vulkan queue rejects it).
                if(!sdk_postprocess || sdk_view_mode!=lit_view_mode) {
                    settings.ssao_enabled=settings.distortion_enabled=false;
                    settings.taa_enabled=settings.fxaa_enabled=settings.bloom_enabled=false;settings.msaa_samples=1;
                } else {
                    settings.ssao_enabled=sdk_ssao && !sdk_fxaa;
                    settings.distortion_enabled=sdk_distortion && !sdk_fxaa;
                    settings.fxaa_enabled=sdk_fxaa && !settings.ssao_enabled && !settings.distortion_enabled;
                }
                scene().set_postprocess_settings(view,settings);scene().set_view_mode(view,sdk_view_mode);sdk_view_dirty=false;
            }
        }
        scene().set_projection_matrix(view,p);
        if (test_range) draw_range_score();
        if(editor_overlay) {
            ui::vertex vertices[]={ui::vertex(16,52,0,0xff4080c0,0,1),ui::vertex(16,16,0,0xff4080c0,0,0),
                ui::vertex(160,52,0,0xff4080c0,1,1),ui::vertex(160,16,0,0xff4080c0,1,0)};
            api.editor_renderer().ui().draw_vertices(view,vertices,vertices+4,0,2);
            for(auto& vertex:vertices)vertex.m_color=0xffff0000;
            // Linear 85/255 = 1/3 maps through Reinhard to 1/4; its sRGB
            // encoding rounds unambiguously to 137, away from byte boundaries.
            api.editor_renderer().debug().draw_triangle(target,{24,3,-23},{26,3,-23},{25,5,-23},math::color(0u,85u,0u,255u),false);
        }
        if(editor)api.editor_renderer().draw_scene(sdk_project_scene?sdk_project_scene:target,view,output,{{0,0},{1,1}});
        else api.game_renderer().draw_scene(target,view,output,{{0,0},{1,1}});
    }
    void render_preview() {
        if(!preview_model || preview_closing || preview_loading)return;
        u32 bounds_w=0, bounds_h=0;
        require(vulkan::surface_adapter::query_surface_extent(preview_window,bounds_w,bounds_h),"Preview surface disappeared before GPU drain");
        if(!bounds_w || !bounds_h)return;
        math::float3 eye(1.8f,1.6f,-2.5f),at(0,.5f,0);
        auto forward=math::normalize(at-eye),right=math::normalize(math::cross_product(math::float3(0,1,0),forward));
        auto camera=math::float4x4().identity();camera.i.xyz()=right;camera.j.xyz()=math::cross_product(forward,right);camera.k.xyz()=forward;camera.c.xyz()=eye;
        auto projection=math::float4x4().identity();projection.e00=1/std::tan(math::pi/6)/(float(bounds_w)/bounds_h);projection.e11=1/std::tan(math::pi/6);
        projection.e22=64.f/(64.f-.1f);projection.e23=1;projection.e32=-.1f*64.f/(64.f-.1f);projection.e33=0;
        scene().set_view_matrix(preview_view,math::invert4x3(camera));scene().set_projection_matrix(preview_view,projection);
        api.editor_renderer().draw_scene(preview_scene,preview_view,preview_output,{{0,0},{1,1}});
    }
    void queue_preview_close() {
        if(preview_output && !preview_close_queued) {scene().close_output(preview_output);preview_close_queued=true;}
    }
    void publish(unsigned frame,protocol::shot shot) {
        require(!awaiting,"Overlapping capture requests");awaiting=protocol::token(frame,shot);
        require(xray::engine::capture_control_bus::set_token(engine.get_render_window_handle(),protocol::request_property,awaiting),"Cannot publish workflow frame");
        std::cerr<<"M8 workflow submit: shot="<<protocol::name(shot)<<" generation="<<generation<<" actor_time_ms="<<(shot==protocol::shot::actor_transition?625:240)<<" "<<owner_label()<<owner<<'\n';
    }
    void controls(protocol::shot shot,bool pause_particles=true) {
        using s=protocol::shot;
        auto settings=base_settings;settings.ssao_enabled &= shot!=s::no_ssao;settings.distortion_enabled &= shot!=s::no_distortion;
        scene().set_postprocess_settings(view,settings);
        bool decal_visible=shot!=s::no_decals;
        if (decal_visible!=decal_added) {if (decal_visible)scene().add_decal(target,1,*decal_state);else scene().remove_decal(target,1);decal_added=decal_visible;}
        for(size_t i=0;i<emitters.size();++i) {
            auto transform=emitter_placements[i];if(shot==s::particle_moved)transform.c.xyz()+=math::float3(1,.5f,0);
            bool visible=shot!=s::no_particles && !(shot==s::no_alpha && emitter_ids[i]=="candle_alpha") && !(shot==s::no_additive && emitter_ids[i]=="candle_add");
            scene().update_particle_system_instance(target,emitters[i],transform,visible,pause_particles);
        }
        for(auto const& tree:trees)scene().set_speedtree_instance_visible(tree,shot!=s::no_tree);
        bool move_tree=shot==s::tree_moved;
        if(move_tree!=tree_translated) {
            for(size_t i=0;i<trees.size();++i) {auto transform=tree_placements[i];if(move_tree)transform.c.x+=2;scene().update_speedtree_instance(target,trees[i],transform,true);}
            tree_translated=move_tree;
        }
        bool move_decal=shot==s::decal_moved;
        if(move_decal!=decal_translated) {auto properties=*decal_state;if(move_decal)properties.transform.c.x+=1;scene().update_decal(target,1,properties);decal_translated=move_decal;}
        bool transition=shot==s::actor_transition;
        if(transition!=actor_transitioning){reset_player(transition);actor_transitioning=transition;}
        bool actor_visible=shot!=s::no_actor;
        if (actor_visible!=actor_attached) {if(actor_visible)scene().add_model(target,actor->m_render_model,player->get_object_transform()*actor_transform);else scene().remove_model(target,actor->m_render_model);actor_attached=actor_visible;}
        for(auto& model:models) if(model.opaque && model.attached!=((shot!=s::no_static) && !editor_props_hidden)) {
            if(model.attached)scene().remove_model(target,model.value);else scene().add_model(target,model.value,model.transform);model.attached=!model.attached;
        }
        bool terrain_visible=shot!=s::no_terrain;
        if (terrain_visible!=terrain_attached) {for(auto const& cell:terrain) {if(terrain_visible)scene().terrain_add_cell(target,cell->m_render_model);else scene().terrain_remove_cell(target,cell->m_render_model);}terrain_attached=terrain_visible;}
    }
    void begin_pending_shutdown() {
        require(!pending && !held_transaction,"Pending shutdown began with unfinished load");
        pending_scene=target;pending_world=particles;resources::start_query_transaction();held_transaction=true;++pending;
        resources::user_data_variant data;data.set(pending_world);
        resources::query_resource("m8/candlelight_alpha",resources::particle_system_instance_class,[this](resources::queries_result& result) {
            bool valid=threading::current_thread_id()==owner && !result.is_successful() && pending_world && !pending_world->accepting();
            late_returned=valid;--pending;pending_scene=nullptr;pending_world=nullptr;
            std::cerr<<"M8 pending shutdown callback: rejected="<<valid<<" "<<owner_label()<<threading::current_thread_id()<<'\n';
            CHECK_OR_EXIT(valid,"Pending query published after session close or changed owner");
        },allocator,&data,nullptr,assert_on_fail_false);
        require(pending==1 && !late_returned,"Shutdown request was not held by transaction");
        std::cerr<<"M8 pending shutdown: held=1 scene_retained=1 "<<owner_label()<<owner<<'\n';engine.exit(0);
    }
    void workflow_tick(unsigned frame) {
        using s=protocol::shot;
        if (awaiting) {
            if (xray::engine::capture_control_bus::get_token(engine.get_render_window_handle(),protocol::ack_property)!=awaiting)return;
            awaiting=0;
            if(current_shot==s::camera_restored) {
                unload_registry=static_cast<vulkan::scene_resource*>(target.c_ptr())->owner;
                unload(nullptr,false);unloading=true;unload_deadline=std::chrono::steady_clock::now()+std::chrono::seconds(10);return;
            }
            if(current_shot==s::unloaded) {load("vulkan_m8_control",nullptr);current_shot=s::reloaded_overview;return;}
            if(current_shot==s::reloaded_inspection) {begin_pending_shutdown();return;}
            if(editor && current_shot==s::particles_replayed)current_shot=s::editor_props_hidden;
            else if(current_shot==s::editor_overlay_restored)current_shot=s::camera_moved;
            else current_shot=s(unsigned(current_shot)+1);
            editor_action_applied=false;
        }
        if (unloading) {
            require(std::chrono::steady_clock::now()<unload_deadline,"Unload resource handles did not drain");
            if (pending || unload_registry->counts()!=std::array<size_t,3>{0,0,0})return;
            unloading=false;std::cerr<<"M8 unload drained: scenes=0 views=0 outputs=0 "<<owner_label()<<owner<<'\n';
            current_shot=s::unloaded;publish(frame+1,current_shot);return;
        }
        if (!ready || stopping || !enabled)return;
        if(editor && current_shot>=s::editor_props_hidden && current_shot<=s::editor_overlay_restored) {
            if(!editor_action_applied)return;
            controls(current_shot);render_frame(240,inspection_camera());
            if(!editor_binding_pending)publish(frame+1,current_shot);
            return;
        }
        if (current_shot==s::overview || current_shot==s::reloaded_overview) {
            render_frame(ticks++*16,camera);
            if(ticks==16)publish(frame+1,current_shot);
            return;
        }
        if(current_shot==s::particles_stopped_removed && !emitters.empty()) {
            require(emitters.size()==2 && !pending,"Particle stop/remove began before readiness");
            for(auto const& emitter:emitters){scene().stop_particle_system(target,emitter);scene().remove_particle_system_instance(target,emitter);}
            emitters.clear();emitter_placements.clear();emitter_ids.clear();
            std::cerr<<"M8 particles stopped/removed: instances=2 "<<owner_label()<<owner<<'\n';
        }
        if(current_shot==s::particles_replayed) {
            if(!replay_loading){replay_loading=true;for(auto const& object:project->root()["vulkan_m8_scene"]["objects"])if(text(object,"role")=="particles")load_emitter(object);}
            if(pending)return;
            require(emitters.size()==2,"Particle replay did not create both profiles");
            controls(current_shot,false);render_frame(240,inspection_camera());
            if(++replay_ticks==16){std::cerr<<"M8 particles replayed: instances=2 warmup_frames=16 "<<owner_label()<<owner<<'\n';publish(frame+1,current_shot);}
            return;
        }
        controls(current_shot);render_frame(current_shot==s::actor_transition?625:240,inspection_camera(current_shot==s::camera_moved));publish(frame+1,current_shot);
    }
    void audit_legacy_config_apis() {
        auto const flag = std::getenv("XRAY_M8_CAPABILITY_AUDIT");
        if (!flag || std::strcmp(flag, "1") != 0) return;
        // Lua values require the real core VM/mutex. Keep these controls in the
        // native host instead of constructing a partially initialized CPU VM.
        auto config=configs::create_lua_config();
        auto reject=[&](pcstr entry,pcstr operation,auto call) {
            bool rejected=false;
            try {call();}catch(std::runtime_error const& error) {
                require(error.what()==std::string("Vulkan unsupported operation: ")+operation+" (outside the M8 capability profile)","Legacy config API returned an unrelated error");
                rejected=true;
            }
            require(rejected,"Legacy config API silently succeeded or queued work");
            std::cerr<<"M8 capability native rejected: entry="<<entry<<' '<<owner_label()<<owner<<'\n';
        };
        auto& world=api.engine_world();
        reject("world.update_preview_particle_system","update_preview_particle_system",[&]{world.update_preview_particle_system(nullptr,{},config->get_root());});
        reject("world.add_preview_particle_system","add_preview_particle_system",[&]{world.add_preview_particle_system(nullptr,{},config->get_root(),math::float4x4().identity());});
        reject("world.apply_material_changes","apply_material_changes",[&]{world.apply_material_changes(fs_new::virtual_path_string("legacy"),config->get_root());});
        if(editor) {
            auto& renderer=api.editor_renderer();
            reject("editor.update_preview_particle_system","update_preview_particle_system",[&]{renderer.update_preview_particle_system({},nullptr,config->get_root());});
            reject("editor.add_preview_particle_system","add_preview_particle_system",[&]{renderer.add_preview_particle_system({},nullptr,config->get_root(),math::float4x4().identity());});
            reject("editor.apply_material_changes","apply_material_changes",[&]{renderer.apply_material_changes(fs_new::virtual_path_string("legacy"),config->get_root());});
        }
    }
public:
    control_world(engine_user::engine& e,render::world& r,memory::base_allocator& a,bool editor_channel=false,bool complete_workflow=false,bool outputs=false,bool sdk_diagnostic=false,bool blank_sdk_host=false,level_profile profile=level_profile::control):
        engine(e),api(r),allocator(&a),workflow(complete_workflow),editor(editor_channel),output_workflow(outputs),sdk_capture(sdk_diagnostic),sdk_blank_scene(blank_sdk_host),test_range(profile==level_profile::test_range) {
        if(auto mode=std::getenv("XRAY_VULKAN_PERFORMANCE");mode && !std::strcmp(mode,"1"))
            require(xray::engine::capture_control_bus::set_flag(engine.get_render_window_handle(),xray::engine::m9_performance::hold_property),"Cannot prepare performance content hold");
        audit_legacy_config_apis();load(test_range ? "test_range" : "vulkan_m8_control",nullptr);
    }
    void prepare_ready() {
        try { if (target && !ready && !pending && !stopping && enabled) start(); }
        catch (std::exception const& error) { fail(error); }
    }
    math::float4x4 camera_view() const {return math::invert4x3(camera);}
    void camera_view(math::float4x4 const& value) {
        // Reapplying the current view must not repeatedly invert the rounded
        // camera basis and accumulate translation error while assets load.
        auto current=camera_view();if(std::memcmp(&current,&value,sizeof(value))==0)return;
        camera=math::invert4x3(value);
    }
    bool editor_ready() const {return ready && !pending && !stopping && !editor_binding_pending;}
    scene_ptr editor_scene() const {return sdk_project_scene?sdk_project_scene:target;}
    scene_view_ptr editor_view() const {return view;}
    render_output_window_ptr editor_output() const {return output;}
    scene_renderer& editor_scene_renderer() {return scene();}
    render::debug::renderer& editor_debug_renderer() {return api.editor_renderer().debug();}
    render::ui::renderer& editor_ui_renderer() {return api.editor_renderer().ui();}
    void draw_editor_output(scene_ptr const& scene,scene_view_ptr const& view,render_output_window_ptr const& output) {api.editor_renderer().draw_scene(scene,view,output,{{0,0},{1,1}});}
    void project_selection(render_model_instance_ptr const& model,bool selected){api.editor_renderer().select_model(editor_scene(),model,selected);}
    void project_scene(scene_ptr const& value) {
        if(!sdk_project_scene && value)sdk_frozen_camera=false;
        if(sdk_project_scene!=value)sdk_view_dirty=true;
        if(sdk_project_scene && !value && sdk_view_configured){scene().set_view_mode(view,lit_view_mode);scene().set_postprocess_settings(view,base_settings);}
        sdk_project_scene=value;
    }
    void project_projection(math::float4x4 const& value) {sdk_project_projection=value;}
    void project_view_options(scene_view_mode mode,bool freeze,bool postprocess,bool ssao=false,bool distortion=false,bool fxaa=false) {
        if(!sdk_view_configured || mode!=sdk_view_mode || postprocess!=sdk_postprocess || ssao!=sdk_ssao || distortion!=sdk_distortion || fxaa!=sdk_fxaa)sdk_view_dirty=true;
        if(freeze!=sdk_freeze)sdk_frozen_camera=false;
        sdk_view_configured=true;sdk_view_mode=mode;sdk_freeze=freeze;sdk_postprocess=postprocess;sdk_ssao=ssao;sdk_distortion=distortion;sdk_fxaa=fxaa;
    }
    void read_editor_position(u64& id,u64& revision,math::float3& position) const {
        require(editor && threading::current_thread_id()==owner && editor_ready() && !workflow && !editor_props_moved && !editor_props_hidden,"Unavailable SDK object");
        auto found=std::find_if(models.begin(),models.end(),[this](auto const& model){return model.value==woodbox_model;});
        require(found!=models.end() && found->attached,"SDK selection is missing");
        id=(u64(generation)<<32)|1;revision=sdk_revision;position=found->transform.c.xyz();
    }
    u64 write_editor_position(u64 id,u64 revision,math::float3 const& position) {
        u64 actual=0,current=0;math::float3 previous;read_editor_position(actual,current,previous);
        require(id==actual && revision==current && current!=~u64(0),"Stale SDK object/revision");
        require(std::isfinite(position.x) && std::isfinite(position.y) && std::isfinite(position.z),"Nonfinite SDK position");
        auto found=std::find_if(models.begin(),models.end(),[this](auto const& model){return model.value==woodbox_model;});
        auto transform=found->transform;transform.c.xyz()=position;
        scene().update_model(target,found->value,transform);
        found->transform=transform;
        return ++sdk_revision;
    }
    bool outputs_ready() const {return editor_ready() && ticks>=16 && preview_model && !preview_closing;}
    bool outputs_warm() const {return editor_ready() && ticks>=16;}
    unsigned pending_queries() const {return pending;}
    unsigned rejected_previews() const {return cancelled_preview_queries;}
    std::shared_ptr<vulkan::scene_registry> registry() const {return static_cast<vulkan::scene_resource*>(target.c_ptr())->owner;}
    void preview_visible(bool visible) {
        require(editor_ready() && preview_model && !preview_closing,"Preview is not ready for visibility");
        auto* model=static_cast<vulkan::static_model_resource*>(preview_model.c_ptr());
        for(auto const& surface:model->data->surfaces)scene().set_model_visible(preview_model,fs_new::virtual_path_string(surface.name.c_str()),visible);
    }
    void open_preview(platform::native_surface_handle window) {
        require(editor && editor_ready() && !workflow && !preview_window.valid() && vulkan::surface_adapter::is_surface_alive(window) && window!=engine.get_render_window_handle(),"Unavailable or duplicate model preview");
        preview_window=window;preview_loading=true;preview_closing=preview_close_queued=preview_releasing=false;
        resources::user_data_variant data,handle;data.set(vulkan::scene_configuration{});handle.set(window);
        resources::user_data_variant const* users[]={&data,nullptr,&handle};
        resources::creation_request requests[]={{"m8_model_preview_scene",const_buffer("",1),resources::scene_class},
            {"m8_model_preview_view",const_buffer("",1),resources::scene_view_class},{"m8_model_preview_output",const_buffer("",1),resources::render_output_window_class}};
        resources::query_create_resources(requests,3,callback([this](resources::queries_result& q) {
            preview_scene=dynamic_cast<base_scene*>(q[0].get_unmanaged_resource().c_ptr());preview_view=dynamic_cast<base_scene_view*>(q[1].get_unmanaged_resource().c_ptr());
            preview_output=dynamic_cast<base_output_window*>(q[2].get_unmanaged_resource().c_ptr());require(preview_scene && preview_view && preview_output,"Preview scene resources missing");
            preview_registry=static_cast<vulkan::scene_resource*>(preview_scene.c_ptr())->owner;
            if(preview_closing){preview_loading=false;queue_preview_close();return;}
            resources::user_data_variant clone;clone.set(vulkan::static_model_clone_request{woodbox_model});
            resources::user_data_variant const* clone_user=&clone;
            resources::creation_request instance("m8_woodbox_preview",const_buffer("",1),resources::static_render_model_instance_class);
            resources::query_create_resources(&instance,1,callback([this](resources::queries_result& result) {
                preview_loading=false;
                if(preview_closing){queue_preview_close();return;}
                preview_model=dynamic_cast<render_model_instance*>(result[0].get_unmanaged_resource().c_ptr());
                auto* model=dynamic_cast<vulkan::static_model_resource*>(preview_model.c_ptr());
                auto* main=dynamic_cast<vulkan::static_model_resource*>(woodbox_model.c_ptr());
                require(model && main && model!=main && model->data==main->data,"Preview must have an independent instance sharing the woodbox asset");
                scene().add_model(preview_scene,preview_model,math::float4x4().identity());scene().set_view_mode(preview_view,unlit_view_mode);
                require(xray::engine::capture_control_bus::set_surface(engine.get_render_window_handle(),protocol::preview_property,preview_window),"Cannot publish preview surface");
                std::cerr<<"M8 model preview ready: woodbox_mil shared_asset=1 distinct_instance=1 size=640x480 editor_thread="<<owner<<'\n';
            },"woodbox preview instance"),allocator,&clone_user,nullptr,assert_on_fail_false);
        },"model preview scene/view/output"),allocator,users,nullptr,assert_on_fail_false);
    }
    void close_preview(platform::native_surface_handle window) {
        require(editor && threading::current_thread_id()==owner && window.valid() && preview_window==window && !stopping,"Unknown or foreign preview close");
        if(preview_closing)return;
        preview_closing=true;xray::engine::capture_control_bus::remove(engine.get_render_window_handle(),protocol::preview_property);queue_preview_close();
    }
    bool collect_closed_preview() {
        require(threading::current_thread_id()==owner,"Foreign preview collector");
        if(!preview_closing || preview_loading || (preview_output && !static_cast<vulkan::output_resource*>(preview_output.c_ptr())->closed()))return false;
        if(!preview_releasing){preview_model=nullptr;preview_scene=nullptr;preview_view=nullptr;preview_output=nullptr;preview_releasing=true;}
        if(preview_registry && preview_registry->counts()!=std::array<size_t,3>{1,1,1})return false;
        preview_registry.reset();preview_window={};preview_closing=preview_close_queued=preview_releasing=false;
        std::cerr<<"M8 model preview closed: gpu_drained=1 editor_thread="<<owner<<'\n';return true;
    }
    unsigned next_editor_command() const {
        using s=protocol::shot;
        if(!editor || !workflow || !editor_ready() || awaiting || editor_action_applied || current_shot<s::editor_props_hidden || current_shot>s::editor_overlay_restored)return 0;
        unsigned const commands[]={1,1,2,2,8,3,4,8,5,6,7,7};return commands[unsigned(current_shot)-unsigned(s::editor_props_hidden)];
    }
    void editor_command(unsigned operation) {
        require(editor && threading::current_thread_id()==owner && editor_ready(),"Unavailable/foreign editor command");
        auto& renderer=api.editor_renderer();
        if(operation==1) {
            editor_props_hidden=!editor_props_hidden;
            for(auto& model:models)if(model.opaque && model.attached==editor_props_hidden) {
                if(editor_props_hidden)scene().remove_model(target,model.value);
                else {auto transform=model.transform;if(editor_props_moved)transform.c.x+=2;scene().add_model(target,model.value,transform);}
                model.attached=!editor_props_hidden;
            }
        }
        else if(operation==2) {
            editor_props_moved=!editor_props_moved;
            for(auto const& model:models)if(model.opaque){auto transform=model.transform;if(editor_props_moved)transform.c.x+=2;scene().update_model(target,model.value,transform);}
        } else if(operation>=3 && operation<=6) {
            require(editor_cell && editor_layers.size()>1,"Terrain command source missing");
            auto registry=static_cast<vulkan::scene_resource*>(target.c_ptr())->owner;
            editor_binding_before=registry->terrain_binding_status();
            editor_binding_expected=1;
            auto tail=u32(editor_layers.size());auto const& original=editor_layers[editor_dominant];
            auto replacement=editor_layers[(editor_dominant+1)%editor_layers.size()];
            if(operation==3){require(editor_isolated && !editor_tail,"Isolate bar cell before adding a layer");renderer.terrain_add_cell_texture(target,editor_cell->m_render_model,fs_new::virtual_path_string(original.c_str()),tail);editor_tail=true;}
            else if(operation==4){require(editor_tail,"No added terrain tail");renderer.terrain_remove_cell_texture(target,editor_cell->m_render_model,tail);editor_tail=false;}
            else if(operation==5)renderer.terrain_exchange_texture(target,fs_new::virtual_path_string(original.c_str()),fs_new::virtual_path_string(replacement.c_str()));
            else {
                editor_binding_expected=0;
                for(auto const& origin:terrain_origins)if(!editor_isolated || origin.model==editor_cell)for(unsigned i=0;i<origin.cell->textures.size();++i) {
                    renderer.terrain_add_cell_texture(target,origin.model->m_render_model,fs_new::virtual_path_string(origin.cell->textures[i].c_str()),i);++editor_binding_expected;
                }
            }
            editor_binding_pending=true;editor_binding_operation=operation;
            editor_binding_deadline=std::chrono::steady_clock::now()+std::chrono::seconds(15);
        } else if(operation==7)editor_overlay=!editor_overlay;
        else if(operation==8) {
            require(editor_cell && !editor_tail,"Remove the extra layer before restoring both cells");editor_isolated=!editor_isolated;
            for(auto const& cell:terrain)if(cell!=editor_cell) {
                if(editor_isolated)scene().terrain_remove_cell(target,cell->m_render_model);
                else scene().terrain_add_cell(target,cell->m_render_model);
            }
        }
        else throw std::runtime_error("Unknown editor command");
        editor_action_applied=true;
        std::cerr<<"M8 editor action: op="<<operation<<" editor_thread="<<owner<<'\n';
    }
    ~control_world() override {
        R_ASSERT(!pending && !held_transaction);resources::unregister_cook(resources::animation_class);resources::unregister_cook(resources::cubic_spline_skeleton_animation_class);resources::unregister_cook(resources::skeleton_class);
        xray::engine::capture_control_bus::remove(engine.get_render_window_handle(),protocol::request_property);
        xray::engine::capture_control_bus::remove(engine.get_render_window_handle(),protocol::ack_property);
    }
    void runtime_rendering(bool active) {sdk_runtime_suppressed=active;}
    void enable(bool value) override {enabled=value;}
    void tick(u32 current_frame) override {
        if(editor && sdk_runtime_suppressed){render_preview();api.editor_renderer().end_frame();return;}
        try {
            if (test_range && !ready) input::clear_normalized_input_events();
            if (target && !ready && !pending && !stopping && enabled) start();
            if(editor_binding_pending) {
                auto status=static_cast<vulkan::scene_resource*>(target.c_ptr())->owner->terrain_binding_status();
                require(status.failed==editor_binding_before.failed && status.cancelled==editor_binding_before.cancelled,"Editor terrain binding failed");
                require(std::chrono::steady_clock::now()<editor_binding_deadline,"Editor terrain binding did not drain");
                if(status.requested>=editor_binding_before.requested+editor_binding_expected && !status.pending && !status.in_flight) {
                    require(status.committed>editor_binding_before.committed && !status.queued_bytes,"Editor binding did not commit");
                    editor_binding_pending=false;
                    std::cerr<<"M8 editor binding committed: op="<<editor_binding_operation<<" requested="<<(status.requested-editor_binding_before.requested)
                        <<" committed="<<(status.committed-editor_binding_before.committed)<<" queries="<<(status.queries-editor_binding_before.queries)<<" editor_thread="<<owner<<'\n';
                }
            }
            if(preview_loading) {
                // Keep presenting the main scene while preview cooks are pending.
                // Hold only its content phase, so resource latency cannot change
                // the fixed capture's actor/particle state or clear the main view.
                if(ready && enabled && !stopping) {
                    if(!preview_main_paused) {
                        for(size_t i=0;i<emitters.size();++i)scene().update_particle_system_instance(target,emitters[i],emitter_placements[i],true,true);
                        preview_main_paused=true;
                        preview_main_redraws=0;
                    }
                    render_frame(ticks ? (ticks-1)*16 : 0,output_workflow && ticks>=16 ? inspection_camera() : camera);
                    ++preview_main_redraws;
                }
            }
            else if(workflow)workflow_tick(current_frame);
            else if(xray::engine::capture_control_bus::has_flag(engine.get_render_window_handle(),xray::engine::m9_performance::hold_property) && ready && enabled && ticks>=16) {
                if(!preview_main_paused) {
                    for(size_t i=0;i<emitters.size();++i)scene().update_particle_system_instance(target,emitters[i],emitter_placements[i],true,true);
                    preview_main_paused=true;
                }
                render_frame(240,test_range ? camera : inspection_camera());
            }
            else if((output_workflow || sdk_capture) && ready && enabled && ticks>=16) {
                if(!preview_main_paused) {
                    for(size_t i=0;i<emitters.size();++i)scene().update_particle_system_instance(target,emitters[i],emitter_placements[i],true,true);
                    preview_main_paused=true;
                }
                render_frame(240,inspection_camera());
            }
            else if (ready && enabled) {
                if(preview_main_paused) {
                    for(size_t i=0;i<emitters.size();++i)scene().update_particle_system_instance(target,emitters[i],emitter_placements[i],true,false);
                    preview_main_paused=false;
                    std::cerr<<"M8 model preview main continued: held_phase="<<ticks<<" redraws="<<preview_main_redraws<<" editor_thread="<<owner<<'\n';
                }
                if (test_range) update_test_range();
                render_frame(ticks++*16,camera);
                // Capture the sixteenth content frame, independent of disk load
                // duration or how far the logic producer runs ahead of render.
                if (!output_workflow && !sdk_capture && ticks==16) require(xray::engine::capture_control_bus::set_token(engine.get_render_window_handle(),"XRAY_VULKAN_CONTROL_CAPTURE_FRAME",
                    uintptr_t(current_frame)+1),"Cannot publish capture frame");
            }
            render_preview();
        } catch (std::exception const& error) {fail(error);}
        if(editor)api.editor_renderer().end_frame();else api.game_renderer().end_frame();
    }
    void clear_resources() override {
        stopping=true;ready=false;xray::engine::capture_control_bus::remove(engine.get_render_window_handle(),"XRAY_VULKAN_CONTROL_CAPTURE_FRAME");
        xray::engine::capture_control_bus::remove(engine.get_render_window_handle(),xray::engine::m9_performance::hold_property);
        xray::engine::capture_control_bus::remove(engine.get_render_window_handle(),protocol::preview_property);
        if(preview_output)queue_preview_close();
        preview_model=nullptr;preview_scene=nullptr;preview_view=nullptr;preview_output=nullptr;preview_window={};preview_loading=false;
        preview_registry.reset();
        preview_main_paused=false;
        if (target) {
            for (auto const& emitter:emitters) scene().remove_particle_system_instance(target,emitter);
            for (auto const& tree:trees) scene().remove_speedtree_instance(target,tree,false);
            for (auto const& model:models) if(model.attached)scene().remove_model(target,model.value);
            if (range_flash_attached) scene().remove_model(target,range_flash);
            if (actor && actor_attached) scene().remove_model(target,actor->m_render_model);
            if(terrain_attached)for (auto const& cell:terrain)if(!editor_isolated || cell==editor_cell)scene().terrain_remove_cell(target,cell->m_render_model);
            if (decal_added) scene().remove_decal(target,1);
        }
        player.reset();clips={};actor=nullptr;woodbox_model=nullptr;emitters.clear();trees.clear();range_targets.clear();range_held_models.clear();models.clear();terrain.clear();materials.clear();
        range_flash=nullptr;range_flash_attached=false;
        terrain_origins.clear();editor_cell=nullptr;editor_layers.clear();editor_binding_pending=false;
        emitter_placements.clear();emitter_ids.clear();tree_placements.clear();decal_state.reset();particles=nullptr;target=nullptr;view=nullptr;output=nullptr;project.reset();decal_added=false;actor_attached=false;
    }
    void load(pcstr name,pcstr) override {
        require(std::string(name)==(test_range ? "test_range" : "vulkan_m8_control") && !project && !pending && !failed,"Unsupported control-level load or callbacks still pending");
        stopping=false;ticks=0;++generation;sdk_revision=0;terrain_attached=true;unload_registry.reset();tree_translated=decal_translated=actor_transitioning=replay_loading=false;replay_ticks=0;
        editor_props_hidden=editor_props_moved=editor_overlay=editor_tail=editor_action_applied=editor_isolated=false;
        if (sdk_blank_scene || test_range) { create_blank_scene(); return; }
        query("resources.converted/projects/vulkan_m8_control/render_scene",resources::raw_data_class,[this](resources::queries_result& q) {read_project(q);});
    }
    void unload(pcstr,bool) override {clear_resources();}
    void on_render_session_closed() override {
        if(!held_transaction)return;
        bool closed=stopping && pending_world && !pending_world->accepting();held_transaction=false;
        resources::end_query_might_destroy_this_transaction();resources::wait_and_dispatch_callbacks(false);
        CHECK_OR_EXIT(closed && !pending && late_returned,"Pending shutdown did not drain after render session close");
        std::cerr<<"M8 pending shutdown drained: pending=0 owner="<<owner<<'\n';
    }
    void on_application_activate() override {}
    void on_application_deactivate() override {}
    xray::ui::world& ui_world() override {throw std::runtime_error("Legacy UI is unsupported in the M8 render level");}
    input::world& input_world() override {throw std::runtime_error("Legacy input is unsupported in the M8 render level");}
};
}
}
