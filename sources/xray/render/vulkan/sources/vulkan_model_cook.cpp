#include "pch.h"
#include "vulkan_model_cook.h"
#include "vulkan_diagnostics.h"
#include "vulkan_binary_config.h"
#include "vulkan_original_static_assets.h"
#include "vulkan_model_surface_names.h"
#include <xray/render/vulkan/skeletal_resources.h>
#include <xray/render/core/memory.h>
#include <xray/render/engine/model_format.h>
#include <xray/resources_fs.h>
#include <xray/resources_query_result.h>
#include <xray/resources_pinned_ptr.h>
#include <cmath>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {
void failed(resources::query_result_for_cook& query,std::exception const& e) {
    diagnostic("error","Vulkan model query failed: %s",e.what()); query.finish_query(resources::cook_base::result_error);
}
void contains(model_properties const& parent,std::array<float,3> const& minimum,std::array<float,3> const& maximum) {
    for (size_t axis=0;axis<3;++axis) {
        float epsilon=.001f*std::max({1.f,std::abs(parent.minimum[axis]),std::abs(parent.maximum[axis])});
        if (minimum[axis]<parent.minimum[axis]-epsilon || maximum[axis]>parent.maximum[axis]+epsilon)
            throw std::runtime_error("export_properties bounds do not contain the surface geometry");
    }
}
}
static_model_cook::static_model_cook(bool instance,std::shared_ptr<scene_registry> owner,bool skeletal)
    : translate_query_cook(skeletal ? (instance ? resources::skeleton_render_model_instance_class : resources::skeleton_render_model_class)
        : (instance ? resources::static_render_model_instance_class : resources::static_render_model_class),reuse_false,use_current_thread_id),
      m_owner(std::move(owner)),m_allocator(render::g_allocator),m_instance(instance),m_skeletal(skeletal) {}
