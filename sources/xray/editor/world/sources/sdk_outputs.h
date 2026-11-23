#pragma once
#include <xray/editor/world/sdk_scene_bridge.h>
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/vulkan/decal_resources.h>
#include <xray/render/vulkan/model_resources.h>
#include <xray/render/vulkan/skeletal_resources.h>
#include <xray/render/facade/ui_draw_options.h>
#include <xray/render/facade/debug_renderer.h>
#include <xray/physics/editor_physics_scene.h>
#include <chrono>
#include <cmath>
#include <map>
#include <memory>

namespace xray::editor::hosting {
class sdk_outputs {
    struct output {
        sdk_output_settings settings{};
        std::string asset;
        unsigned pending=0;
        bool failed=false,closing=false,close_queued=false,attached=false,decal_attached=false,shutdown=false;
        u64 frames=0;
        render::scene_ptr scene;
        render::scene_view_ptr view;
        render::render_output_window_ptr target;
        render::render_model_instance_ptr model;
        resources::unmanaged_resource_ptr image,material;
        std::vector<resources::unmanaged_resource_ptr> faces;
        std::shared_ptr<render::vulkan::texture_data const> face_source;
        std::unique_ptr<physics::editor_physics_scene> physics;
        unsigned physics_body=0;
        std::chrono::steady_clock::time_point physics_clock;
    };
    std::map<u64,std::shared_ptr<output>> outputs;
    u64 sequence=0;
    bool stopping=false;
    memory::base_allocator* face_allocator=nullptr;
    static void create_faces(std::shared_ptr<output> const& o,std::shared_ptr<render::vulkan::texture_data const> data,memory::base_allocator& allocator) {
        unsigned levels=data->description.mip_levels,layers=data->description.array_layers;
        o->faces.clear();o->face_source=data;
        if(!levels || layers<2 || layers>64 || data->mips.size()!=size_t(layers)*levels){o->failed=true;return;}
        o->faces.assign(layers,{});
        for(unsigned face=0;face<layers;++face){
            auto slice=std::make_shared<render::vulkan::texture_data>();slice->description=data->description;slice->description.type=render::rhi::texture_type::type_2d;slice->description.array_layers=1;
            slice->mips.assign(data->mips.begin()+size_t(face)*levels,data->mips.begin()+size_t(face+1)*levels);resources::user_data_variant user;user.set(render::vulkan::texture_view_request{slice});++o->pending;
            try{resources::query_create_resource("sdk_cube_face",const_buffer("",1),resources::texture_class,[o,face](resources::queries_result& ready){--o->pending;if(o->shutdown)return;if(!ready.is_successful()){o->failed=true;return;}o->faces[face]=ready[0].get_unmanaged_resource();},&allocator,&user,nullptr,assert_on_fail_false);}
            catch(...){--o->pending;o->failed=true;return;}
        }
    }
    static void validate(sdk_output_settings const& s,bool create) {
        if(s.version!=1 || s.size!=sizeof(s) || s.kind>3 || (s.flags&~7u) || ((s.flags&6u) && s.kind!=1) || s.reserved || (create?s.id!=0:s.id==0))throw std::runtime_error("Invalid SDK output settings");
        auto hwnd=reinterpret_cast<HWND>(s.window);
        if(!IsWindow(hwnd) || GetWindowThreadProcessId(hwnd,nullptr)!=GetCurrentThreadId())throw std::runtime_error("Foreign SDK output HWND");
        math::float4x4 camera;std::memcpy(&camera,s.camera,sizeof(camera));
        for(auto const& row:camera.elements)for(float f:row)if(!std::isfinite(f))throw std::runtime_error("Nonfinite preview camera");
        if(camera.e03 || camera.e13 || camera.e23 || camera.e33!=1 || std::abs(camera.i.xyz().length()-1)>1e-3f || std::abs(camera.j.xyz().length()-1)>1e-3f || std::abs(camera.k.xyz().length()-1)>1e-3f
            || std::abs(math::dot_product(camera.i.xyz(),camera.j.xyz()))>1e-3f || std::abs(math::dot_product(camera.i.xyz(),camera.k.xyz()))>1e-3f || std::abs(math::dot_product(camera.j.xyz(),camera.k.xyz()))>1e-3f)
            throw std::runtime_error("Preview camera must be orthonormal");
        if(!std::isfinite(s.fov) || s.fov<1 || s.fov>175 || !std::isfinite(s.near_plane) || s.near_plane<.001f || !std::isfinite(s.far_plane) || s.far_plane<=s.near_plane || s.far_plane>1e6f)
            throw std::runtime_error("Invalid preview projection");
    }
    static sdk_output_status status(u64 id,output const& o) {
        auto* target=static_cast<render::vulkan::output_resource*>(o.target.c_ptr());
        auto size=target?target->client_size():math::uint2(0,0);
        sdk_output_status result{1,sizeof(sdk_output_status),o.closing && !o.pending && (!target || target->closed())?3u:o.failed?2u:o.pending?0u:1u,o.pending,id,o.settings.window,size.x,size.y,o.frames};
        if(auto* model=dynamic_cast<render::vulkan::static_model_resource*>(o.model.c_ptr()))for(unsigned i=0;i<3;++i){result.minimum[i]=model->geometry_minimum[i];result.maximum[i]=model->geometry_maximum[i];}
        if(o.settings.flags&2u)result.maximum[1]+=2;
        return result;
    }
    static void queue_close(output& o,render::scene_renderer& renderer) {
        if(o.decal_attached && o.scene){renderer.remove_decal(o.scene,1);o.decal_attached=false;}
        if(o.target && !o.close_queued){if(!static_cast<render::vulkan::output_resource*>(o.target.c_ptr())->closed())renderer.close_output(o.target);o.close_queued=true;}
    }
public:
    int request(sdk_scene_request& r,render::control_level::control_world& level,memory::base_allocator& allocator,std::shared_ptr<render::vulkan::static_model_data const> prepared={}) {
        face_allocator=&allocator;
        if(r.operation==26){
            if(stopping || !r.data || !r.result || r.count || r.query)return -1;
            auto const& pose=*static_cast<sdk_output_pose const*>(r.data);auto found=outputs.find(pose.output);
            if(pose.version!=1 || pose.size!=sizeof(pose) || pose.reserved || found==outputs.end() || !pose.matrices || !pose.count || pose.count>80)return -1;
            auto& o=*found->second;if(o.closing || o.failed || o.pending || o.settings.kind!=1 || (o.settings.flags&2) || !o.model)return -1;
            auto* model=dynamic_cast<render::vulkan::static_model_resource*>(o.model.c_ptr());
            if(!model || model->data->inverse_bind_pose.size()!=pose.count)return -1;
            std::vector<math::float4x4> matrices(pose.count),palette;std::memcpy(matrices.data(),pose.matrices,matrices.size()*sizeof(math::float4x4));
            render::vulkan::validate_skeleton_pose(matrices.data(),pose.count);
            for(unsigned i=0;i<pose.count;++i)palette.push_back(model->data->inverse_bind_pose[i]*matrices[i]);
            // Validate every skin before queuing a mutation on the renderer thread.
            for(auto const& surface:model->data->surfaces){if(!surface.skin)return -1;render::vulkan::skin_skeletal_mesh(*surface.skin,palette);}
            level.editor_scene_renderer().update_skeleton(o.model,matrices.data(),pose.count);
            *static_cast<sdk_output_status*>(r.result)=status(pose.output,o);return 0;
        }
        if(!r.data || r.count || (r.query && r.operation!=25) || !r.result)return -1;
        auto const input=*static_cast<sdk_output_settings const*>(r.data);
        if(r.operation==20 || r.operation==25) {
            if(stopping || outputs.size()>=8)return -1;validate(input,true);
            if((r.operation==25 && (!prepared || input.kind!=1)) || (r.operation==20 && prepared))return -1;
            for(auto const& [id,o]:outputs)if(o->settings.window==input.window)return -1;
            auto o=std::make_shared<output>();o->settings=input;o->settings.asset=nullptr;
            if(input.kind) {
                if(!input.asset || !*input.asset || std::strlen(input.asset)>1024)return -1;
                o->asset=input.asset;configs::checked::validate_path(o->asset);
            }
            auto id=++sequence;outputs.emplace(id,o);
            if(auto* test=std::getenv("XRAY_SDK_PROPERTY_TEST");test && std::strcmp(test,"1")==0)
                if(!SetPropA(reinterpret_cast<HWND>(input.window),"XRAY_VULKAN_TEST_CAPTURE",HANDLE(1)))throw std::runtime_error("Cannot opt SDK output into fenced capture");
            resources::user_data_variant window;window.set(reinterpret_cast<HWND>(input.window));
            resources::user_data_variant const* users[]={nullptr,nullptr,&window};
            resources::creation_request requests[]={{"sdk_preview_scene",const_buffer("",1),resources::scene_class},
                {"sdk_preview_view",const_buffer("",1),resources::scene_view_class},{"sdk_preview_output",const_buffer("",1),resources::render_output_window_class}};
            ++o->pending;
            try {resources::query_create_resources(requests,3,[o](resources::queries_result& q) {
                --o->pending;
                if(o->shutdown)return;
                if(!q.is_successful()){o->failed=true;return;}
                o->scene=dynamic_cast<render::base_scene*>(q[0].get_unmanaged_resource().c_ptr());o->view=dynamic_cast<render::base_scene_view*>(q[1].get_unmanaged_resource().c_ptr());
                o->target=dynamic_cast<render::base_output_window*>(q[2].get_unmanaged_resource().c_ptr());
                if(!o->scene || !o->view || !o->target)o->failed=true;
            },&allocator,users,nullptr,assert_on_fail_false);
            if(input.kind) {
                ++o->pending;auto type=input.kind==1?(o->asset.ends_with(".skinned_model/render")?resources::skeleton_render_model_instance_class:resources::static_render_model_instance_class):input.kind==2?resources::texture_class:resources::material_class;
                auto loaded=[o,&allocator](resources::queries_result& q){
                    --o->pending;if(o->shutdown)return;if(!q.is_successful()){o->failed=true;return;}
                    if(o->settings.kind==1)o->model=dynamic_cast<render::render_model_instance*>(q[0].get_unmanaged_resource().c_ptr());
                    else if(o->settings.kind==2){o->image=q[0].get_unmanaged_resource();auto* texture=dynamic_cast<render::vulkan::texture_resource*>(o->image.c_ptr());
                        auto data=texture?texture->snapshot():nullptr;
                        if(!data)o->failed=true;
                        else if((data->description.type==render::rhi::texture_type::type_cube && data->description.array_layers==6)
                            || (data->description.type==render::rhi::texture_type::type_2d_array && data->description.array_layers>=2 && data->description.array_layers<=64)) {
                            create_faces(o,data,allocator);
                        }else if(data->description.type!=render::rhi::texture_type::type_2d || data->description.array_layers!=1)o->failed=true;}
                    else o->material=q[0].get_unmanaged_resource();
                    if(!o->model && !o->image && !o->material)o->failed=true;
                };
                if(prepared){resources::user_data_variant data;data.set(render::vulkan::authored_model_request{prepared});
                    resources::query_create_resource("sdk_prepared_preview_model",const_buffer("",1),resources::static_render_model_instance_class,loaded,&allocator,&data,nullptr,assert_on_fail_false);
                }else resources::query_resource(o->asset.c_str(),type,loaded,&allocator,nullptr,nullptr,assert_on_fail_false);
                if(input.kind==3) {
                    ++o->pending;
                    resources::query_resource("resources/models/props/box/woodbox_mil.model/render",resources::static_render_model_instance_class,[o](resources::queries_result& q){
                        --o->pending;if(o->shutdown)return;if(!q.is_successful()){o->failed=true;return;}
                        o->model=dynamic_cast<render::render_model_instance*>(q[0].get_unmanaged_resource().c_ptr());if(!o->model)o->failed=true;
                    },&allocator,nullptr,nullptr,assert_on_fail_false);
                }
            }}catch(...){o->closing=true;o->failed=true;throw;}
            auto result=status(id,*o);*static_cast<sdk_output_status*>(r.result)=result;return 0;
        }
        auto found=outputs.find(input.id);if(found==outputs.end() || input.version!=1 || input.size!=sizeof(input))return -1;
        auto& o=*found->second;
        if(r.operation==21) {
            if(stopping || o.closing)return -1;validate(input,false);
            if(input.window!=o.settings.window || input.kind!=o.settings.kind || input.asset)return -1;
            o.settings=input;
        } else if(r.operation==23) {o.closing=true;queue_close(o,level.editor_scene_renderer());}
        else if(r.operation!=24)return -1;
        resources::dispatch_callbacks();
        if(o.closing)queue_close(o,level.editor_scene_renderer());
        auto result=status(input.id,o);*static_cast<sdk_output_status*>(r.result)=result;
        if(result.state==3)outputs.erase(found);
        return 0;
    }
    void tick(render::control_level::control_world& level,bool project_open) {
        auto& renderer=level.editor_scene_renderer();
        for(auto const& [id,ptr]:outputs) {auto& o=*ptr;
            if(o.closing){queue_close(o,renderer);continue;}
            if(o.pending || o.failed || !(o.settings.flags&1))continue;
            if(!o.target || !o.view || !o.scene)continue;
            auto* target=static_cast<render::vulkan::output_resource*>(o.target.c_ptr());auto size=target->client_size();if(!size.x || !size.y)continue;
            if(o.model && !o.attached) {
                auto* decal_material=dynamic_cast<render::vulkan::decal_material_resource*>(o.material.c_ptr());
                if(o.material && !decal_material) {
                    auto* model=static_cast<render::vulkan::static_model_resource*>(o.model.c_ptr());
                    for(auto const& surface:model->data->surfaces)renderer.set_model_material(o.model,fs_new::virtual_path_string(surface.name.c_str()),o.material);
                }
                renderer.add_model(o.scene,o.model,math::float4x4().identity());
                if(decal_material) {
                    auto* model=static_cast<render::vulkan::static_model_resource*>(o.model.c_ptr());
                    math::float3 center(
                        (model->geometry_minimum[0]+model->geometry_maximum[0])*0.5f,
                        (model->geometry_minimum[1]+model->geometry_maximum[1])*0.5f,
                        (model->geometry_minimum[2]+model->geometry_maximum[2])*0.5f);
                    math::float3 extent(
                        (model->geometry_maximum[0]-model->geometry_minimum[0])*0.5f,
                        (model->geometry_maximum[1]-model->geometry_minimum[1])*0.5f,
                        (model->geometry_maximum[2]-model->geometry_minimum[2])*0.5f);
                    render::decal_properties decal;
                    decal.transform=math::create_translation(center);
                    decal.material=o.material;
                    decal.width_height_far_distance=math::float3(std::clamp(extent.x,0.25f,4.0f),std::clamp(extent.y,0.25f,4.0f),std::clamp(extent.z*2.0f,0.25f,4.0f));
                    decal.alpha_angle=-1.0f;decal.clip_angle=-1.0f;
                    decal.projection_on_terrain_geometry=true;
                    decal.projection_on_static_geometry=true;
                    decal.projection_on_speedtree_geometry=false;
                    decal.projection_on_skeleton_geometry=false;
                    decal.projection_on_particle_geometry=false;
                    renderer.add_decal(o.scene,1,decal);
                    o.decal_attached=true;
                }
                o.attached=true;
            }
            if(o.model){auto* model=static_cast<render::vulkan::static_model_resource*>(o.model.c_ptr());auto const& source=model->data->physics;auto transform=math::float4x4().identity();
                if((o.settings.flags&2) && !source.primitives.empty()){
                    if(!o.physics){auto candidate=std::make_unique<physics::editor_physics_scene>();physics::model_physics_parameters floor;floor.primitives.push_back({1,{0,0,0},{0,0,0},{100,.05f,100}});candidate->add(floor,math::create_translation(math::float3(0,model->geometry_minimum[1]-.05f,0)));o.physics_body=candidate->add(source,math::create_translation(math::float3(0,2,0)));o.physics=std::move(candidate);o.physics_clock=std::chrono::steady_clock::now();}
                    auto now=std::chrono::steady_clock::now();float seconds=float(std::chrono::duration<double>(now-o.physics_clock).count());o.physics_clock=now;o.physics->step(std::clamp(seconds,0.f,.1f));transform=o.physics->transform(o.physics_body);renderer.update_model(o.scene,o.model,transform);
                }
                if(o.settings.flags&4)for(auto const& p:source.primitives){auto matrix=math::create_rotation(p.rotation)*math::create_translation(p.position+source.mass_center)*transform;auto& debug=level.editor_debug_renderer();math::color color(80,255,120);if(p.type==0)debug.draw_sphere(o.scene,matrix.c.xyz(),p.scale.x,color,false);else if(p.type==1)debug.draw_cube(o.scene,matrix,p.scale,color,false);else debug.draw_cylinder(o.scene,matrix,math::float3(p.scale.y,p.scale.x,p.scale.y),color,false);}
            }
            math::float4x4 camera;std::memcpy(&camera,o.settings.camera,sizeof(camera));
            auto projection=math::create_perspective_projection(o.settings.fov*math::pi/180,float(size.x)/size.y,o.settings.near_plane,o.settings.far_plane);
            renderer.set_view_matrix(o.view,math::invert4x3(camera));renderer.set_projection_matrix(o.view,projection);renderer.set_view_mode(o.view,o.material?render::lit_view_mode:render::unlit_view_mode);
            if(o.material) {
                render::light_props light={};light.type=render::light_type_point;light.transform=camera;light.range=100;light.intensity=5;light.color=0xffffffff;light.attenuation_power=2;light.diffuse_influence_factor=light.specular_influence_factor=1;
                if(o.frames)renderer.update_light(o.scene,1,light);else renderer.add_light(o.scene,1,light);
            }
            if(o.image) {
                auto texture=static_cast<render::vulkan::texture_resource*>(o.image.c_ptr())->snapshot();
                bool multi=texture->description.type==render::rhi::texture_type::type_cube || texture->description.type==render::rhi::texture_type::type_2d_array;
                if(multi && texture!=o.face_source){create_faces(ptr,texture,*face_allocator);continue;}
                if(!multi && !o.faces.empty()){o.faces.clear();o.face_source.reset();}
                auto desc=texture->description;unsigned count=o.faces.empty()?1u:unsigned(o.faces.size());
                unsigned columns=count==1?1u:count==6?3u:unsigned(std::ceil(std::sqrt(double(count)))),rows=(count+columns-1)/columns;
                float fit=std::min(float(size.x)/desc.width,float(size.y)/desc.height),w=desc.width*fit,h=desc.height*fit,x=(size.x-w)*.5f,y=(size.y-h)*.5f;
                if(!o.faces.empty()){float side=std::min(float(size.x)/columns,float(size.y)/rows);w=h=side;x=(size.x-side*columns)*.5f;y=(size.y-side*rows)*.5f;}
                for(unsigned face=0;face<count;++face){float left=x+(face%columns)*w,top=y+(face/columns)*h;
                    render::ui::vertex quad[]={render::ui::vertex(left,top+h,0,0xffffffff,0,1),render::ui::vertex(left,top,0,0xffffffff,0,0),render::ui::vertex(left+w,top+h,0,0xffffffff,1,1),render::ui::vertex(left+w,top,0,0xffffffff,1,0)};
                    render::ui::draw_options options;options.texture=o.faces.empty()?o.image:o.faces[face];level.editor_ui_renderer().draw_vertices(o.view,quad,quad+4,0,1,options);
                }
            }
            level.draw_editor_output(!o.settings.kind && project_open?level.editor_scene():o.scene,o.view,o.target);
            ++o.frames;
        }
    }
    void close(render::control_level::control_world& level) {
        stopping=true;
        for(auto& [id,o]:outputs){
            o->closing=o->shutdown=true;queue_close(*o,level.editor_scene_renderer());
            // Queued renderer commands retain their frame inputs. Release cook
            // resources before the renderer unregisters/finalizes its cooks;
            // the managed surface lease stays alive until the shutdown ack.
            o->physics.reset();o->model=nullptr;o->material=nullptr;o->faces.clear();o->face_source.reset();o->image=nullptr;o->scene=nullptr;o->view=nullptr;o->target=nullptr;
        }
    }
    void drained() {
        for(auto const& [id,o]:outputs)if(o->pending || (o->target && !static_cast<render::vulkan::output_resource*>(o->target.c_ptr())->closed()))throw std::runtime_error("SDK output resources still pending after renderer drain");
        outputs.clear();
    }
};
}
