#pragma once
#include <xray/engine/engine_user.h>
#include <xray/render/world.h>
#include <xray/render/facade/game_renderer.h>
#include <xray/render/facade/editor_runtime.h>
#include <xray/render/vulkan/scene_resources.h>
#include <xray/physics/editor_physics_scene.h>
#include <chrono>
#include <algorithm>
#include <stdexcept>
#include <iostream>
namespace stalker2 {
using namespace xray;
namespace vulkan=xray::render::vulkan;

// Actual editor-launched game world. It consumes a typed authored snapshot;
// there is no manifest/control fixture or second asset/cook owner here.
class editor_game_world final : public xray::engine_user::world {
    using editor_runtime_request=xray::render::editor_runtime_request;
    xray::render::world& api;
    u32 const owner=xray::threading::current_thread_id();
    bool enabled=false,stopping=false,runtime_focused=true;
    editor_runtime_request authored_runtime;
    std::unique_ptr<physics::editor_physics_scene> authored_physics;
    unsigned runtime_steps=0;
    std::chrono::steady_clock::time_point runtime_clock;
    static void require(bool condition,char const* message){if(!condition)throw std::runtime_error(message);}
    xray::render::scene_renderer& scene(){return api.game_renderer().scene();}
public:
    explicit editor_game_world(xray::render::world& render):api(render){}
    bool editor_runtime(editor_runtime_request& request) override {
        require(threading::current_thread_id()==owner,"Runtime request requires the logic owner");
        if(request.operation==0) {
            require(!stopping && !authored_runtime.scene && request.scene && request.view && request.output,"Runtime session unavailable");
            // Reject a foreign/retired renderer session before enabling game.
            auto* scene=dynamic_cast<vulkan::scene_resource*>(request.scene.c_ptr());
            auto* view=dynamic_cast<vulkan::view_resource*>(request.view.c_ptr());
            auto* output=dynamic_cast<vulkan::output_resource*>(request.output.c_ptr());
            require(scene && view && output && scene->owner==view->owner && scene->owner==output->owner && scene->owner->accepting() && !output->closed(),"Invalid authored runtime ownership");
            std::unique_ptr<physics::editor_physics_scene> candidate;
            if(!request.bodies.empty() || !request.terrain_bodies.empty()){
                candidate=std::make_unique<physics::editor_physics_scene>();
                for(auto const& body:request.bodies)candidate->add(body.parameters,body.transform);
                for(auto const& terrain:request.terrain_bodies)candidate->add_static_triangles(terrain.vertices,terrain.indices,terrain.transform);
            }
            authored_physics=std::move(candidate);authored_runtime=request;runtime_focused=true;authored_runtime.state=request.paused?4:3;authored_runtime.logic_thread=owner;
            authored_runtime.ticks=authored_runtime.frames=0;authored_runtime.seconds=0;runtime_steps=0;runtime_clock=std::chrono::steady_clock::now();
            this->scene().set_view_mode(authored_runtime.view,render::lit_view_mode);
            this->scene().set_lighting_environment(authored_runtime.view,authored_runtime.environment);
            this->scene().set_postprocess_settings(authored_runtime.view,authored_runtime.postprocess);
            std::cerr<<"SDK runtime logic started: objects="<<request.objects<<" logic_thread="<<owner<<" mode="<<(request.simulate?"simulate":"play")<<'\n';
        } else if(request.operation==1) {
            require(bool(authored_runtime.scene),"No authored runtime");authored_runtime.camera=request.camera;authored_runtime.projection=request.projection;
            authored_runtime.paused=request.paused;authored_runtime.state=request.paused?4:3;
        } else if(request.operation==2) {
            authored_physics.reset();authored_runtime.bodies.clear();authored_runtime.terrain_bodies.clear();
            authored_runtime.scene=nullptr;authored_runtime.view=nullptr;authored_runtime.output=nullptr;authored_runtime.state=5;runtime_steps=0;
            std::cerr<<"SDK runtime logic stopped: ticks="<<authored_runtime.ticks<<" frames="<<authored_runtime.frames<<" logic_thread="<<owner<<'\n';
        } else if(request.operation==4) {
            require(authored_runtime.scene && authored_runtime.paused,"Single step requires paused runtime");++runtime_steps;
        } else if(request.operation==5)runtime_focused=!request.paused;
        else if(request.operation!=3 && request.operation!=6)return false;
        if(authored_runtime.scene)authored_runtime.state=authored_runtime.paused?4:runtime_focused?3:7;
        request.physics_bodies=authored_physics?authored_physics->size():unsigned(authored_runtime.bodies.size());request.physics_dynamic=0;request.physics_minimum_y=request.physics_maximum_y=0;
        for(size_t i=0;i<authored_runtime.bodies.size();++i){auto const& b=authored_runtime.bodies[i];if(b.parameters.mass>0)++request.physics_dynamic;float y=b.transform.c.y;if(!i)request.physics_minimum_y=request.physics_maximum_y=y;else{request.physics_minimum_y=std::min(request.physics_minimum_y,y);request.physics_maximum_y=std::max(request.physics_maximum_y,y);}}
        request.state=authored_runtime.state;request.logic_thread=owner;request.ticks=authored_runtime.ticks;request.frames=authored_runtime.frames;request.seconds=authored_runtime.seconds;request.objects=authored_runtime.objects;return true;
    }
    void render_authored_runtime() {
        auto now=std::chrono::steady_clock::now();double elapsed=std::chrono::duration<double>(now-runtime_clock).count();runtime_clock=now;
        if((runtime_focused && !authored_runtime.paused) || runtime_steps) {
            double seconds=authored_runtime.paused?1./60:std::clamp(elapsed,0.,.1);authored_runtime.seconds+=seconds;++authored_runtime.ticks;if(runtime_steps)--runtime_steps;
            if(authored_physics){authored_physics->step(float(seconds));for(unsigned i=0;i<authored_runtime.bodies.size();++i){auto& body=authored_runtime.bodies[i];body.transform=authored_physics->transform(i);scene().update_model(authored_runtime.scene,body.model,body.transform);}}
        }
        auto size=static_cast<vulkan::output_resource*>(authored_runtime.output.c_ptr())->client_size();if(!size.x || !size.y)return;
        scene().set_view_matrix(authored_runtime.view,math::invert4x3(authored_runtime.camera));scene().set_projection_matrix(authored_runtime.view,authored_runtime.projection);
        api.game_renderer().draw_scene(authored_runtime.scene,authored_runtime.view,authored_runtime.output,{{0,0},{1,1}});++authored_runtime.frames;
    }

    void enable(bool value) override {require(!value || bool(authored_runtime.scene),"Start an authored runtime before enabling game");enabled=value;}
    void tick(u32) override {
        require(threading::current_thread_id()==owner,"Runtime tick changed logic owner");
        if(enabled && !stopping && authored_runtime.scene)render_authored_runtime();
        api.game_renderer().end_frame();
    }
    void clear_resources() override {stopping=true;enabled=false;authored_physics.reset();authored_runtime.bodies.clear();authored_runtime.scene=nullptr;authored_runtime.view=nullptr;authored_runtime.output=nullptr;authored_runtime.state=5;}
    void load(pcstr,pcstr) override {throw std::runtime_error("Editor runtime requires a validated authored snapshot");}
    void unload(pcstr,bool) override {clear_resources();}
    void on_application_activate() override {}
    void on_application_deactivate() override {}
    xray::ui::world& ui_world() override {throw std::runtime_error("Legacy game UI is unavailable for authored static scenes");}
    xray::input::world& input_world() override {throw std::runtime_error("Authored runtime input is routed by the SDK viewport");}
};
}