void static_model_cook::finish(resources::query_result_for_cook& query,std::shared_ptr<static_model_data const> data) {
    if (!m_owner->accepting()) throw std::runtime_error("Static model cook session is closed");
    if(data->identity.empty()) {
        std::string name=query.get_requested_path();
        if(name.starts_with("resources/models/") && name.ends_with("/render")){auto identified=std::make_shared<static_model_data>(*data);identified->identity=identified->source_path=name;data=identified;}
    }
    if (m_instance) {
        auto* result=XRAY_NEW_IMPL(*m_allocator,static_model_resource)(m_owner,std::move(data));
        query.set_unmanaged_resource(result,resources::nocache_memory,sizeof(*result));
    } else {
        auto* result=XRAY_NEW_IMPL(*m_allocator,static_model_asset_resource)(m_owner,std::move(data));
        query.set_unmanaged_resource(result,resources::nocache_memory,sizeof(*result));
    }
    query.finish_query(result_success);
}
void static_model_cook::translate_query(resources::query_result_for_cook& query) {
    try {
        if (!m_owner->accepting()) throw std::runtime_error("Static model cook session is closed");
        if (auto* user=query.user_data()) {
            if(auto* authored=user->get_if<authored_model_request>()){if(!authored->data)throw std::runtime_error("Missing authored model data");finish(query,authored->data);return;}
            if(auto* original=user->get_if<original_static_model_request>()) {
                if(m_skeletal || query.creation_data_from_user())throw std::runtime_error("Original runtime request requires a disk static model");
                if(!m_instance) {
                    original::query_static_model(query,m_owner,*m_allocator,query.get_requested_path(),original->cache_directory,
                        [this,&query](std::shared_ptr<static_model_data const> data){finish(query,std::move(data));});
                    return;
                }
                auto request=std::make_shared<resources::user_data_variant>();request->set(*original);
                resources::query_resource(query.get_requested_path(),resources::static_render_model_class,[this,&query,request](resources::queries_result& result){
                    try {
                        if(!result.is_successful()){query.finish_query(result_error);return;}
                        auto* asset=dynamic_cast<static_model_asset_resource*>(result[0].get_unmanaged_resource().c_ptr());
                        if(!asset || asset->owner!=m_owner)throw std::runtime_error("Foreign original static model asset");
                        finish(query,asset->data);
                    }catch(std::exception const& e){failed(query,e);}
                },m_allocator,request.get(),&query,assert_on_fail_false);
                return;
            }
            auto* clone=user->get_if<static_model_clone_request>();
            auto* source=clone ? dynamic_cast<static_model_resource*>(clone->source.c_ptr()) : nullptr;
            if(!m_instance || m_skeletal || !query.creation_data_from_user() || !source || source->owner!=m_owner || !source->data->inverse_bind_pose.empty())
                throw std::runtime_error("Static model clone requires a local static instance and creation request");
            finish(query,source->data);return;
        }
        if (auto bytes=query.creation_data_from_user()) {
            if (m_skeletal) throw std::runtime_error("Skeletal queries require a /render root with bind_pose");
            static_model_resource temporary(m_owner,bytes); finish(query,temporary.data); return;
        }
        std::string path=query.get_requested_path();
        if(auto authored=m_owner->model_override(path)){finish(query,std::move(authored));return;}
        if (path.empty() || path.size()>=fs_new::max_path_length) throw std::runtime_error("Invalid static model request path");
        if (m_instance) {
            resources::query_resource(path.c_str(),m_skeletal ? resources::skeleton_render_model_class : resources::static_render_model_class,[this,&query](resources::queries_result& result) {
                try {
                    if (!result.is_successful()) { query.finish_query(result_error); return; }
                    auto* asset=dynamic_cast<static_model_asset_resource*>(result[0].get_unmanaged_resource().c_ptr());
                    if (!asset || asset->owner!=m_owner) throw std::runtime_error("Foreign static model asset");
                    finish(query,asset->data);
                } catch (std::exception const& e) { failed(query,e); }
            },m_allocator,nullptr,&query,assert_on_fail_false);
            return;
        }
        resources::query_vfs_iterator(fs_new::virtual_path_string(path.c_str()),[this,&query,path](vfs::vfs_locked_iterator const& iterator) {
            try {
                if (!m_owner->accepting()) throw std::runtime_error("Static model cook session is closed");
                if (iterator.is_end()) throw std::runtime_error("Static model path was not found");
                if (!iterator.is_folder()) {
                    if (m_skeletal) throw std::runtime_error("Skeletal queries require a /render root with bind_pose");
                    resources::query_resource(path.c_str(),resources::raw_data_class,[this,&query](resources::queries_result& result) {
                        try {
                            if (!result.is_successful()) { query.finish_query(result_error); return; }
                            resources::pinned_ptr_const<u8> bytes(result[0].get_managed_resource());
                            static_model_resource temporary(m_owner,bytes); finish(query,temporary.data);
                        } catch (std::exception const& e) { failed(query,e); }
                    },m_allocator,nullptr,&query,assert_on_fail_false);
                    return;
                }
                if (!path.ends_with("/render")) throw std::runtime_error("Static model root must end with /render");
                std::vector<std::string> names;
                for (auto it=iterator.children_begin();it!=iterator.children_end();++it) if (it.is_folder()) {
                    if (names.size()>=128) throw std::runtime_error("Static model exceeds 128 surfaces");
                    std::string name=it.get_name();
                    if (name.empty() || name.size()>128) throw std::runtime_error("Invalid static model surface name");
                    names.push_back(std::move(name));
                }
                if (names.empty()) throw std::runtime_error("Static model root has no surface folders");
                std::sort(names.begin(),names.end());
                load_root(query,path,std::move(names));
            } catch (std::exception const& e) { failed(query,e); }
        },m_allocator,resources::recursive_false,&query);
    } catch (std::exception const& e) { failed(query,e); }
}
void static_model_cook::load_root(resources::query_result_for_cook& query,std::string const& path,std::vector<std::string> names) {
    // Keep source folders as the surface manifest, matching the legacy cooks.
    auto data_path=binary::converted_path(path);
    std::vector<std::string> paths; paths.push_back(data_path+"/export_properties");
    for (auto const& name : names) { paths.push_back(data_path+"/"+name+"/converted_model"); paths.push_back(data_path+"/"+name+"/export_properties"); }
    if (m_skeletal) paths.push_back(data_path+"/bind_pose");
    std::vector<resources::request> requests;
    for (auto const& p : paths) {
        if (p.size()>=fs_new::max_path_length) throw std::runtime_error("Static surface path exceeds the VFS limit");
        requests.push_back({p.c_str(),resources::raw_data_class});
    }
    resources::query_resources(requests.data(),u32(requests.size()),[this,&query,names=std::move(names),path](resources::queries_result& result) {
        try {
            if (!result.is_successful()) { query.finish_query(result_error); return; }
            auto model=std::make_shared<static_model_data>();
            resources::pinned_ptr_const<u8> root(result[0].get_managed_resource());
            model->properties=load_model_properties(root,*m_allocator);
            if (model->properties.type!=(m_skeletal ? mt_skinned_mesh : mt_static_mesh)) throw std::runtime_error("Unsupported model-root type");
            size_t total_bytes=root.size();
            if (m_skeletal) {
                resources::pinned_ptr_const<u8> bind(result[u32(1+names.size()*2)].get_managed_resource());
                total_bytes+=bind.size(); auto pose=load_skeletal_bind_pose(bind);
                model->inverse_bind_pose=std::move(pose.inverse); model->bone_names=std::move(pose.names);
                for (auto const& locator : model->properties.locators)
                    if (locator.m_bone!=u16(-1) && locator.m_bone>=model->inverse_bind_pose.size()) throw std::runtime_error("Locator bone exceeds bind pose");
            }
            bool const flip_speedtree_v=!m_skeletal
                && (path.starts_with("resources/models/speedtree/")
                    || path.starts_with("resources.converted/models/speedtree/"));
            for (size_t i=0;i<names.size();++i) {
                resources::pinned_ptr_const<u8> bytes(result[u32(1+i*2)].get_managed_resource()),properties(result[u32(2+i*2)].get_managed_resource());
                total_bytes+=bytes.size()+properties.size();
                if (total_bytes>64u*1024u*1024u) throw std::runtime_error("Static model exceeds 64 MiB aggregate import limit");
                static_surface_data surface; surface.name=names[i]; surface.properties=load_model_properties(properties,*m_allocator);
                if (surface.properties.type!=(m_skeletal ? mt_skinned_submesh_4w : mt_static_submesh)) throw std::runtime_error("Unsupported surface type");
                if (m_skeletal) {
                    for (auto const& locator : surface.properties.locators)
                        if (locator.m_bone!=u16(-1) && locator.m_bone>=model->inverse_bind_pose.size()) throw std::runtime_error("Surface locator bone exceeds bind pose");
                    surface.skin=load_skeletal_mesh(bytes,u32(model->inverse_bind_pose.size()));
                    std::vector<math::float4x4> identity(model->inverse_bind_pose.size(),math::float4x4().identity());
                    surface.mesh=skin_skeletal_mesh(*surface.skin,identity);
                } else {
                    surface.mesh=load_static_mesh(bytes);
                    if (flip_speedtree_v) {
                        // SpeedTree .srt exports OpenGL bottom-up V coordinates while Vulkan TGA textures load top-down.
                        auto flipped=std::make_shared<static_mesh_data>(*surface.mesh);
                        for (auto& vertex : flipped->vertices) vertex.uv[1]=1.0f-vertex.uv[1];
                        surface.mesh=std::move(flipped);
                    }
                }
                contains(surface.properties,surface.mesh->minimum,surface.mesh->maximum);
                contains(model->properties,surface.properties.minimum,surface.properties.maximum);
                model->surfaces.push_back(std::move(surface));
            }
            load_materials(query,path.substr(0,path.size()-7)+"/settings",std::move(model));
        } catch (std::exception const& e) { failed(query,e); }
    },m_allocator,nullptr,&query,assert_on_fail_false);
}
void static_model_cook::load_materials(resources::query_result_for_cook& query,std::string const& path,std::shared_ptr<static_model_data> model) {
    // DCC staging validates geometry first. The host binds the saved surface
    // overrides through authored_model_request before publishing this generation.
    if(path.starts_with("resources/models/__sdk6_preview/dcc/")){finish(query,std::move(model));return;}
    auto cooked=binary::converted_path(path);
    resources::query_vfs_iterator(fs_new::virtual_path_string(cooked.c_str()),[this,&query,path,cooked,model](vfs::vfs_locked_iterator const& it) {
        try {
            if (!m_owner->accepting()) throw std::runtime_error("Static model cook session is closed");
            if (it.is_end()) {
                if (path==cooked) { finish(query,model); return; }
                resources::query_vfs_iterator(fs_new::virtual_path_string(path.c_str()),[this,&query,model](vfs::vfs_locked_iterator const& source) {
                    try {
                        if (!source.is_end()) throw std::runtime_error("Source model settings exist without a cooked binary config");
                        finish(query,model);
                    } catch (std::exception const& e) { failed(query,e); }
                },m_allocator,resources::recursive_false,&query);
                return;
            }
            if (it.is_folder()) throw std::runtime_error("Model settings must be a binary config file");
            resources::query_resource(cooked.c_str(),resources::raw_data_class,[this,&query,model](resources::queries_result& settings) {
                try {
                    if (!settings.is_successful()) { query.finish_query(result_error); return; }
                    resources::pinned_ptr_const<u8> bytes(settings[0].get_managed_resource());
                    binary::checked_config config(bytes,*m_allocator);
                    if (auto const* lod_root=binary::optional(config.root(),"lod_hierrarchy")) {
                        static constexpr std::array<pcstr,3> lod_names={"LOD0","LOD1","LOD2"};
                        static constexpr std::array<float,3> default_thresholds={0.15f,0.05f,0.0f};
                        std::vector<model_lod_entry> parsed_lods;
                        for (size_t li=0;li<lod_names.size();++li) {
                            auto const* lod_tbl=binary::optional(*lod_root,lod_names[li]);
                            if (!lod_tbl) continue;
                            auto const* surfs=binary::optional(*lod_tbl,"surfaces");
                            if (!surfs || surfs->empty()) continue;
                            model_lod_entry entry;
                            entry.min_screen_coverage=default_thresholds[li];
                            for (auto const& s : *surfs) entry.surfaces.emplace_back(s.key());
                            parsed_lods.push_back(std::move(entry));
                        }
                        if (parsed_lods.size()>=2) model->lods=std::move(parsed_lods);
                    }
                    auto const& table=binary::field(config.root(),"material_settings",configs::t_table_named);
                    if (table.size()!=model->surfaces.size()) throw std::runtime_error("Model material_settings must match every surface");
                    std::vector<std::string> fields;for(auto const& field:table)fields.emplace_back(field.key());
                    std::vector<std::string> names; std::vector<size_t> indices;
                    for (size_t i=0;i<model->surfaces.size();++i) {
                        // VFS canonicalizes folders to lowercase. Keep the exact
                        // authored shader/surface key in the resource and UI.
                        model->surfaces[i].name=source_surface_name(model->surfaces[i].name,fields);
                        auto const& surface=binary::field(table,model->surfaces[i].name.c_str(),configs::t_table_named);
                        std::string name=pcstr(binary::field(surface,"material_name",configs::t_string));
                        if (!name.empty()) { binary::validate_path(name); names.push_back(std::move(name)); indices.push_back(i); }
                    }
                    if (names.empty()) { finish(query,model); return; }
                    std::vector<resources::request> requests;
                    for (auto const& name : names) requests.push_back({name.c_str(),resources::material_class});
                    resources::query_resources(requests.data(),u32(requests.size()),[this,&query,model,indices](resources::queries_result& materials) {
                        try {
                            if (!materials.is_successful()) { query.finish_query(result_error); return; }
                            for (size_t i=0;i<indices.size();++i) model->surfaces[indices[i]].material=materials[u32(i)].get_unmanaged_resource();
                            finish(query,model);
                        } catch (std::exception const& e) { failed(query,e); }
                    },m_allocator,nullptr,&query,assert_on_fail_false);
                } catch (std::exception const& e) { failed(query,e); }
            },m_allocator,nullptr,&query,assert_on_fail_false);
        } catch (std::exception const& e) { failed(query,e); }
    },m_allocator,resources::recursive_false,&query);
}
void static_model_cook::delete_resource(resources::resource_base* resource) {
    if (m_instance) { auto* typed=static_cast<static_model_resource*>(resource); XRAY_DELETE_IMPL(*m_allocator,typed); }
    else { auto* typed=static_cast<static_model_asset_resource*>(resource); XRAY_DELETE_IMPL(*m_allocator,typed); }
}
skeleton_model_cook::skeleton_model_cook(std::shared_ptr<scene_registry> owner)
    : translate_query_cook(resources::skeleton_model_instance_class,reuse_false,use_current_thread_id),
      m_owner(std::move(owner)),m_allocator(render::g_allocator) {}
