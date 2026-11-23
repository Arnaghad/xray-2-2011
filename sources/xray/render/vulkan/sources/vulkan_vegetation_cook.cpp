#include "pch.h"
#include "vulkan_vegetation_cook.h"
#include "vulkan_binary_config.h"
#include "vulkan_diagnostics.h"
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/core/memory.h>
#include <xray/resources_query_result.h>
#include <xray/resources_pinned_ptr.h>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {void failed(resources::query_result_for_cook& query,std::exception const& error){
    diagnostic("error","Vulkan vegetation query failed: %s",error.what());query.finish_query(resources::cook_base::result_error);}}
vegetation_cook::vegetation_cook(bool instance,std::shared_ptr<scene_registry> owner)
    :translate_query_cook(instance ? resources::speedtree_instance_class : resources::speedtree_class,reuse_false,use_current_thread_id),
      m_instance(instance),m_owner(std::move(owner)),m_allocator(render::g_allocator){}
void vegetation_cook::translate_query(resources::query_result_for_cook& query){try{
    if(!m_owner->accepting() || !m_owner->on_owner_thread())throw std::runtime_error("Vegetation cook session is closed or foreign thread");
    if(query.user_data() || query.creation_data_from_user())throw std::runtime_error("Vegetation requires a named prepared fixed-LOD asset");
    std::string name=query.get_requested_path();binary::validate_path(name);
    if(m_instance){resources::query_resource(name.c_str(),resources::speedtree_class,[this,&query](resources::queries_result& result){try{
        if(!result.is_successful())throw std::runtime_error("Tree asset dependency failed");
        auto* asset=dynamic_cast<tree_asset*>(result[0].get_unmanaged_resource().c_ptr());
        if(!asset || asset->owner!=m_owner)throw std::runtime_error("Foreign tree asset");
        speedtree_tree_ptr retained=asset;
        resources::query_resource(asset->model_root.c_str(),resources::static_render_model_instance_class,[this,&query,retained](resources::queries_result& proxy){try{
            if(!m_owner->accepting() || !proxy.is_successful())throw std::runtime_error("Tree instance dependencies failed/closed");
            auto* model=dynamic_cast<static_model_resource*>(proxy[0].get_unmanaged_resource().c_ptr());
            if(!model || model->owner!=m_owner)throw std::runtime_error("Foreign tree model instance");
            auto* value=XRAY_NEW_IMPL(*m_allocator,tree_instance)(m_owner,retained,model);
            query.set_unmanaged_resource(value,resources::nocache_memory,sizeof(*value));query.finish_query(result_success);
        }catch(std::exception const& e){failed(query,e);}},m_allocator,nullptr,&query,assert_on_fail_false);
    }catch(std::exception const& e){failed(query,e);}},m_allocator,nullptr,&query,assert_on_fail_false);return;}
    std::string path="resources.converted/speedtree/"+name+".tree";
    binary::validate_path(path);
    resources::query_resource(path.c_str(),resources::raw_data_class,[this,&query](resources::queries_result& result){try{
        if(!m_owner->accepting() || !result.is_successful())throw std::runtime_error("Missing/closed prepared tree descriptor");
        resources::pinned_ptr_const<u8> bytes(result[0].get_managed_resource());binary::checked_config config(bytes,*m_allocator);auto const& root=config.root();
        if(root.size()!=4 || s32(binary::field(root,"version",configs::t_integer))!=1
            || std::strcmp(pcstr(binary::field(root,"profile",configs::t_string)),"fixed_highest_lod"))throw std::runtime_error("Unsupported tree version/profile (no wind or distance/billboard LOD)");
        std::string model=pcstr(binary::field(root,"model",configs::t_string)),cards=pcstr(binary::field(root,"leaf_cards",configs::t_string));
        binary::validate_path(model);binary::validate_path(cards);
        if(!model.starts_with("resources/models/") || !model.ends_with("/render") || !cards.starts_with("resources.converted/speedtree/"))
            throw std::runtime_error("Invalid prepared tree dependency paths");
        resources::request requests[]={{model.c_str(),resources::static_render_model_class},{cards.c_str(),resources::raw_data_class}};
        resources::query_resources(requests,2,[this,&query,model](resources::queries_result& dependencies){try{
            if(!m_owner->accepting() || !dependencies.is_successful())throw std::runtime_error("Tree mesh/material/card dependency failed/closed");
            auto* mesh=dynamic_cast<static_model_asset_resource*>(dependencies[0].get_unmanaged_resource().c_ptr());
            if(!mesh || mesh->owner!=m_owner || mesh->data->surfaces.size()>4)throw std::runtime_error("Invalid tree model dependency");
            resources::pinned_ptr_const<u8> data(dependencies[1].get_managed_resource());auto leaves=load_leaf_cards(data);
            bool leaf_surface=false;size_t vertices=0;
            for(auto const& surface:mesh->data->surfaces){
                if(surface.name!="branch" && surface.name!="frond" && surface.name!="leafmesh" && surface.name!="leafcard")throw std::runtime_error("Unknown tree component");
                auto* material=dynamic_cast<material_resource*>(surface.material.c_ptr());
                if(!material || material->blend!=material_blend_mode::opaque)throw std::runtime_error("Tree components require authored opaque/cutout materials");
                if(surface.name=="leafcard"){leaf_surface=true;if(surface.mesh->vertices.size()!=leaves->size()*4 || surface.mesh->indices.size()!=leaves->size()*6)throw std::runtime_error("Tree leaf-card mesh count mismatch");}
                vertices+=surface.mesh->vertices.size();}
            if(vertices>65536 || leaf_surface!=!leaves->empty())throw std::runtime_error("Tree geometry/card budget mismatch");
            auto* value=XRAY_NEW_IMPL(*m_allocator,tree_asset)(m_owner,model,dependencies[0].get_unmanaged_resource(),std::move(leaves));
            query.set_unmanaged_resource(value,resources::nocache_memory,sizeof(*value));query.finish_query(result_success);
        }catch(std::exception const& e){failed(query,e);}},m_allocator,nullptr,&query,assert_on_fail_false);
    }catch(std::exception const& e){failed(query,e);}},m_allocator,nullptr,&query,assert_on_fail_false);
}catch(std::exception const& e){failed(query,e);}}
void vegetation_cook::delete_resource(resources::resource_base* value){
    if(m_instance){auto* p=static_cast<tree_instance*>(value);XRAY_DELETE_IMPL(*m_allocator,p);}
    else{auto* p=static_cast<tree_asset*>(value);XRAY_DELETE_IMPL(*m_allocator,p);}}
} } }
