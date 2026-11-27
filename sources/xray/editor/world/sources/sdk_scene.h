#pragma once
#include <xray/editor/world/sdk_scene_bridge.h>
#include <xray/editor/base/sdk_transform.h>
#include <xray/editor/base/sdk_gizmo.h>
#include <xray/render/vulkan/terrain_resources.h>
#include <xray/render/vulkan/terrain_collision.h>
#include <xray/render/vulkan/vegetation_resources.h>
#include <xray/render/vulkan/sky_resources.h>
#include <xray/render/facade/decal_properties.h>
#include <xray/render/facade/terrain_base.h>
#include <xray/particle/world.h>
#include <map>
#include <set>
#include <string>
#include <vector>
#include <memory>
#include <chrono>
#include <cstring>
#include <algorithm>
#include <functional>
#include "sdk_outputs.h"
#include "sdk_runtime.h"

namespace xray::editor::hosting {
// Candidate resources are retained independently. No renderer mutation takes
// place until every cook has succeeded. The scene pointer is the commit point.
class sdk_scene {
    static constexpr std::uint64_t terrain_hit_id = 0x7ffffffeu;
    sdk_outputs outputs;
    sdk_runtime runtime;
    u64 material_serial=0;
    struct object {
        sdk_scene_object value{};
        sdk_world_object_extension ext{};
        std::string path,primary_uri,secondary_uri,tertiary_uri,quaternary_uri;
        render::render_model_instance_ptr model;
        render::speedtree_instance_ptr tree;
        resources::unmanaged_resource_ptr tree_bark_mat,tree_frond_mat,tree_leaf_mat;
        particle::particle_system_instance_ptr particle;
        resources::unmanaged_resource_ptr decal_material;
        math::float4x4 transform;
        bool selected=false;
    };
    struct stored_extension {
        sdk_world_object_extension ext{};
        std::string primary,secondary,tertiary,quaternary;
    };
    struct terrain_cell_entry {
        std::int32_t kx=0,ky=0;
        std::string virtual_path;
        std::vector<std::uint8_t> bytes;
        std::shared_ptr<render::vulkan::terrain_cell_data const> data;
        std::shared_ptr<render::vulkan::terrain_collision_geometry> collision;
        render::render_model_instance_ptr render_model;
        math::float3 world_min{0,0,0},world_max{0,0,0};
        bool has_bounds=false,selected=false;
    };
    struct pending_terrain_input {
        std::int32_t kx=0,ky=0;
        std::string virtual_path;
        std::vector<std::uint8_t> bytes;
        std::uint32_t flags=1;
    };
    struct candidate {
        std::uint64_t session=0;
        unsigned pending=0;
        bool failed=false,cancelled=false;
        bool incremental=false;
        bool authored_sky=false;
        bool has_world_tools=false;
        bool has_trees_particles_decals=false;
        bool has_decals=false;
        bool terrain_selected=false;
        render::scene_ptr scene;
        std::map<std::uint64_t,object> objects;
        std::vector<std::uint64_t> order;
        std::vector<terrain_cell_entry> terrain_cells;
        resources::unmanaged_resource_ptr sky_cube_texture;
        resources::unmanaged_resource_ptr sky_material;
        std::vector<std::shared_ptr<std::string>> retained_strings;
    };
    std::shared_ptr<candidate> staged;
    std::map<std::uint64_t,object> objects;
    std::vector<std::uint64_t> order;
    std::vector<terrain_cell_entry> terrain_cells;
    resources::unmanaged_resource_ptr sky_material,default_sky_material;
    std::vector<stored_extension> pending_extensions;
    std::vector<pending_terrain_input> pending_terrain;
    std::uint64_t pending_extensions_session=0,pending_terrain_session=0;
    bool live_has_world_tools=false,live_has_trees_particles_decals=false,live_has_decals=false,terrain_selected=false;
    bool interactive_default_sky=false,live_authored_sky=false;
    bool gizmo_target_valid(sdk_gizmo_settings const& s) const {
        if(!s.mode) return true;
        if(s.object==terrain_hit_id) return s.mode==1 && terrain_selected && !terrain_cells.empty();
        auto o=objects.find(s.object);
        return o!=objects.end() && (o->second.value.flags&7)==7;
    }
    std::uint64_t serial=0,session=0;
    render::scene_ptr live_scene;
    sdk_scene_statistics statistics{};
    sdk_viewport_settings viewport{};
    bool viewport_enabled=false,editor_sun=false,editor_sun_shadows=true,editor_camera_light=false;
    render::scene_ptr light_scene;
    math::float4x4 last_light_camera=math::float4x4().identity();
    memory::base_allocator* debug_allocator=nullptr;
    sdk_gizmo_settings gizmo{};
    std::vector<editor_base::sdk_gizmo_triangle> gizmo_triangles;
    static math::float3 vec(float const* p) {return {p[0],p[1],p[2]};}
    static math::float4x4 matrix(sdk_scene_object const& v) {
        return math::create_scale(vec(v.scale))*math::create_rotation(vec(v.rotation))*math::create_translation(vec(v.position));
    }
    static void validate(sdk_scene_object const& v) {
        if(!v.id || v.kind>2 || (v.flags&~7u))throw std::runtime_error("Invalid SDK scene object");
        for(auto p:{v.position,v.rotation,v.scale})for(unsigned i=0;i<3;++i)
            if(!std::isfinite(p[i]))throw std::runtime_error("Nonfinite SDK transform");
        for(float f:v.scale)if(std::abs(f)<1e-5f)throw std::runtime_error("Singular SDK transform");
        if(!std::isfinite(v.range) || !std::isfinite(v.intensity) || v.range<=0 || v.intensity<0)
            throw std::runtime_error("Invalid SDK light");
    }
    static bool triangle(math::float3 const& o,math::float3 const& d,math::float3 const& a,
        math::float3 const& b,math::float3 const& c,float& t) {
        auto e=b-a,f=c-a,h=math::cross_product(d,f);float det=math::dot_product(e,h);
        if(std::abs(det)<1e-7f)return false;
        auto s=o-a;float u=math::dot_product(s,h)/det;if(u<0 || u>1)return false;
        auto q=math::cross_product(s,e);float v=math::dot_product(d,q)/det;if(v<0 || u+v>1)return false;
        t=math::dot_product(f,q)/det;return t>=0;
    }
    static math::float3 edge(math::float3 const& p,math::float3 const& a,math::float3 const& b) {
        auto d=b-a;float n=math::dot_product(d,d);return a+d*(n>1e-12f?std::clamp(math::dot_product(p-a,d)/n,0.f,1.f):0.f);
    }
public:
    void set_interactive_default_sky(bool value){interactive_default_sky=value;}
    int execute(sdk_scene_request& r,render::control_level::control_world& level,memory::mimalloc_allocator_type& allocator,editor::engine& engine,sdk_resources& material_resources,std::function<xray::platform::native_surface_handle(std::uint64_t)> const& resolve_surface={}) {
        if(r.version!=1 || r.size!=sizeof(r))return -1;
        // A preview cook may be pending while the main view keeps presenting.
        // Projection updates and detaching the authored scene need no new cooks
        // and must remain available during that drain.
        bool drain=(r.operation>=31 && r.operation<=38) || (r.operation>=6 && r.operation<=18) || r.operation==21 || r.operation==23 || r.operation==24 || r.operation==3 || (r.operation==0 && !r.session && !r.count)
            || ((r.operation==50 || r.operation==51) && !r.session && !r.count)
            || ((r.operation==1 || r.operation==2) && staged && !staged->session);
        if(!level.editor_ready() && !drain)return -1;
        if((r.operation>=20 && r.operation<=24) || r.operation==26)return outputs.request(r,level,allocator,{},resolve_surface);
        if(r.operation==25) {if(!r.query)return -1;return outputs.request(r,level,allocator,material_resources.model_result(r.query),resolve_surface);}
        if(r.operation==44 || r.operation==45) {
            if(!r.data || !r.result || r.query || r.count || runtime.running())return -1;auto const& source=*static_cast<sdk_model_publish const*>(r.data);
            if(!source.uri || std::strlen(source.uri)>1024)return -1;std::string uri=source.uri;configs::checked::validate_path(uri);
            if(!uri.starts_with("resources/models/") || (!uri.ends_with(".model/render") && !uri.ends_with(".skinned_model/render")))return -1;
            auto model=material_resources.model_result(source.resource);
            if(r.operation==45) {
                // DCC is the explicit geometry replacement route. Ordinary model
                // material edits retain the existing same-source check in op 44.
                constexpr pcstr prefix="resources/models/__sdk6_preview/dcc/";
                if(!model->source_path.starts_with(prefix) || !model->identity.starts_with(prefix)
                    || (!model->inverse_bind_pose.empty())!=uri.ends_with(".skinned_model/render"))return -1;
                auto linked=std::make_shared<render::vulkan::static_model_data>(*model);linked->source_path=uri;model=std::move(linked);
            }
            level.registry()->publish_model(uri,std::move(model));*static_cast<u64*>(r.result)=++material_serial;return 0;
        }
        if(r.operation==42) {
            if(!r.data || r.count || r.query)return -1;auto& keys=*static_cast<sdk_material_keys*>(r.data);
            if(keys.version!=1 || keys.size!=sizeof(keys) || !keys.utf8 || !keys.capacity || keys.capacity>512*1024)return -1;
            auto names=level.registry()->material_keys();size_t bytes=0;for(auto const& name:names)bytes+=name.size()+1;if(bytes>keys.capacity)return -1;
            keys.used=0;for(auto const& name:names){std::memcpy(keys.utf8+keys.used,name.c_str(),name.size()+1);keys.used+=unsigned(name.size()+1);}return 0;
        }
        if(r.operation==43) {
            if(!r.data || !r.result || !r.count || r.count>128 || r.query || runtime.running())return -1;
            auto* bindings=static_cast<sdk_material_binding const*>(r.data);std::map<std::string,resources::unmanaged_resource_ptr> updates;
            for(unsigned i=0;i<r.count;++i){auto const& b=bindings[i];if(!b.uri || std::strlen(b.uri)>1024)return -1;std::string uri=b.uri;configs::checked::validate_path(uri);
                if((!uri.starts_with("resources/materials/") && !uri.starts_with("resources/material_instances/")) || !uri.ends_with(".material") || updates.contains(uri))return -1;
                updates.emplace(uri,material_resources.material_result(b.resource));}
            level.registry()->publish_materials(updates);*static_cast<u64*>(r.result)=++material_serial;return 0;
        }
        if(r.operation==40 || r.operation==41) {
            if(r.operation==41)resources::dispatch_callbacks();
            if(r.count || r.query || !r.result)return -1;unsigned affected=0;
            if(r.operation==40) {
                if(!r.data || runtime.running())return -1;auto const& input=*static_cast<sdk_texture_source const*>(r.data);
                if(input.version!=1 || input.size!=sizeof(input) || !input.virtual_path || !input.physical_path || std::strlen(input.virtual_path)>1024 || std::strlen(input.physical_path)>1024)return -1;
                std::string uri=input.virtual_path;configs::checked::validate_path(uri);
                if(!uri.starts_with("resources/textures/") || !uri.ends_with(".tga"))return -1;
                affected=level.registry()->publish_texture_source(uri,input.physical_path);
            }else if(r.data)return -1;
            auto s=level.registry()->texture_reload_status();*static_cast<sdk_asset_publish_status*>(r.result)={1,sizeof(sdk_asset_publish_status),affected,0,s.requested,s.reloaded,s.unchanged,s.failed,s.cancelled,s.pending};return 0;
        }
        if(r.operation==50) {
            if(staged || r.query || r.count>100000 || (r.count && !r.data) || (!r.session && r.count))return -1;
            pending_extensions.clear();pending_extensions_session=r.session;
            auto* src=static_cast<sdk_world_object_extension const*>(r.data);
            for(unsigned i=0;i<r.count;++i) {
                auto const& e=src[i];if(e.version!=1 || e.size!=sizeof(sdk_world_object_extension))return -1;
                stored_extension se;se.ext=e;
                if(e.primary_uri)se.primary=e.primary_uri;
                if(e.secondary_uri)se.secondary=e.secondary_uri;
                if(e.tertiary_uri)se.tertiary=e.tertiary_uri;
                if(e.quaternary_uri)se.quaternary=e.quaternary_uri;
                se.ext.primary_uri=se.ext.secondary_uri=se.ext.tertiary_uri=se.ext.quaternary_uri=nullptr;
                pending_extensions.push_back(std::move(se));
            }
            return 0;
        }
        if(r.operation==51) {
            if(staged || r.query || r.count>64 || (r.count && !r.data) || (!r.session && r.count))return -1;
            pending_terrain.clear();pending_terrain_session=r.session;
            auto* src=static_cast<sdk_terrain_cell_upload const*>(r.data);
            for(unsigned i=0;i<r.count;++i) {
                auto const& t=src[i];
                if(t.version!=1 || t.size!=sizeof(sdk_terrain_cell_upload) || !t.bytes || !t.byte_length || t.byte_length>32u*1024u*1024u || (t.flags&~3u)!=0)return -1;
                pending_terrain_input pti;pti.kx=t.kx;pti.ky=t.ky;pti.flags=t.flags;
                pti.virtual_path=t.virtual_path?t.virtual_path:"";
                configs::checked::validate_path(pti.virtual_path);
                auto const* raw=static_cast<std::uint8_t const*>(t.bytes);
                pti.bytes.assign(raw,raw+t.byte_length);
                pending_terrain.push_back(std::move(pti));
            }
            return 0;
        }
        if(drain && (!level.editor_view() || !level.editor_output()))return -1;
        if(runtime.running() && r.operation<=5)return -1;
        if(r.operation==0) {
            if(staged || r.query || r.count>100000 || (r.count && !r.data) || (!r.session && r.count))return -1;
            auto next=std::make_shared<candidate>();next->session=r.session;
            auto* source=static_cast<sdk_scene_object const*>(r.data);
            bool use_ext=(pending_extensions_session==r.session && pending_extensions.size()==r.count);
            for(unsigned i=0;i<r.count;++i) {
                auto const& v=source[i];validate(v);object o;o.value=v;o.transform=matrix(v);
                if(use_ext && pending_extensions[i].ext.id==v.id) {
                    auto const& se=pending_extensions[i];
                    o.ext=se.ext;o.primary_uri=se.primary;o.secondary_uri=se.secondary;o.tertiary_uri=se.tertiary;o.quaternary_uri=se.quaternary;
                    if(o.ext.world_kind>=2 && o.ext.world_kind<=8)next->has_world_tools=true;
                    if(o.ext.world_kind>=4 && o.ext.world_kind<=6)next->has_trees_particles_decals=true;
                    if(o.ext.world_kind==6)next->has_decals=true;
                }
                if(v.kind==1) {
                    if(!v.path || !*v.path || std::strlen(v.path)>1024)throw std::runtime_error("Missing SDK model URI");
                    o.path=v.path;
                    if(o.path.rfind("resources/models/",0)!=0 || !o.path.ends_with(".model/render") || o.path.find(':')!=std::string::npos || o.path.find("..")!=std::string::npos || o.path.find('\\')!=std::string::npos)
                        throw std::runtime_error("Invalid SDK model URI");
                    auto old=objects.find(v.id);
                    if(session==r.session && old!=objects.end() && old->second.path==o.path && old->second.ext.world_kind<=1)o.model=old->second.model;
                    if(session==r.session && !o.model) {
                        auto same=std::find_if(objects.begin(),objects.end(),[&](auto const& pair){return pair.second.path==o.path && pair.second.model && pair.second.ext.world_kind<=1;});
                        if(same!=objects.end())o.model=same->second.model;
                    }
                } else if(o.ext.world_kind==2) {
                    if(o.primary_uri.rfind("resources/models/",0)!=0 || !o.primary_uri.ends_with(".skinned_model/render"))
                        throw std::runtime_error("Invalid SDK skinned model URI");
                    configs::checked::validate_path(o.primary_uri);
                    auto old=objects.find(v.id);
                    if(session==r.session && old!=objects.end() && old->second.ext.world_kind==2 && old->second.primary_uri==o.primary_uri && old->second.model)
                        o.model=old->second.model;
                } else if(o.ext.world_kind==4) {
                    if(o.primary_uri.empty())throw std::runtime_error("Missing SDK speedtree name");
                    configs::checked::validate_path(o.primary_uri);
                    render::vulkan::validate_tree_transform(o.transform);
                    auto old=objects.find(v.id);
                    if(session==r.session && old!=objects.end() && old->second.ext.world_kind==4
                        && old->second.primary_uri==o.primary_uri && old->second.secondary_uri==o.secondary_uri
                        && old->second.tertiary_uri==o.tertiary_uri && old->second.quaternary_uri==o.quaternary_uri && old->second.tree) {
                        o.tree=old->second.tree;
                        o.tree_bark_mat=old->second.tree_bark_mat;
                        o.tree_frond_mat=old->second.tree_frond_mat;
                        o.tree_leaf_mat=old->second.tree_leaf_mat;
                    }
                } else if(o.ext.world_kind==5 || o.ext.world_kind==6 || o.ext.world_kind==7) {
                    if(o.primary_uri.empty())throw std::runtime_error("Missing SDK world asset URI");
                    configs::checked::validate_path(o.primary_uri);
                    if(o.ext.world_kind==6 && session==r.session) {
                        auto old=objects.find(v.id);
                        if(old!=objects.end() && old->second.ext.world_kind==6 && old->second.primary_uri==o.primary_uri && old->second.decal_material)
                            o.decal_material=old->second.decal_material;
                    }
                }
                o.value.path=nullptr;
                for(auto const& row:o.transform.elements)for(float f:row)if(!std::isfinite(f))throw std::runtime_error("SDK transform overflow");
                if(!next->objects.emplace(v.id,std::move(o)).second)throw std::runtime_error("Duplicate SDK scene object ID");
                next->order.push_back(v.id);
            }
            if(pending_terrain_session==r.session && !pending_terrain.empty()) {
                next->has_world_tools=true;
                for(auto& pt:pending_terrain) {
                    terrain_cell_entry ce;ce.kx=pt.kx;ce.ky=pt.ky;ce.virtual_path=std::move(pt.virtual_path);ce.bytes=std::move(pt.bytes);
                    ce.selected=(pt.flags&2u)!=0;
                    if(ce.selected)next->terrain_selected=true;
                    terrain_cell_entry const* old_ce=nullptr;
                    if(session==r.session) {
                        for(auto const& existing:terrain_cells)if(existing.kx==ce.kx && existing.ky==ce.ky){old_ce=&existing;break;}
                    }
                    if(old_ce && old_ce->bytes==ce.bytes && old_ce->data && old_ce->collision && old_ce->render_model) {
                        ce.data=old_ce->data;ce.collision=old_ce->collision;ce.render_model=old_ce->render_model;
                        ce.world_min=old_ce->world_min;ce.world_max=old_ce->world_max;ce.has_bounds=old_ce->has_bounds;
                    } else {
                        ce.data=render::vulkan::load_terrain_cell(const_buffer(ce.bytes.data(),ce.bytes.size()));
                        if(old_ce && old_ce->data && old_ce->collision
                            && ce.data->row_size==old_ce->data->row_size
                            && ce.data->physical_size==old_ce->data->physical_size
                            && ce.data->samples.size()==old_ce->data->samples.size()
                            && std::memcmp(ce.data->samples.data(),old_ce->data->samples.data(),ce.data->samples.size()*sizeof(render::vulkan::terrain_sample))==0) {
                            ce.collision=old_ce->collision;
                        } else {
                            ce.collision=std::make_shared<render::vulkan::terrain_collision_geometry>(allocator,ce.data);
                        }
                        if(ce.collision && ce.data && !ce.collision->vertices().empty()) {
                            auto const& verts=ce.collision->vertices();
                            auto const& xf=ce.data->transform;
                            math::float3 bmin=xf.transform_position(verts.front()),bmax=bmin;
                            for(auto const& v:verts) {
                                auto p=xf.transform_position(v);
                                for(unsigned c=0;c<3;++c){bmin[c]=std::min(bmin[c],p[c]);bmax[c]=std::max(bmax[c],p[c]);}
                            }
                            ce.world_min=bmin;ce.world_max=bmax;ce.has_bounds=true;
                        }
                        if(old_ce && old_ce->data && old_ce->render_model
                            && ce.data->row_size==old_ce->data->row_size
                            && ce.data->physical_size==old_ce->data->physical_size
                            && ce.data->textures==old_ce->data->textures) {
                            ce.render_model=old_ce->render_model;
                        }
                    }
                    next->terrain_cells.push_back(std::move(ce));
                }
            }
            pending_extensions.clear();pending_terrain.clear();
            pending_extensions_session=pending_terrain_session=0;
            bool reuse=r.session && r.session==session && live_scene && next->objects.size()==objects.size()
                && !next->has_world_tools && !live_has_world_tools && next->terrain_cells.empty() && terrain_cells.empty();
            if(reuse)for(auto& [id,o]:next->objects) {
                auto found=objects.find(id);if(found==objects.end() || found->second.path!=o.path || found->second.value.kind!=o.value.kind){reuse=false;break;}
                auto const& old=found->second;o.selected=old.selected;
                if(o.value.kind==2 && (((o.value.flags^old.value.flags)&1) || std::memcmp(o.value.position,old.value.position,9*sizeof(float)) || o.value.color!=old.value.color || o.value.range!=old.value.range || o.value.intensity!=old.value.intensity)){reuse=false;break;}
            }
            if(reuse){next->incremental=true;next->scene=live_scene;staged=next;r.query=++serial;return 0;}
            if(r.session) {
                ++statistics.scene_builds;
                ++next->pending;
                bool any_particle_in_scene=false;
                for(auto const& [id,o]:next->objects)if(o.ext.world_kind==5){any_particle_in_scene=true;break;}
                render::vulkan::scene_configuration scene_cfg;
                scene_cfg.features.m_create_terrain=!next->terrain_cells.empty();
                scene_cfg.features.m_create_particle_world=any_particle_in_scene;
                scene_cfg.terrain_memory=scene_cfg.features.m_create_terrain
                    ? render::terrain_memory_profile::extended_384_mib
                    : render::terrain_memory_profile::standard_256_mib;
                scene_cfg.particle_fixed_timestep=any_particle_in_scene ? (1.f/60.f) : 0.f;
                auto scene_user=std::make_shared<resources::user_data_variant>();
                scene_user->set(scene_cfg);
                resources::user_data_variant const* scene_users[]={scene_user.get()};
                resources::creation_request create("sdk_authored_scene",const_buffer("",1),resources::scene_class);
                resources::query_create_resources(&create,1,[next,scene_user,any_particle_in_scene,&allocator,&renderer=level.editor_scene_renderer()](resources::queries_result& q) {
                    if(next->cancelled){--next->pending;return;}
                    if(!q.is_successful()){next->failed=true;--next->pending;return;}
                    next->scene=dynamic_cast<render::base_scene*>(q[0].get_unmanaged_resource().c_ptr());
                    if(!next->scene){next->failed=true;--next->pending;return;}
                    for(size_t ci=0;ci<next->terrain_cells.size();++ci) {
                        auto& ce=next->terrain_cells[ci];
                        if(ce.render_model)continue;
                        ++next->pending;
                        auto proj=std::make_shared<std::string>("resources/projects/alex");
                        next->retained_strings.push_back(proj);
                        auto cook_user=std::make_shared<resources::user_data_variant>();
                        cook_user->set(render::terrain_cook_user_data{next->scene,proj->c_str(),render::terrain_memory_profile::extended_384_mib});
                        resources::user_data_variant const* t_users[]={cook_user.get()};
                        resources::creation_request t_req(ce.virtual_path.c_str(),const_buffer(ce.bytes.data(),ce.bytes.size()),resources::terrain_instance_class);
                        resources::query_create_resources(&t_req,1,[next,ci,cook_user](resources::queries_result& tq) {
                            --next->pending;if(next->cancelled)return;
                            if(!tq.is_successful()){next->failed=true;return;}
                            auto* inst=dynamic_cast<render::vulkan::terrain_instance_resource*>(tq[0].get_unmanaged_resource().c_ptr());
                            if(!inst){next->failed=true;return;}
                            next->terrain_cells[ci].render_model=inst;
                        },&allocator,t_users,nullptr,assert_on_fail_false);
                    }
                    if(any_particle_in_scene) {
                        auto pworld=renderer.particle_world_handle(next->scene);
                        if(!pworld){next->failed=true;--next->pending;return;}
                        for(auto& [id,o]:next->objects)if(o.ext.world_kind==5) {
                            ++next->pending;
                            auto puser=std::make_shared<resources::user_data_variant>();puser->set(pworld);
                            resources::query_resource(o.primary_uri.c_str(),resources::particle_system_instance_class,[next,id,puser](resources::queries_result& pq) {
                                --next->pending;if(next->cancelled)return;
                                if(!pq.is_successful()){next->failed=true;return;}
                                particle::particle_system_instance_ptr inst=pq[0].get_unmanaged_resource();
                                if(!inst){next->failed=true;return;}
                                next->objects.at(id).particle=inst;
                            },&allocator,puser.get(),nullptr,assert_on_fail_false);
                        }
                    }
                    --next->pending;
                },&allocator,scene_users,nullptr,assert_on_fail_false);
                std::uint64_t sky_owner_id=0;
                auto cook_sky=[&](bool authored) {
                    ++next->pending;
                    next->authored_sky=authored;
                    auto tga=make_sky_cube_tga();
                    render::vulkan::texture_configuration tc{};tc.srgb=true;tc.source_tga=true;tc.source_cube=true;tc.source_dimension=3;
                    auto t_user=std::make_shared<resources::user_data_variant>();t_user->set(tc);
                    resources::user_data_variant const* t_users[]={t_user.get()};
                    resources::creation_request t_req("sdk_sky_cube",const_buffer(tga->data(),tga->size()),resources::texture_class);
                    resources::query_create_resources(&t_req,1,[next,tga,t_user,&allocator](resources::queries_result& tq) {
                        if(next->cancelled){--next->pending;return;}
                        if(!tq.is_successful()){next->failed=true;--next->pending;return;}
                        auto tex=tq[0].get_unmanaged_resource();next->sky_cube_texture=tex;
                        render::vulkan::sky_material_configuration sc{};sc.texture=tex;sc.color={1.f,1.f,1.f};sc.intensity=1.f;sc.rotation=0.f;
                        auto s_user=std::make_shared<resources::user_data_variant>();s_user->set(sc);
                        resources::user_data_variant const* s_users[]={s_user.get()};
                        resources::creation_request s_req("sdk_sky_material",const_buffer("",1),resources::material_class);
                        ++next->pending;
                        resources::query_create_resources(&s_req,1,[next,s_user](resources::queries_result& sq) {
                            --next->pending;if(next->cancelled)return;
                            if(!sq.is_successful()){next->failed=true;return;}
                            next->sky_material=sq[0].get_unmanaged_resource();
                            if(!next->sky_material)next->failed=true;
                        },&allocator,s_users,nullptr,assert_on_fail_false);
                        --next->pending;
                    },&allocator,t_users,nullptr,assert_on_fail_false);
                };
                for(auto& [id,o]:next->objects) {
                    if(o.value.kind==1) {
                        ++statistics.model_cooks;++next->pending;
                        auto finish=[next,id](resources::queries_result& q) {
                            --next->pending;if(next->cancelled)return;
                            if(!q.is_successful()){next->failed=true;return;}
                            auto model=dynamic_cast<render::vulkan::static_model_resource*>(q[0].get_unmanaged_resource().c_ptr());
                            if(!model || !model->data || model->data->surfaces.empty()){next->failed=true;return;}
                            next->objects.at(id).model=model;
                        };
                        if(o.model) {
                            resources::user_data_variant clone;clone.set(render::vulkan::static_model_clone_request{o.model});
                            resources::user_data_variant const* user=&clone;
                            resources::creation_request create_inst("sdk_scene_instance",const_buffer("",1),resources::static_render_model_instance_class);
                            resources::query_create_resources(&create_inst,1,finish,&allocator,&user,nullptr,assert_on_fail_false);
                        } else resources::query_resource(o.path.c_str(),resources::static_render_model_instance_class,finish,&allocator,nullptr,nullptr,assert_on_fail_false);
                    } else if(o.ext.world_kind==2) {
                        if(!o.model) {
                            ++statistics.model_cooks;++next->pending;
                            resources::query_resource(o.primary_uri.c_str(),resources::skeleton_render_model_instance_class,[next,id](resources::queries_result& q) {
                                --next->pending;if(next->cancelled)return;
                                if(!q.is_successful()){next->failed=true;return;}
                                auto model=dynamic_cast<render::vulkan::static_model_resource*>(q[0].get_unmanaged_resource().c_ptr());
                                if(!model || !model->data || model->data->surfaces.empty() || model->data->inverse_bind_pose.empty()){next->failed=true;return;}
                                next->objects.at(id).model=model;
                            },&allocator,nullptr,nullptr,assert_on_fail_false);
                        }
                    } else if(o.ext.world_kind==4) {
                        if(!o.tree) {
                            ++next->pending;
                            resources::query_resource(o.primary_uri.c_str(),resources::speedtree_instance_class,[next,id](resources::queries_result& q) {
                                --next->pending;if(next->cancelled)return;
                                if(!q.is_successful()){next->failed=true;return;}
                                auto tree=dynamic_cast<render::speedtree_instance*>(q[0].get_unmanaged_resource().c_ptr());
                                if(!tree){next->failed=true;return;}
                                next->objects.at(id).tree=tree;
                            },&allocator,nullptr,nullptr,assert_on_fail_false);
                            auto query_tree_mat=[&](std::string const& uri,int slot) {
                                if(uri.empty())return;
                                configs::checked::validate_path(uri);++next->pending;
                                resources::query_resource(uri.c_str(),resources::material_class,[next,id,slot](resources::queries_result& q) {
                                    --next->pending;if(next->cancelled)return;
                                    if(!q.is_successful()){next->failed=true;return;}
                                    auto mat=q[0].get_unmanaged_resource();
                                    auto& target_obj=next->objects.at(id);
                                    if(slot==0)target_obj.tree_bark_mat=mat;
                                    else if(slot==1)target_obj.tree_frond_mat=mat;
                                    else target_obj.tree_leaf_mat=mat;
                                },&allocator,nullptr,nullptr,assert_on_fail_false);
                            };
                            query_tree_mat(o.secondary_uri,0);
                            query_tree_mat(o.tertiary_uri,1);
                            query_tree_mat(o.quaternary_uri,2);
                        }
                    } else if(o.ext.world_kind==6) {
                        if(!o.decal_material) {
                            ++next->pending;
                            resources::query_resource(o.primary_uri.c_str(),resources::material_class,[next,id](resources::queries_result& q) {
                                --next->pending;if(next->cancelled)return;
                                if(!q.is_successful()){next->failed=true;return;}
                                auto* dm=dynamic_cast<render::vulkan::decal_material_resource*>(q[0].get_unmanaged_resource().c_ptr());
                                if(!dm){next->failed=true;return;}
                                next->objects.at(id).decal_material=dm;
                            },&allocator,nullptr,nullptr,assert_on_fail_false);
                        }
                    } else if(o.ext.world_kind==7 && !sky_owner_id) {
                        sky_owner_id=id;
                        next->authored_sky=true;
                        if(session==r.session && live_authored_sky && sky_material) {
                            next->sky_material=sky_material;
                        } else {
                            cook_sky(true);
                        }
                    }
                }
                if(!sky_owner_id && interactive_default_sky) {
                    if(default_sky_material) {
                        next->sky_material=default_sky_material;
                        next->authored_sky=false;
                    } else {
                        cook_sky(false);
                    }
                }
            }
            staged=next;r.query=++serial;return 0;
        }
        if(r.operation>=1 && r.operation<=3) {
            if(!staged || r.query!=serial)return -1;
            if(r.operation==1) {resources::dispatch_callbacks();return staged->pending?0:staged->failed?-1:1;}
            if(r.operation==3){staged->cancelled=true;staged.reset();return 0;}
            if(staged->pending || staged->failed)return -1;
            auto& renderer=level.editor_scene_renderer();
            if(staged->incremental) {
                std::vector<render::model_scene_edit> edits;edits.reserve(staged->objects.size());
                for(auto id:staged->order) {auto& o=staged->objects.at(id);if(o.model) {o.selected=(o.value.flags&2)!=0;edits.push_back({o.model,o.transform,(o.value.flags&1)!=0,o.selected});}}
                renderer.apply_model_edits(live_scene,std::move(edits));++statistics.delta_commits;
            } else {
                for(auto const& ce:staged->terrain_cells)if(ce.render_model) {
                    if(auto* inst=dynamic_cast<render::vulkan::terrain_instance_resource*>(ce.render_model.c_ptr());inst && ce.data && inst->cell()!=ce.data) {
                        auto prev_cell=inst->cell();
                        bool samples_differ=!prev_cell || prev_cell->samples.size()!=ce.data->samples.size()
                            || std::memcmp(prev_cell->samples.data(),ce.data->samples.data(),ce.data->samples.size()*sizeof(render::vulkan::terrain_sample))!=0;
                        if(samples_differ) {
                            render::vulkan::terrain_cell_fragment frag{0,0,ce.data->row_size,ce.data->row_size,ce.data->samples};
                            inst->update_cell({&frag,1});
                        }
                        if(std::memcmp(&inst->cell()->transform,&ce.data->transform,sizeof(math::float4x4))!=0) {
                            inst->update_transform(ce.data->transform);
                        }
                    }
                    renderer.terrain_add_cell(staged->scene,ce.render_model);
                }
                unsigned light_id=1,decal_id=1;
                for(auto id:staged->order) {auto const& o=staged->objects.at(id);if(o.value.flags&1) {
                    if(o.model)renderer.add_model(staged->scene,o.model,o.transform);
                    else if(o.value.kind==2) {
                        render::light_props p={};
                        unsigned lt=o.ext.world_kind==3?o.ext.light_type:0u;
                        p.type=lt==4?render::light_type_parallel:(lt==1||lt==6)?render::light_type_spot:render::light_type_point;
                        p.transform=o.transform;p.color=o.value.color;p.range=std::max(0.05f,o.value.range);
                        p.intensity=std::clamp(o.value.intensity,0.f,16.f);
                        p.diffuse_influence_factor=std::clamp(o.ext.world_kind==3?o.ext.diffuse_factor:1.f,0.f,1.f);
                        p.specular_influence_factor=std::clamp(o.ext.world_kind==3?o.ext.specular_factor:1.f,0.f,1.f);
                        p.attenuation_power=std::max(0.1f,o.ext.world_kind==3?o.ext.attenuation_power:2.f);
                        p.spot_umbra_angle=std::clamp(o.ext.world_kind==3?o.ext.spot_umbra_angle:0.39f,0.01f,3.0f);
                        p.spot_penumbra_angle=std::clamp(std::max(p.spot_umbra_angle+0.02f,o.ext.world_kind==3?o.ext.spot_penumbra_angle:0.78f),0.03f,3.1f);
                        p.spot_falloff=std::max(0.05f,o.ext.world_kind==3?o.ext.spot_falloff:1.f);
                        p.does_cast_shadows=false;p.shadower=false;p.use_with_lpv=false;
                        renderer.add_light(staged->scene,light_id++,p);
                    } else if(o.ext.world_kind==4 && o.tree) {
                        renderer.add_speedtree_instance(staged->scene,o.tree,o.transform,false);
                        renderer.set_speedtree_instance_visible(o.tree,(o.value.flags&1)!=0);
                        auto* ti=static_cast<render::vulkan::tree_instance*>(o.tree.c_ptr());
                        auto* tm=static_cast<render::vulkan::static_model_resource*>(ti->model.c_ptr());
                        for(auto const& s:tm->data->surfaces) {
                            if(s.name=="branch" && o.tree_bark_mat)renderer.set_speedtree_instance_material(o.tree,fs_new::virtual_path_string("branch"),o.tree_bark_mat);
                            else if(s.name=="frond" && o.tree_frond_mat)renderer.set_speedtree_instance_material(o.tree,fs_new::virtual_path_string("frond"),o.tree_frond_mat);
                            else if((s.name=="leafmesh" || s.name=="leafcard") && o.tree_leaf_mat)renderer.set_speedtree_instance_material(o.tree,fs_new::virtual_path_string(s.name.c_str()),o.tree_leaf_mat);
                        }
                    } else if(o.ext.world_kind==5 && o.particle) {
                        renderer.play_particle_system(staged->scene,o.particle,o.transform);
                        bool vis=(o.value.flags&1)!=0 && (o.ext.flags&1)!=0;
                        bool paused=(o.ext.flags&4)!=0 || (o.ext.flags&2)==0;
                        renderer.update_particle_system_instance(staged->scene,o.particle,o.transform,vis,paused);
                    } else if(o.ext.world_kind==6 && o.decal_material) {
                        render::decal_properties dp;
                        dp.transform=orthonormalize_rigid(o.transform);
                        dp.material=o.decal_material;
                        dp.width_height_far_distance={std::clamp(o.ext.decal_width,0.001f,128.f),std::clamp(o.ext.decal_height,0.001f,128.f),std::clamp(o.ext.decal_depth,0.001f,128.f)};
                        dp.alpha_angle=-1.f;dp.clip_angle=-1.f;
                        dp.projection_on_terrain_geometry=(o.ext.flags&1)!=0;
                        dp.projection_on_static_geometry=(o.ext.flags&2)!=0;
                        if(!dp.projection_on_terrain_geometry && !dp.projection_on_static_geometry)dp.projection_on_terrain_geometry=true;
                        dp.projection_on_speedtree_geometry=dp.projection_on_skeleton_geometry=dp.projection_on_particle_geometry=false;
                        renderer.add_decal(staged->scene,decal_id++,dp);
                    }
                }}
                if(staged->sky_material && staged->scene)renderer.set_sky_material(staged->scene,staged->sky_material);
            }
            bool new_session=session!=staged->session;
            level.project_scene(staged->scene);live_scene=staged->scene;session=staged->session;objects=std::move(staged->objects);order=std::move(staged->order);
            if(!staged->incremental) {
                terrain_cells=std::move(staged->terrain_cells);
                sky_material=staged->sky_material;
                live_authored_sky=staged->authored_sky;
                if(!staged->authored_sky && staged->sky_material)default_sky_material=staged->sky_material;
                live_has_world_tools=staged->has_world_tools;
                live_has_trees_particles_decals=staged->has_trees_particles_decals;
                live_has_decals=staged->has_decals;
                terrain_selected=staged->terrain_selected;
            }
            if(new_session || !gizmo_target_valid(gizmo)){gizmo={};gizmo_triangles.clear();}
            if(!session)light_scene=nullptr;
            staged.reset();return 0;
        }
        if(!session || r.session!=session)return -1;
        if(r.operation==52) {
            if(!r.result || r.data || r.query || r.count)return -1;
            sdk_world_scene_status st{1,sizeof(sdk_world_scene_status)};
            st.terrain_cells=std::uint32_t(terrain_cells.size());
            for(auto const& ce:terrain_cells)if(ce.collision)st.terrain_collision_triangles+=std::uint32_t(ce.collision->indices().size()/3);
            for(auto const& [id,o]:objects) {
                if(!(o.value.flags&1))continue;
                if(o.value.kind==1)++st.solid_visuals;
                else if(o.ext.world_kind==2 && o.model)++st.skinned_visuals;
                else if(o.value.kind==2)++st.lights;
                else if(o.ext.world_kind==4 && o.tree)++st.speedtrees;
                else if(o.ext.world_kind==5 && o.particle)++st.particles;
                else if(o.ext.world_kind==6 && o.decal_material)++st.decals;
                else if(o.ext.world_kind==8)++st.misc_locators;
            }
            st.sky_active=live_authored_sky?1u:0u;
            *static_cast<sdk_world_scene_status*>(r.result)=st;
            return 0;
        }
        if(r.operation>=30 && r.operation<=38)return runtime.request(r,level,engine,allocator,objects,terrain_cells,sky_material);
        if(r.operation==16){if(!r.result || r.data || r.query || r.count)return -1;auto copy=statistics;copy.objects=objects.size();*static_cast<sdk_scene_statistics*>(r.result)=copy;return 0;}
        if(r.operation==17){if(r.data || r.query || r.count)return -1;level.editor_scene_renderer().reload_modified_textures();return 0;}
        if(r.operation==18){if(!r.result || r.data || r.query || r.count)return -1;*static_cast<render::vulkan::texture_reload_statistics*>(r.result)=level.registry()->texture_reload_status();return 0;}
        if(r.operation==14) {
            if(!r.data || !r.result || r.count || r.query)return -1;auto input=*static_cast<sdk_scene_transform_frame const*>(r.data);
            if(input.version!=1 || input.size!=sizeof(input))return -1;math::float4x4 frame;std::memcpy(&frame,input.frame,sizeof(frame));
            editor_base::sdk_transform_in_frame(allocator,level.editor_scene(),level.editor_view(),level.editor_output(),input.object,frame);
            *static_cast<sdk_scene_transform*>(r.result)=input.object;return 0;
        }
        if(r.operation==15) {
            if(!r.data || !r.result || r.count || r.query)return -1;auto const& input=*static_cast<sdk_gizmo_drag const*>(r.data);
            if(!input.origin.mode || !gizmo_target_valid(input.origin))return -1;
            auto delta=editor_base::sdk_gizmo_drag_extended(input);*static_cast<sdk_gizmo_delta_extended*>(r.result)=delta;return 0;
        }
        if(r.operation==13) {
            if(!r.data || r.count || r.query)return -1;
            auto s=*static_cast<render::editor_selection_settings const*>(r.data);
            if(s.version!=1 || s.size!=sizeof(s) || s.enabled>1 || s.reserved || !std::isfinite(s.rate) || s.rate<0 || s.rate>10 || !std::isfinite(s.time) || s.time<0 || s.time>1e6f || !std::isfinite(s.width) || s.width<1 || s.width>8)return -1;
            level.editor_scene_renderer().set_editor_selection(level.editor_view(),s);return 0;
        }
        if(r.operation>=10 && r.operation<=12) {
            if(r.count || r.query || !r.data)return -1;
            auto available=[&](sdk_gizmo_settings const& settings){return gizmo_target_valid(settings);};
            if(r.operation==10) {
                auto copy=*static_cast<sdk_gizmo_settings const*>(r.data);
                if((copy.version!=1 && copy.version!=2) || copy.size!=sizeof(copy) || copy.active_axis>10 || (copy.version==1 && copy.active_axis>4) || (copy.active_axis==4 && copy.mode!=3)
                    || (copy.active_axis>=5 && copy.active_axis<=8 && copy.mode!=1 && copy.mode!=3) || (copy.active_axis>=9 && copy.mode!=2) || (!copy.mode && copy.active_axis))return -1;
                if(!available(copy))return -1;
                auto previous=gizmo;previous.active_axis=copy.active_axis;
                if(std::memcmp(&previous,&copy,sizeof(copy))) {
                    std::vector<editor_base::sdk_gizmo_triangle> next;editor_base::sdk_gizmo_build(copy,next);gizmo_triangles=std::move(next);
                }
                gizmo=copy;debug_allocator=&allocator;return 0;
            }
            if(!r.result)return -1;
            if(r.operation==11) {
                // Even an empty/disabled gizmo validates the ray and returns an explicit miss.
                auto settings=gizmo;if(!settings.version){settings={1,sizeof(settings)};}
                auto hit=editor_base::sdk_gizmo_pick(settings,gizmo_triangles,*static_cast<sdk_scene_ray const*>(r.data));
                *static_cast<sdk_gizmo_hit*>(r.result)=hit;return 0;
            }
            auto const& input=*static_cast<sdk_gizmo_drag const*>(r.data);
            if(!input.origin.mode || !available(input.origin))return -1;
            auto delta=editor_base::sdk_gizmo_drag(input);*static_cast<sdk_gizmo_delta*>(r.result)=delta;return 0;
        }
        if(r.operation==7) {
            if(!r.data)return -1;
            auto const v=*static_cast<sdk_viewport_settings const*>(r.data);
            if(v.version!=1 || v.size!=sizeof(v) || (v.flags&~2047u) || v.mode>20
                || !std::isfinite(v.grid_step) || v.grid_step<.1f || v.grid_step>10000 || !v.grid_dimension || v.grid_dimension>10000
                || v.grid_step*v.grid_dimension>1e6f || !std::isfinite(v.selection_rate) || v.selection_rate<0 || v.selection_rate>10)return -1;
            viewport=v;viewport_enabled=true;debug_allocator=&allocator;
            bool fxaa=(v.flags&512)!=0 && !live_has_trees_particles_decals && !(v.flags&(128|256));
            level.project_view_options(static_cast<render::scene_view_mode>(v.mode),(v.flags&16)!=0,(v.flags&1)!=0,(v.flags&128)!=0,(v.flags&256)!=0,fxaa);return 0;
        }
        if(r.operation==8) {
            if(!r.result)return -1;
            auto* out=static_cast<sdk_viewport_snapshot*>(r.result);if(out->version!=1 || out->size!=sizeof(*out))return -1;
            auto* view=static_cast<render::vulkan::view_resource*>(level.editor_view().c_ptr());auto matrices=view->camera();
            sdk_viewport_snapshot copy{1,sizeof(copy),unsigned(view->mode()),viewport.flags};
            std::memcpy(copy.view,&matrices[0],sizeof(matrices[0]));std::memcpy(copy.culling_view,&matrices[2],sizeof(matrices[2]));
            *out=copy;return 0;
        }
        if(r.operation==9) {
            if(!r.result)return -1;
            sdk_selection_bounds result{};
            math::float3 minimum(std::numeric_limits<float>::max(),std::numeric_limits<float>::max(),std::numeric_limits<float>::max()),maximum=-minimum;
            for(auto id:order) {auto const& o=objects.at(id);if(!(o.value.flags&2))continue;
                auto corners=bounds(o);++result.count;
                for(auto const& p:corners)for(unsigned i=0;i<3;++i){minimum[i]=std::min(minimum[i],p[i]);maximum[i]=std::max(maximum[i],p[i]);}
            }
            if(terrain_selected) {
                for(auto const& ce:terrain_cells)if(ce.has_bounds) {
                    ++result.count;
                    for(unsigned i=0;i<3;++i){minimum[i]=std::min(minimum[i],ce.world_min[i]);maximum[i]=std::max(maximum[i],ce.world_max[i]);}
                }
            }
            if(result.count)for(unsigned i=0;i<3;++i){result.minimum[i]=minimum[i];result.maximum[i]=maximum[i];}
            *static_cast<sdk_selection_bounds*>(r.result)=result;return 0;
        }
        if(r.operation==6) {
            if(!r.data)return -1;
            auto* values=static_cast<float const*>(r.data);
            for(unsigned i=0;i<16;++i)if(!std::isfinite(values[i]))return -1;
            math::float4x4 projection;std::memcpy(&projection,values,sizeof(projection));
            if(projection.e00<=0 || projection.e11<=0 || projection.e23!=1 || projection.e33!=0)return -1;
            level.project_projection(projection);return 0;
        }
        if(r.operation==4) {
            if(!r.data || !r.result)return -1;
            auto const& ray=*static_cast<sdk_scene_ray*>(r.data);
            if(ray.snap>3 || ray.reserved)return -1;
            auto origin=vec(ray.origin),direction=vec(ray.direction);
            for(auto p:{ray.origin,ray.direction})for(unsigned i=0;i<3;++i)if(!std::isfinite(p[i]))return -1;
            if(std::abs(direction.length()-1)>1e-3f)return -1;
            sdk_scene_hit hit{};float nearest=std::numeric_limits<float>::max();math::float3 point(0,0,0);
            auto test_model=[&](std::uint64_t hit_id,math::float4x4 const& xf,render::vulkan::static_model_resource const* model) {
                if(!model || !model->data)return;
                for(auto const& surface:model->data->surfaces) {
                    auto const& mesh=*surface.mesh;
                    auto vertex=[&](unsigned i){return xf.transform_position(vec(mesh.vertices.at(i).position));};
                    for(size_t i=0;i+2<mesh.indices.size();i+=3) {
                        auto a=vertex(mesh.indices[i]),b=vertex(mesh.indices[i+1]),c=vertex(mesh.indices[i+2]);float t;
                        if(!triangle(origin,direction,a,b,c,t) || t>=nearest)continue;
                        nearest=t;hit.id=hit_id;point=origin+direction*t;
                        if(ray.snap==1) {auto p=point;point=a;if((b-p).squared_length()<(point-p).squared_length())point=b;if((c-p).squared_length()<(point-p).squared_length())point=c;}
                        if(ray.snap==2) {auto p=point,x=edge(p,a,b),y=edge(p,b,c),z=edge(p,c,a);point=x;if((y-p).squared_length()<(point-p).squared_length())point=y;if((z-p).squared_length()<(point-p).squared_length())point=z;}
                    }
                }
            };
            for(auto id:order) {auto const& o=objects.at(id);
                if((o.value.flags&5)!=5 || ray.exclude==id || (ray.exclude==~std::uint64_t(0) && (o.value.flags&2)))continue;
                if(o.model) {
                    test_model(id,o.transform,static_cast<render::vulkan::static_model_resource const*>(o.model.c_ptr()));
                } else if(o.tree) {
                    auto* ti=static_cast<render::vulkan::tree_instance const*>(o.tree.c_ptr());
                    test_model(id,o.transform,ti?static_cast<render::vulkan::static_model_resource const*>(ti->model.c_ptr()):nullptr);
                } else {
                    auto delta=o.transform.c.xyz()-origin;float t=math::dot_product(delta,direction);
                    if(t>=0 && t<nearest && (origin+direction*t-o.transform.c.xyz()).length()<.4f) {nearest=t;hit.id=id;point=o.transform.c.xyz();}
                }
            }
            if(ray.exclude!=terrain_hit_id && !(ray.exclude==~std::uint64_t(0) && terrain_selected)) {
                for(auto const& ce:terrain_cells) {
                    if(!ce.collision || !ce.data)continue;
                    auto const& verts=ce.collision->vertices();
                    auto const& idxs=ce.collision->indices();
                    auto const& xform=ce.data->transform;
                    for(size_t i=0;i+2<idxs.size();i+=3) {
                        auto a=xform.transform_position(verts[idxs[i]]),b=xform.transform_position(verts[idxs[i+1]]),c=xform.transform_position(verts[idxs[i+2]]);float t;
                        if(!triangle(origin,direction,a,b,c,t) || t>=nearest)continue;
                        nearest=t;hit.id=terrain_hit_id;point=origin+direction*t;
                        if(ray.snap==1) {auto p=point;point=a;if((b-p).squared_length()<(point-p).squared_length())point=b;if((c-p).squared_length()<(point-p).squared_length())point=c;}
                        if(ray.snap==2) {auto p=point,x=edge(p,a,b),y=edge(p,b,c),z=edge(p,c,a);point=x;if((y-p).squared_length()<(point-p).squared_length())point=y;if((z-p).squared_length()<(point-p).squared_length())point=z;}
                    }
                }
            }
            hit.point[0]=point.x;hit.point[1]=point.y;hit.point[2]=point.z;hit.distance=hit.id?nearest:0;
            *static_cast<sdk_scene_hit*>(r.result)=hit;return 0;
        }
        if(r.operation==5) {
            if(!r.data || !r.result)return -1;
            auto copy=*static_cast<sdk_scene_transform*>(r.data);
            for(auto p:{copy.position,copy.rotation,copy.scale,copy.pivot})for(unsigned i=0;i<3;++i)if(!std::isfinite(p[i]))return -1;
            for(float f:copy.scale)if(std::abs(f)<1e-5f)return -1;
            editor_base::sdk_transform(allocator,level.editor_scene(),level.editor_view(),level.editor_output(),copy);
            *static_cast<sdk_scene_transform*>(r.result)=copy;return 0;
        }
        return -1;
    }
    void draw(render::control_level::control_world& level) {
        outputs.tick(level,session!=0);if(runtime.running())return;
        if(!session)return;
        auto& debug=level.editor_debug_renderer();auto target=level.editor_scene();
        auto& renderer=level.editor_scene_renderer();
        if(live_has_trees_particles_decals) {
            render::postprocess_settings pp{};
            pp.enabled=!viewport_enabled || (viewport.flags&1)!=0;
            pp.ssao_enabled=pp.enabled && viewport_enabled && (viewport.flags&128)!=0;
            pp.distortion_enabled=pp.enabled && viewport_enabled && (viewport.flags&256)!=0;
            pp.msaa_samples=1;pp.taa_enabled=false;pp.fxaa_enabled=false;
            renderer.set_postprocess_settings(level.editor_view(),pp);
            if(live_has_decals) {
                auto* view=static_cast<render::vulkan::view_resource*>(level.editor_view().c_ptr());
                if(view && view->mode()!=render::lit_view_mode) {
                    renderer.set_view_mode(level.editor_view(),render::lit_view_mode);
                    level.project_view_options(render::lit_view_mode,(viewport.flags&16)!=0,pp.enabled,pp.ssao_enabled,pp.distortion_enabled,false);
                }
            } else {
                auto* view=static_cast<render::vulkan::view_resource*>(level.editor_view().c_ptr());
                level.project_view_options(view?view->mode():render::lit_view_mode,(viewport.flags&16)!=0,pp.enabled,pp.ssao_enabled,pp.distortion_enabled,false);
            }
        }
        for(auto& [id,o]:objects) {
            if(o.model && (o.value.flags&1)) {
                bool selected=(o.value.flags&2)!=0;if(selected!=o.selected){level.project_selection(o.model,selected);o.selected=selected;}
            } else if(o.tree && (o.value.flags&1)) {
                auto* ti=static_cast<render::vulkan::tree_instance*>(o.tree.c_ptr());
                if(ti && ti->model) {
                    bool selected=(o.value.flags&2)!=0;
                    if(selected!=o.selected){level.project_selection(ti->model,selected);o.selected=selected;}
                }
            }
            if(o.ext.world_kind==5 && o.particle) {
                bool vis=(o.value.flags&1)!=0 && (o.ext.flags&1)!=0;
                bool paused=(o.ext.flags&4)!=0 || (o.ext.flags&2)==0;
                renderer.update_particle_system_instance(target,o.particle,o.transform,vis,paused);
            }
        }
        if(viewport_enabled) {
            editor_lights(level);
            if(viewport.flags&2) {
                render::debug_vertices_type vertices(*debug_allocator);render::debug_indices_type indices(*debug_allocator);
                float half=viewport.grid_dimension*viewport.grid_step*.5f;
                auto line=[&](math::float3 a,math::float3 b,u32 color){u16 index=u16(vertices.size());vertices.push_back({a,color});vertices.push_back({b,color});indices.push_back(index);indices.push_back(index+1);};
                for(unsigned i=0;i<=viewport.grid_dimension;++i) {
                    float value=-half+i*viewport.grid_step;
                    bool center=std::abs(value)<viewport.grid_step*.01f;
                    u32 base=i%10==0?0xff808a94:0xff4c545c;
                    line({value,0,-half},{value,0,half},center?0xff4080e8:base);
                    line({-half,0,value},{half,0,value},center?0xffd84040:base);
                }
                if(vertices.size()+2<=65536) line({0,0,0},{0,viewport.grid_step*2.f,0},0xff40c850);
                debug.draw_lines(target,vertices,indices,true);
            }
        }
        auto draw_selection_box=[&](std::array<math::float3,8> const& p) {
            unsigned const edges[][2]={{0,1},{0,2},{0,4},{1,3},{1,5},{2,3},{2,6},{3,7},{4,5},{4,6},{5,7},{6,7}};
            auto color=viewport.selection_color;auto alpha=unsigned((color>>24)*std::min(viewport.selection_rate,1.f));color=(color&0xffffff)|(alpha<<24);
            for(auto const& edge:edges)debug.draw_line(target,p[edge[0]],p[edge[1]],math::color(color),false);
        };
        if(terrain_selected && viewport_enabled && (viewport.flags&32) && viewport.selection_rate>0) {
            for(auto const& ce:terrain_cells)if(ce.has_bounds) {
                float max_y=std::max(ce.world_max.y,ce.world_min.y+0.15f);
                std::array<math::float3,8> corners;
                for(unsigned i=0;i<8;++i)corners[i]={i&1?ce.world_max.x:ce.world_min.x,i&2?max_y:ce.world_min.y,i&4?ce.world_max.z:ce.world_min.z};
                draw_selection_box(corners);
            }
        }
        for(auto id:order) {auto const& o=objects.at(id);if(o.value.flags&1) {
            auto rigid=orthonormalize_rigid(o.transform);auto pos=o.transform.c.xyz();
            if(o.value.kind==2) {
                math::color lc(255,215,90);
                unsigned lt=o.ext.world_kind==3?o.ext.light_type:0u;
                if(lt==1 || lt==6)debug.draw_cone(target,rigid,{0.5f,0.5f,std::clamp(o.value.range,0.5f,4.f)},lc);
                else if(lt==2)debug.draw_obb(target,rigid,{0.5f,0.5f,0.5f},lc);
                else if(lt==3)debug.draw_line_capsule(target,rigid,{0.35f,0.75f,0.35f},lc);
                else if(lt==4){debug.draw_arrow(target,pos,pos+rigid.k.xyz()*2.f,lc);debug.draw_sphere(target,pos,0.35f,lc);}
                else debug.draw_sphere(target,pos,std::clamp(o.value.range,0.25f,2.5f),lc);
            } else if(o.ext.world_kind==6) {
                if(o.value.flags&2)debug.draw_obb(target,rigid,{std::clamp(o.ext.decal_width*0.5f,0.05f,128.f),std::clamp(o.ext.decal_height*0.5f,0.05f,128.f),std::clamp(o.ext.decal_depth*0.5f,0.05f,128.f)},math::color(90,220,255));
            } else if(o.ext.world_kind==8) {
                switch(o.ext.locator_shape) {
                    case 1:debug.draw_frustum(target,0.9f,0.1f,1.5f,1.33f,pos,rigid.k.xyz(),rigid.j.xyz(),math::color(160,210,255));break;
                    case 6:debug.draw_obb(target,o.transform,{0.5f,0.5f,0.5f},math::color(100,220,255));break;
                    case 7:debug.draw_sphere(target,pos,0.75f,math::color(80,255,160));break;
                    case 9:debug.draw_line(target,pos,pos+rigid.i.xyz()*2.f,math::color(255,220,90));debug.draw_cross(target,pos,0.25f,math::color(255,220,90));break;
                    case 10:debug.draw_cross(target,pos,0.3f,math::color(120,200,255));debug.draw_arrow(target,pos,pos+rigid.k.xyz()*1.2f,math::color(120,200,255));break;
                    case 11:debug.draw_cross(target,pos,0.3f,math::color(255,140,220));debug.draw_arrow(target,pos,pos+rigid.k.xyz()*1.2f,math::color(255,140,220));break;
                    default:debug.draw_aabb(target,pos,math::float3(.3f,.3f,.3f),math::color(255,160,40));break;
                }
            } else if(!o.model && !o.tree && !o.particle && o.ext.world_kind!=7 && o.ext.world_kind!=9) {
                debug.draw_aabb(target,pos,math::float3(.3f,.3f,.3f),math::color(255,160,40));
            }
            if(o.value.flags&2) {
                if(viewport_enabled && (viewport.flags&32) && viewport.selection_rate>0) draw_selection_box(bounds(o));
                if(viewport_enabled && !(viewport.flags&64))continue;
                auto p=o.transform.c.xyz();
                debug.draw_line(target,p,p+math::float3(1,0,0),math::color(255,60,60),false);
                debug.draw_line(target,p,p+math::float3(0,1,0),math::color(60,255,60),false);
                debug.draw_line(target,p,p+math::float3(0,0,1),math::color(60,120,255),false);
            }
        }}
        if(gizmo.mode && !gizmo_triangles.empty()) {
            render::debug_vertices_type vertices(*debug_allocator);render::debug_indices_type indices(*debug_allocator);
            u32 const colors[]={0,0xffff4040,0xff40ff40,0xff4080ff,0xffeeeeee,0x604080ff,0x60ff4040,0x6040ff40,0x80eeeeee,0xffeeeeee,0x10ffffff};
            vertices.reserve(gizmo_triangles.size()*3);indices.reserve(gizmo_triangles.size()*3);
            for(auto const& t:gizmo_triangles){u32 color=t.axis==gizmo.active_axis?(t.axis==10?0x40ffff40:0xffffff40):colors[t.axis];
                for(auto const& p:{t.a,t.b,t.c}){indices.push_back(u16(vertices.size()));vertices.push_back({p,color});}
            }
            debug.draw_triangles(target,vertices,indices,false);
        }
    }
    void close(render::control_level::control_world& level,editor::engine& engine) {
        runtime.close(level,engine);outputs.close(level);
        if(staged)staged->cancelled=true;staged.reset();
        objects.clear();order.clear();terrain_cells.clear();sky_material=nullptr;default_sky_material=nullptr;
        pending_extensions.clear();pending_terrain.clear();
        pending_extensions_session=pending_terrain_session=0;
        live_has_world_tools=live_has_trees_particles_decals=live_has_decals=live_authored_sky=terrain_selected=false;
        light_scene=nullptr;live_scene=nullptr;session=0;viewport_enabled=false;gizmo={};gizmo_triangles.clear();
    }
    void drained(){outputs.drained();}
private:
    static std::shared_ptr<std::vector<std::uint8_t>> make_sky_cube_tga() {
        constexpr unsigned h=32,w=6*h;
        auto bytes=std::make_shared<std::vector<std::uint8_t>>(18+w*h*3,0);
        (*bytes)[2]=2;
        (*bytes)[12]=std::uint8_t(w&0xff);(*bytes)[13]=std::uint8_t((w>>8)&0xff);
        (*bytes)[14]=std::uint8_t(h&0xff);(*bytes)[15]=std::uint8_t((h>>8)&0xff);
        (*bytes)[16]=24;(*bytes)[17]=0x20;
        for(unsigned face=0;face<6;++face) for(unsigned py=0;py<h;++py) for(unsigned px=0;px<h;++px) {
            float u=2.f*(float(px)+.5f)/float(h)-1.f,v=2.f*(float(py)+.5f)/float(h)-1.f;
            float dx=0.f,dy=0.f,dz=0.f;
            switch(face) {
                case 0: dx= 1.f; dy=-v;   dz=-u;   break;
                case 1: dx=-1.f; dy=-v;   dz= u;   break;
                case 2: dx= u;   dy= 1.f; dz= v;   break;
                case 3: dx= u;   dy=-1.f; dz=-v;   break;
                case 4: dx= u;   dy=-v;   dz= 1.f; break;
                default:dx=-u;   dy=-v;   dz=-1.f; break;
            }
            float ny=dy/std::sqrt(dx*dx+dy*dy+dz*dz);
            float r=0.f,g=0.f,b=0.f;
            if(ny>=0.f) {
                float t=std::pow(ny,.55f);
                r=190.f+(76.f-190.f)*t;g=208.f+(126.f-208.f)*t;b=226.f+(196.f-226.f)*t;
            } else {
                float t=std::pow(-ny,.65f);
                r=156.f+(72.f-156.f)*t;g=168.f+(78.f-168.f)*t;b=178.f+(86.f-178.f)*t;
            }
            size_t p=18+(size_t(py)*w+(face*h+px))*3;
            (*bytes)[p]=std::uint8_t(b+.5f);(*bytes)[p+1]=std::uint8_t(g+.5f);(*bytes)[p+2]=std::uint8_t(r+.5f);
        }
        return bytes;
    }
    static std::array<math::float3,8> bounds(object const& o) {
        math::float3 minimum(-.3f,-.3f,-.3f),maximum(.3f,.3f,.3f);
        if(o.model) {auto* m=static_cast<render::vulkan::static_model_resource*>(o.model.c_ptr());minimum=vec(m->geometry_minimum.data());maximum=vec(m->geometry_maximum.data());}
        else if(o.tree) {
            auto* ti=static_cast<render::vulkan::tree_instance*>(o.tree.c_ptr());
            auto* m=ti?static_cast<render::vulkan::static_model_resource*>(ti->model.c_ptr()):nullptr;
            if(m){minimum=vec(m->geometry_minimum.data());maximum=vec(m->geometry_maximum.data());}
        } else if(o.ext.world_kind==6) {
            maximum={std::clamp(o.ext.decal_width*0.5f,0.05f,128.f),std::clamp(o.ext.decal_height*0.5f,0.05f,128.f),std::clamp(o.ext.decal_depth*0.5f,0.05f,128.f)};
            minimum=-maximum;
        }
        std::array<math::float3,8> result;
        for(unsigned i=0;i<8;++i)result[i]=o.transform.transform_position({i&1?maximum.x:minimum.x,i&2?maximum.y:minimum.y,i&4?maximum.z:minimum.z});
        return result;
    }
    void editor_lights(render::control_level::control_world& level) {
        auto target=level.editor_scene();auto& scene=level.editor_scene_renderer();
        if(light_scene!=target){light_scene=target;editor_sun=editor_camera_light=false;}
        auto camera=math::invert4x3(level.camera_view());
        auto light=[](render::light_type type,math::float4x4 const& transform) {
            render::light_props value={};value.type=type;value.transform=transform;value.color=0xffffffff;value.intensity=1;value.range=100;
            value.diffuse_influence_factor=value.specular_influence_factor=1;value.attenuation_power=2;return value;
        };
        bool want_shadows=(viewport.flags&1024)!=0;
        if(viewport.flags&8) {
            if(!editor_sun || editor_sun_shadows!=want_shadows){
                if(editor_sun) scene.remove_light(target,0xffff0000);
                auto props=light(render::light_type_parallel,math::create_rotation(math::float3(-math::pi_d2,0,0)));
                props.range=300;props.intensity=2.8f;props.does_cast_shadows=want_shadows;props.sun_shadow_map_size=512;props.num_sun_cascades=4;
                props.shadow_transparency=.1f;props.local_light_z_bias=.000001f;
                scene.add_light(target,0xffff0000,props);editor_sun=true;editor_sun_shadows=want_shadows;
            }
        } else if(editor_sun){scene.remove_light(target,0xffff0000);editor_sun=false;}
        if(viewport.flags&4) {
            auto props=light(render::light_type_point,camera);props.range=10;props.intensity=5;
            if(!editor_camera_light)scene.add_light(target,0xffff0001,props);
            else if(std::memcmp(&camera,&last_light_camera,sizeof(camera)))scene.update_light(target,0xffff0001,props);
            editor_camera_light=true;last_light_camera=camera;
        } else if(editor_camera_light){scene.remove_light(target,0xffff0001);editor_camera_light=false;}
    }
};
}