void skeleton_model_cook::translate_query(resources::query_result_for_cook& query) {
    try {
        if (!m_owner->accepting()) throw std::runtime_error("Skeleton model cook session is closed");
        if (query.user_data() || query.creation_data_from_user()) throw std::runtime_error("Skeleton model wrapper requires a logical model name");
        std::string name=query.get_requested_path(); binary::validate_path(name);
        std::string root="resources/models/"+name+".skinned_model";
        binary::validate_path(root+"/render");
        // Animation owns skeleton_class; never register or replace its cook here.
        if (!resources::cook_base::find_cook(resources::skeleton_class)) throw std::runtime_error("Animation skeleton cook is not registered");
        auto descriptor=binary::converted_path(root+"/skeleton");
        resources::query_resource(descriptor.c_str(),resources::raw_data_class,[this,&query,root](resources::queries_result& result) {
            try {
                if (!m_owner->accepting()) throw std::runtime_error("Skeleton model cook session is closed");
                if (!result.is_successful()) { query.finish_query(result_error); return; }
                resources::pinned_ptr_const<u8> bytes(result[0].get_managed_resource());
                binary::checked_config config(bytes,*m_allocator);
                std::string skeleton=pcstr(binary::field(config.root(),"skeleton",configs::t_string));
                binary::validate_path(skeleton);
                binary::converted_path(skeleton+".skeleton");
                std::string render=root+"/render";
                if (!resources::cook_base::find_cook(resources::skeleton_class)) throw std::runtime_error("Animation skeleton cook was unregistered");
                resources::request requests[]={{skeleton.c_str(),resources::skeleton_class},{render.c_str(),resources::skeleton_render_model_instance_class}};
                // One aggregate callback owns completion, including partial child failure.
                resources::query_resources(requests,2,[this,&query](resources::queries_result& children) {
                    try {
                        if (!m_owner->accepting()) throw std::runtime_error("Skeleton model cook session is closed");
                        if (!children.is_successful()) { query.finish_query(result_error); return; }
                        auto* skeleton=dynamic_cast<animation::skeleton*>(children[0].get_unmanaged_resource().c_ptr());
                        auto* model=dynamic_cast<static_model_resource*>(children[1].get_unmanaged_resource().c_ptr());
                        if (!skeleton || !model || model->owner!=m_owner) throw std::runtime_error("Foreign skeletal model dependency");
                        auto const& names=model->data->bone_names;
                        // Game players omit the movement root from their matrix arrays.
                        if (names.empty() || names.size()!=model->data->inverse_bind_pose.size()
                            || skeleton->get_bones_count()!=names.size()+1
                            || skeleton->get_bone(0).children_begin()!=&skeleton->get_bone(1))
                            throw std::runtime_error("Animation skeleton must contain one movement root plus every render bone");
                        for (size_t i=0;i<names.size();++i)
                            if (names[i]!=skeleton->get_bone(u32(i+1)).id())
                                throw std::runtime_error("Animation/render bone order mismatch");
                        auto* wrapper=XRAY_NEW_IMPL(*m_allocator,skeleton_model_instance)();
                        wrapper->m_skeleton=skeleton; wrapper->m_render_model=model;
                        query.set_unmanaged_resource(wrapper,resources::nocache_memory,sizeof(*wrapper));
                        query.finish_query(result_success);
                    } catch (std::exception const& e) { failed(query,e); }
                },m_allocator,nullptr,&query,assert_on_fail_false);
            } catch (std::exception const& e) { failed(query,e); }
        },m_allocator,nullptr,&query,assert_on_fail_false);
    } catch (std::exception const& e) { failed(query,e); }
}
void skeleton_model_cook::delete_resource(resources::resource_base* resource) {
    auto* wrapper=static_cast<skeleton_model_instance*>(resource); XRAY_DELETE_IMPL(*m_allocator,wrapper);
}
} } }
