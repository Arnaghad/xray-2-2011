#include "pch.h"
#include <xray/editor/world/api.h>
#include <xray/editor/world/engine.h>
#include <xray/editor/world/world.h>
#include <xray/editor/world/host_bridge.h>
#include <xray/engine/engine_user.h>
#include <xray/render/facade/control_level.h>
#include <xray/engine/m8_output_protocol.h>
#include <xray/animation/api.h>
#include "managed_host.h"
#include "sdk_resources.h"
#include "sdk_scene.h"
#include <memory>
#include <iostream>
#include <stdexcept>
#include <cstring>
#include <cmath>
#include <limits>

namespace xray::editor {
allocator_type* g_allocator=nullptr;
namespace {
static bool animation_initialized=false;
class level_engine final : public engine_user::engine {
    xray::editor::engine& owner;HWND main,view;
public:
    level_engine(xray::editor::engine& e,HWND m,HWND v):owner(e),main(m),view(v) {}
    void exit(int code) override {if(owner.closing()){if(code)owner.set_exit_code(code);}else owner.request_exit(code);}
    void enter_editor_mode() override {owner.enter_editor_mode();}
    bool command_line_editor() override {return true;}
    pcstr get_resources_path() const override {return owner.get_resources_path();}
    HWND get_main_window_handle() const override {return main;}
    HWND get_render_window_handle() const override {return view;}
    float2 get_render_window_size() const override {RECT r{};GetClientRect(view,&r);return {float(r.right),float(r.bottom)};}
    xray::engine::console* create_game_console(ui::world&,input::world&) override {throw std::runtime_error("Legacy game console is unsupported in the editor");}
};
class editor_world_proxy final : public world {
    engine& engine_api;
    u32 const owner=threading::current_thread_id();
    hosting::world_bridge bridge{hosting::abi_version,sizeof(hosting::world_bridge)};
    std::unique_ptr<level_engine> adapter;
    std::unique_ptr<render::control_level::control_world> level;
    unsigned frame=0;
    bool startup_transaction=false;
    bool preview_opened=false;
    bool const outputs_mode=[] {char value[2]{};return GetEnvironmentVariableA("XRAY_M8_EDITOR_OUTPUT_WORKFLOW",value,2)==1 && value[0]=='1';}();
    bool const auto_preview=[] {char value[2]{};return GetEnvironmentVariableA("XRAY_M8_EDITOR_OPEN_PREVIEW",value,2)==1 && value[0]=='1';}();
    bool const cancel_startup=[] {char value[2]{};return GetEnvironmentVariableA("XRAY_M8_EDITOR_CANCEL_STARTUP",value,2)==1 && value[0]=='1';}();
    bool const sdk_mode=[this] {
        char value[8]{};
        DWORD n=GetEnvironmentVariableA("XRAY_SDK_PROPERTY_EDITOR",value,sizeof(value));
        if(n==1 && value[0]=='0')return false;
        if(n==1 && value[0]=='1')return true;
        if(outputs_mode || auto_preview || cancel_startup || engine_api.editor_workflow())return false;
        return true;
    }();
    bool const sdk_test=[] {char value[2]{};return GetEnvironmentVariableA("XRAY_SDK_PROPERTY_TEST",value,2)==1 && value[0]=='1';}();
    bool const sdk_close_pending=[] {char value[2]{};return GetEnvironmentVariableA("XRAY_SDK_CLOSE_PENDING",value,2)==1 && value[0]=='1';}();
    bool const sdk_modes_test=[] {char value[2]{};return GetEnvironmentVariableA("XRAY_SDK_MODES_TEST",value,2)==1 && value[0]=='1';}();
    bool const sdk_blank_scene=[this] {
        if(!sdk_mode || sdk_test || outputs_mode || auto_preview || cancel_startup || engine_api.editor_workflow())return false;
        char value[8]{};
        DWORD n=GetEnvironmentVariableA("XRAY_SDK_FULL_IDE",value,sizeof(value));
        if(n==1 && value[0]=='0')return false;
        return true;
    }();
    unsigned const sdk_test_steps=sdk_modes_test?21:9;
    hosting::sdk_bridge sdk{1,sizeof(hosting::sdk_bridge)};
    hosting::sdk_resources sdk_resources;
    hosting::sdk_scene sdk_scene;
    uintptr_t sdk_waiting=0;
    unsigned sdk_step=0,sdk_settle=0;
    uintptr_t output_waiting=0;
    unsigned output_next=1,output_shutdown_frames=0;
    bool output_action=false,output_close_sent=false,output_transaction=false;
    HWND output_pending_window=nullptr;
    std::shared_ptr<render::vulkan::scene_registry> output_shutdown_registry;
    static editor_world_proxy& self(void* context) {return *static_cast<editor_world_proxy*>(context);}
    void check_owner() const {if(threading::current_thread_id()!=owner)throw std::runtime_error("Editor callback changed owner");}
    static int tick_callback(void* context) {return self(context).protect([&] {self(context).step();});}
    static int render_callback(void* context,std::uint32_t frame,float const* values) {
        return self(context).protect([&] {
            auto& me=self(context);if(!me.level || !values)throw std::runtime_error("Editor render without a level/matrix");
            for(unsigned i=0;i<16;++i)if(!std::isfinite(values[i]))throw std::runtime_error("Nonfinite editor view");
            math::float4x4 matrix;std::memcpy(&matrix,values,sizeof(matrix));
            me.level->camera_view(matrix);me.sdk_scene.draw(*me.level);me.level->tick(frame);
        });
    }
    static int clear_callback(void* context) {return self(context).protect([&] {auto& me=self(context);if(me.level){me.sdk_scene.close(*me.level,me.engine_api);me.level->project_scene(nullptr);me.level->clear_resources();}});}
    static int closing_callback(void* context) {return self(context).engine_api.closing()?1:0;}
    static int exit_callback(void* context,int code) {return self(context).protect([&] {self(context).engine_api.request_exit(code);});}
    static int command_callback(void* context,std::uint32_t command) {
        try {auto& me=self(context);me.check_owner();if(!me.level || me.sdk_mode)throw std::runtime_error("Legacy command unavailable");me.level->editor_command(command);return 0;}
        catch(std::exception const& error){std::cerr<<"M8 editor command rejected: "<<error.what()<<'\n';return -1;}
    }
    static int sdk_position_callback(void* context,hosting::sdk_position* value,bool write) {
        try {
            auto& me=self(context);me.check_owner();
            if(!me.sdk_mode || !me.level || !value || value->version!=1 || value->size!=sizeof(*value) || value->reserved)
                throw std::runtime_error("Invalid SDK position request");
            auto copy=*value;
            if(write) copy.revision=me.level->write_editor_position(copy.object,copy.revision,math::float3(copy.x,copy.y,copy.z));
            else {
                u64 object=0,revision=0;math::float3 position;
                me.level->read_editor_position(object,revision,position);
                copy.object=object;copy.revision=revision;copy.x=position.x;copy.y=position.y;copy.z=position.z;
            }
            *value=copy;return 0;
        } catch(std::exception const& error) {std::cerr<<"SDK property rejected: "<<error.what()<<'\n';return -1;}
    }
    static int sdk_read(void* context,hosting::sdk_position* value) {return sdk_position_callback(context,value,false);}
    static int sdk_write(void* context,hosting::sdk_position* value) {return sdk_position_callback(context,value,true);}
    static int sdk_resource_request(void* context,hosting::sdk_resource_request const* request,hosting::sdk_resource_result* result) {
        try {
            auto& me=self(context);me.check_owner();
            if(!me.sdk_mode || !request || (request->operation==0 && request->kind!=0 && (!me.level || !me.level->editor_ready())))return -1;
            return me.sdk_resources.execute(request,result,g_allocator);
        } catch(std::exception const& error){std::cerr<<"SDK resource rejected: "<<error.what()<<'\n';return -1;}
    }
    static int sdk_scene_request(void* context,hosting::sdk_scene_request* request) {
        try {auto& me=self(context);me.check_owner();if(!me.sdk_mode || !me.level || !request)return -1;
            return me.sdk_scene.execute(*request,*me.level,*g_allocator,me.engine_api,me.sdk_resources);
        } catch(std::exception const& error){std::cerr<<"SDK scene rejected: "<<error.what()<<'\n';return -1;}
    }
    void verify_sdk_rejections() {
        hosting::sdk_position original{1,sizeof(hosting::sdk_position)};require(sdk_read(this,&original));
        for(unsigned failure=0;failure<6;++failure) {
            auto invalid=original;
            switch(failure) {
            case 0:invalid.version=2;break;
            case 1:invalid.size=0;break;
            case 2:invalid.reserved=1;break;
            case 3:++invalid.object;break;
            case 4:++invalid.revision;break;
            case 5:invalid.x=std::numeric_limits<float>::quiet_NaN();break;
            }
            auto before=invalid;
            if(sdk_write(this,&invalid)>=0 || std::memcmp(&invalid,&before,sizeof(invalid)))throw std::runtime_error("Rejected SDK write changed its payload");
        }
        auto after=original;require(sdk_read(this,&after));
        if(std::memcmp(&original,&after,sizeof(original)))throw std::runtime_error("Rejected SDK writes changed the scene");
        std::cerr<<"SDK native rejection checks: passed=6 scene_unchanged=1\n";
    }
    static int preview_callback(void* context,std::uintptr_t handle,std::uint32_t operation) {
        try {auto& me=self(context);me.check_owner();if(!me.level)throw std::runtime_error("Preview without editor level");
            auto window=reinterpret_cast<HWND>(handle);
            if(me.engine_api.background_capture() && operation==0)me.protect_window(window);
            if(operation==0)me.level->open_preview(window);else if(operation==1)me.level->close_preview(window);else throw std::runtime_error("Unknown preview operation");return 0;}
        catch(std::exception const& error){std::cerr<<"M8 editor preview rejected: "<<error.what()<<'\n';return -1;}
    }
    template<class F> int protect(F operation) {
        try {check_owner();operation();return 0;}
        catch(std::exception const& error) {
            std::cerr<<"M8 editor callback failed: "<<error.what()<<'\n';if(engine_api.closing())engine_api.set_exit_code(2);else engine_api.request_exit(2);return -1;
        }
    }
    void require(int status) {if(status<0)throw std::runtime_error("Managed editor callback failed");}
    void protect_window(HWND window) {
        check_owner();
        if(!IsWindow(window) || GetWindowThreadProcessId(window,nullptr)!=GetCurrentThreadId())throw std::runtime_error("Foreign output workflow HWND");
        auto flags=GetWindowLongPtr(window,GWL_EXSTYLE);
        SetWindowLongPtr(window,GWL_EXSTYLE,flags|WS_EX_LAYERED|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT|WS_EX_TOOLWINDOW);
        if(!SetLayeredWindowAttributes(window,0,0,LWA_ALPHA) || !SetPropA(window,"XRAY_VULKAN_TEST_CAPTURE",HANDLE(1)))
            throw std::runtime_error("Cannot protect output workflow window");
    }
    void resize_window(HWND window,int width,int height) {
        RECT client{},outer{};if(!GetClientRect(window,&client) || !GetWindowRect(window,&outer))throw std::runtime_error("Output resize HWND missing");
        if(!SetWindowPos(window,nullptr,0,0,outer.right-outer.left+width-client.right,outer.bottom-outer.top+height-client.bottom,
            SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE))throw std::runtime_error("Output resize failed");
        GetClientRect(window,&client);if(client.right!=width || client.bottom!=height)throw std::runtime_error("Output client resize differs");
    }
    void resize_main(int width,int height) {
        RECT root{},child{};GetClientRect(main_handle(),&root);GetClientRect(view_handle(),&child);
        resize_window(main_handle(),width+root.right-child.right,height+root.bottom-child.bottom);
        GetClientRect(view_handle(),&child);if(child.right!=width || child.bottom!=height)throw std::runtime_error("Managed main panel did not resize");
    }
    void minimize_window(HWND window,bool minimize) {
        protect_window(window);
        ShowWindow(window,minimize?SW_SHOWMINNOACTIVE:SW_SHOWNOACTIVATE);
        if(!minimize)ShowWindow(window,SW_HIDE);
        if(bool(IsIconic(window))!=minimize || GetForegroundWindow()==window || GetActiveWindow()==window || GetFocus()==window || GetCapture()==window)
            throw std::runtime_error("Output minimize acquired input or did not change state");
    }
    void advance_outputs() {
        namespace op=xray::engine::m8_outputs;using s=op::shot;
        auto phase=s(output_next);auto preview=reinterpret_cast<HWND>(bridge.preview_handle(bridge.context));
        if(output_next==17) {
            if(!output_transaction) {
                if(!level->outputs_warm() || preview)return;
                output_shutdown_registry=level->registry();resources::start_query_transaction();output_transaction=true;
                require(bridge.command(bridge.context,9));output_pending_window=reinterpret_cast<HWND>(bridge.preview_handle(bridge.context));
                if(level->pending_queries()!=1 || !IsWindow(output_pending_window))throw std::runtime_error("Preview shutdown query was not held");
                std::cerr<<"M8 outputs pending preview held: queries=1 editor_thread="<<owner<<'\n';
            }
            if(++output_shutdown_frames<3)return;
            std::cerr<<"M8 outputs host close: pending_preview=1 editor_thread="<<owner<<'\n';
            SendMessage(main_handle(),WM_CLOSE,0,0);return;
        }
        if(phase==s::preview_closed || phase==s::shutdown_ready) {
            // Submit an uncaptured scene frame before WM_CLOSE. Its channel/GPU
            // work must retire before managed disposal acknowledges the close.
            if(!output_action){output_action=true;return;}
            if(preview) {
                if(!output_close_sent){SendMessage(preview,WM_CLOSE,0,0);output_close_sent=true;}
                return;
            }
            if(!level->outputs_warm())return;
        } else {
            if(phase==s::baseline || phase==s::preview_reopened) {
                if(!preview) {if(level->editor_ready())require(bridge.command(bridge.context,9));return;}
            }
            if(!level->outputs_ready())return;
            protect_window(preview);
            switch(phase) {
            case s::preview_resized:resize_window(preview,1102,673);break;
            case s::preview_restored:resize_window(preview,640,480);break;
            case s::main_resized:resize_main(1102,673);break;
            case s::main_restored:resize_main(960,640);break;
            case s::preview_minimized:minimize_window(preview,true);break;
            case s::preview_resumed:minimize_window(preview,false);break;
            case s::main_minimized:minimize_window(main_handle(),true);break;
            case s::main_resumed:minimize_window(main_handle(),false);break;
            case s::preview_hidden:level->preview_visible(false);break;
            case s::preview_visible:level->preview_visible(true);break;
            case s::main_props_hidden:case s::main_props_restored:require(bridge.command(bridge.context,1));break;
            default:break;
            }
        }
        output_waiting=op::token(frame+1,phase);
        if(!SetPropA(view_handle(),op::request_property,reinterpret_cast<HANDLE>(output_waiting)))throw std::runtime_error("Cannot publish paired capture");
        std::cerr<<"M8 outputs submit: step="<<op::name(phase)<<" editor_thread="<<owner<<'\n';
    }
    void step() {
        if(engine_api.closing())return;
        if(sdk_test && sdk_waiting) {
            if(reinterpret_cast<uintptr_t>(GetPropA(view_handle(),xray::engine::m8_control::ack_property))!=sdk_waiting)return;
            sdk_waiting=0;sdk_settle=0;
            if(++sdk_step==sdk_test_steps) {
                if(!sdk_close_pending) {engine_api.request_exit(0);return;}
                output_shutdown_registry=level->registry();resources::start_query_transaction();output_transaction=true;
                require(sdk.diagnostic_step(bridge.context,sdk_test_steps));
                output_pending_window=reinterpret_cast<HWND>(bridge.preview_handle(bridge.context));
                if(level->pending_queries()!=1 || !IsWindow(output_pending_window))throw std::runtime_error("SDK pending-close query was not held");
                std::cerr<<"SDK pending close held: queries=1 selection_live=1 editor_thread="<<owner<<'\n';
            }
        }
        if(sdk_close_pending && sdk_step==sdk_test_steps && ++output_shutdown_frames>=3) {
            std::cerr<<"SDK pending host close: query_held=1 rendered_frames="<<output_shutdown_frames-1<<" editor_thread="<<owner<<'\n';
            SendMessage(main_handle(),WM_CLOSE,0,0);return;
        }
        if(outputs_mode && output_waiting) {
            if(reinterpret_cast<uintptr_t>(GetPropA(view_handle(),xray::engine::m8_outputs::ack_property))!=output_waiting)return;
            output_waiting=0;++output_next;output_action=output_close_sent=false;
        }
        if(!engine_api.on_before_editor_tick())return;
        resources::dispatch_callbacks();
        if(!level) {
            if(!animation_initialized){animation::set_memory_allocator(*g_allocator);animation_initialized=true;}
            adapter=std::make_unique<level_engine>(engine_api,main_handle(),view_handle());
            if(cancel_startup){resources::start_query_transaction();startup_transaction=true;}
            level=std::make_unique<render::control_level::control_world>(*adapter,engine_api.get_renderer_world(),*g_allocator,true,engine_api.editor_workflow(),outputs_mode,sdk_test,sdk_blank_scene);
            std::cerr<<"M8 editor producer ready: editor_thread="<<owner<<" main="<<reinterpret_cast<uintptr_t>(main_handle())<<" view="<<reinterpret_cast<uintptr_t>(view_handle())<<'\n';
            if(cancel_startup) {
                std::cerr<<"M8 editor startup cancel: query_held=1 editor_thread="<<owner<<'\n';
                level->tick(frame++);engine_api.on_after_editor_tick();engine_api.request_exit(0);return;
            }
        }
        if(level->collect_closed_preview())require(bridge.preview_closed(bridge.context));
        level->prepare_ready();
        require(bridge.editor_mode(bridge.context,level->editor_ready()?1:0));
        if(sdk_test && level->outputs_warm()) {
            if(sdk_settle==0) {
                if(sdk_step==0)verify_sdk_rejections();
                require(sdk.diagnostic_step(bridge.context,sdk_step));
            }
            // Allow temporal render effects to settle at the fixed content phase.
            if(level->registry()->texture_reload_status().pending) {sdk_settle=1;}
            else if(++sdk_settle==32) {
                namespace p=xray::engine::m8_control;
                sdk_waiting=p::token(frame+1,p::shot(unsigned(sdk_modes_test?p::shot::sdk_mode_0:p::shot::sdk_property_baseline)+sdk_step));
                if(!SetPropA(view_handle(),p::request_property,reinterpret_cast<HANDLE>(sdk_waiting)))throw std::runtime_error("Cannot request SDK capture");
            }
        }
        if(outputs_mode)advance_outputs();
        if(auto_preview && !preview_opened && level->editor_ready()) {require(bridge.command(bridge.context,9));preview_opened=true;}
        if(auto command=level->next_editor_command())require(bridge.command(bridge.context,command));
        auto matrix=level->camera_view();
        render(frame,matrix);
        engine_api.on_after_editor_tick();++frame;
    }
public:
    explicit editor_world_proxy(engine& e):engine_api(e) {
        check_owner();
        if((sdk_test && (!sdk_mode || !e.background_capture())) || (sdk_mode && (outputs_mode || auto_preview || cancel_startup || e.editor_workflow())))
            throw std::runtime_error("SDK properties require an independent editor session; diagnostics require background capture");
        if(sdk_close_pending && !sdk_test)throw std::runtime_error("SDK pending-close requires the SDK background diagnostic");
        if(outputs_mode && (!e.background_capture() || auto_preview || cancel_startup || e.editor_workflow()))
            throw std::runtime_error("Output workflow requires only background editor capture");
        hosting::callbacks native{hosting::abi_version,sizeof(hosting::callbacks),this,
            e.background_capture()?hosting::background:0,0,tick_callback,render_callback,clear_callback,closing_callback,exit_callback,command_callback,preview_callback};
        auto status=hosting::factory()(&native,&bridge);
        if(status<0)throw std::runtime_error("Managed editor world creation failed");
        if(bridge.version!=hosting::abi_version || bridge.size!=sizeof(bridge) || !bridge.context
            || !bridge.load || !bridge.run || !bridge.clear || !bridge.destroy || !bridge.main_handle || !bridge.view_handle
            || !bridge.editor_mode || !bridge.render || !bridge.message_out || !bridge.command || !bridge.preview_handle || !bridge.preview_closed) {
            if(bridge.destroy && bridge.context)bridge.destroy(bridge.context);
            throw std::runtime_error("Invalid managed editor bridge");
        }
        std::cerr<<"M8 editor bridge created: ABI="<<bridge.version<<" callbacks="<<sizeof(native)<<" bridge="<<sizeof(bridge)<<" editor_thread="<<owner<<'\n';
        if(sdk_mode) {
            try {
                hosting::sdk_callbacks callbacks{1,sizeof(hosting::sdk_callbacks),this,sdk_test?1u:0u,0,sdk_read,sdk_write};
                require(hosting::sdk_factory()(bridge.context,&callbacks,&sdk));
                hosting::sdk_resource_callbacks resources{1,sizeof(hosting::sdk_resource_callbacks),this,sdk_resource_request};
                require(hosting::resource_factory()(bridge.context,&resources));
                hosting::sdk_scene_callbacks scene{1,sizeof(hosting::sdk_scene_callbacks),this,sdk_scene_request};
                require(hosting::scene_factory()(bridge.context,&scene));
                sdk_scene.set_interactive_default_sky(!e.background_capture());
                if(sdk.version!=1 || sdk.size!=sizeof(sdk) || !sdk.diagnostic_step)throw std::runtime_error("Invalid SDK extension");
                std::cerr<<"SDK property bridge bound: ABI=1 snapshot=40 callbacks=40\n";
            } catch(...) {bridge.destroy(bridge.context);bridge={};throw;}
        }
        if(e.background_capture()) {
            try {
                protect_window(main_handle());
                if(outputs_mode && !SetPropA(view_handle(),xray::engine::m8_outputs::mode_property,HANDLE(1)))throw std::runtime_error("Cannot enable output workflow");
            } catch(...) {bridge.destroy(bridge.context);bridge={};throw;}
        }
    }
    ~editor_world_proxy() override {
        check_owner();R_ASSERT(!level);
        require(bridge.destroy(bridge.context));bridge={};
        std::cerr<<"M8 editor bridge destroyed: editor_thread="<<owner<<'\n';
    }
    void load() override {check_owner();require(bridge.load(bridge.context));}
    void run() override {check_owner();if(bridge.run(bridge.context)<0)engine_api.request_exit(2);}
    void clear_resources() override {check_owner();require(bridge.clear(bridge.context));sdk_resources.close();}
    void on_render_session_closed() override {
        check_owner();
        if(startup_transaction){resources::end_query_might_destroy_this_transaction();startup_transaction=false;}
        if(output_transaction){resources::end_query_might_destroy_this_transaction();output_transaction=false;}
        if(level){level->on_render_session_closed();resources::wait_and_dispatch_callbacks(false);
            if((outputs_mode || sdk_close_pending) && output_pending_window) {
                if(!output_shutdown_registry || output_shutdown_registry->accepting() || level->pending_queries() || level->rejected_previews()!=1
                    || !IsWindow(output_pending_window) || !IsWindow(main_handle()))throw std::runtime_error("Pending preview shutdown did not drain before HWND disposal");
                std::cerr<<"M8 outputs pending preview drained: queries=0 rejected=1 hwnds_alive=1 editor_thread="<<owner<<'\n';
                if(sdk_close_pending)std::cerr<<"SDK pending close drained: queries=0 rejected=1 hwnds_alive=1 accepting=0 editor_thread="<<owner<<'\n';
                output_shutdown_registry.reset();
            }
            sdk_scene.drained();level.reset();adapter.reset();}
        if(cancel_startup)std::cerr<<"M8 editor startup cancel drained: pending=0 editor_thread="<<owner<<'\n';
        std::cerr<<"M8 editor native resources drained: editor_thread="<<owner<<'\n';
    }
    HWND main_handle() override {check_owner();return reinterpret_cast<HWND>(bridge.main_handle(bridge.context));}
    HWND view_handle() override {check_owner();return reinterpret_cast<HWND>(bridge.view_handle(bridge.context));}
    void editor_mode(bool enabled) override {check_owner();require(bridge.editor_mode(bridge.context,enabled?1:0));}
    void render(u32 id,math::float4x4 matrix) override {
        check_owner();require(bridge.render(bridge.context,id,reinterpret_cast<float const*>(&matrix)));
    }
    int exit_code() const override {return engine_api.get_exit_code();}
    void message_out(pcstr message) override {check_owner();require(bridge.message_out(bridge.context,message));}
};
editor_world_proxy* active=nullptr;
}
world* create_world(engine& engine_api) {
    try {
        if(!g_allocator || active)throw std::runtime_error("Editor allocator missing or world already exists");
        active=new editor_world_proxy(engine_api);return active;
    } catch(std::exception const& error) {std::cerr<<"M8 editor startup failed: "<<error.what()<<'\n';return nullptr;}
}
void destroy_world(world*& instance) {
    R_ASSERT(instance==active && active);delete active;active=nullptr;instance=nullptr;
}
void set_memory_allocator(allocator_type& allocator) {
    R_ASSERT(!active && (!g_allocator || g_allocator==&allocator));g_allocator=&allocator;
}
}
