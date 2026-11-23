#include "pch.h"
#include <xray/render/vulkan/vegetation_resources.h>
#include "vulkan_scene_runtime.h"
#include <cmath>
#include <cstring>
#include <limits>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
std::shared_ptr<std::vector<leaf_card> const> load_leaf_cards(const_buffer bytes) {
    if (!bytes.c_ptr() || bytes.size()<12 || std::memcmp(bytes.c_ptr(),"VSTCARD1",8)) throw std::runtime_error("Invalid leaf-card stream header");
    auto* data=static_cast<u8 const*>(bytes.c_ptr());u32 count;std::memcpy(&count,data+8,4);
    if (count>16384 || bytes.size()!=12+size_t(count)*sizeof(leaf_card)) throw std::runtime_error("Leaf-card stream exceeds profile or is truncated");
    auto result=std::make_shared<std::vector<leaf_card>>(count);
    if(count)std::memcpy(result->data(),data+12,count*sizeof(leaf_card));
    for(auto const& card:*result) {
        float values[23];std::memcpy(values,&card,sizeof(values));
        for(float value:values)if(!std::isfinite(value) || std::abs(value)>1e4)throw std::runtime_error("Non-finite or unbounded leaf-card coordinate");
        for(auto const& basis:card.basis)for(u32 packed:basis){double length=0;for(unsigned c=0;c<3;++c){double x=((packed>>(c*8))&255)/127.5-1;length+=x*x;}
            if(length<.5 || length>1.5)throw std::runtime_error("Invalid packed leaf-card basis");}
    }
    return result;
}
float validate_tree_transform(math::float4x4 const& matrix) {
    for(auto const& row:matrix.elements)for(float value:row)if(!std::isfinite(value)||std::abs(value)>1e6)throw std::runtime_error("Invalid tree transform");
    if(matrix.i.w || matrix.j.w || matrix.k.w || matrix.c.w!=1)throw std::runtime_error("Tree transform must be affine");
    double length=matrix.i.xyz().squared_length();
    if(length<1e-8 || length>4096.*4096.)throw std::runtime_error("Tree scale outside [0.0001,4096]");
    for(unsigned a=0;a<3;++a)for(unsigned b=0;b<3;++b){double dot=0;for(unsigned c=0;c<3;++c)dot+=double(matrix.elements[a][c])*matrix.elements[b][c];
        if(std::abs(dot-(a==b ? length : 0.))>length*1e-4)throw std::runtime_error("Trees require uniform orthogonal scale");}
    float determinant=(matrix.i.xyz()^matrix.j.xyz())|matrix.k.xyz();
    if(determinant<=0)throw std::runtime_error("Reflected tree transform is unsupported");
    return float(std::sqrt(length));
}
std::shared_ptr<static_mesh_data const> expand_leaf_cards(std::vector<leaf_card> const& cards,
    math::float4x4 const& world,math::float4x4 const& view) {
    float scale=validate_tree_transform(world);
    if(std::abs(validate_tree_transform(view)-1)>1e-4f)throw std::runtime_error("Leaf cards require a rigid camera");
    math::float4x4 inverse;if(!inverse.try_invert(math::mul4x4(world,view)))throw std::runtime_error("Leaf-card camera is not invertible");
    auto mesh=std::make_shared<static_mesh_data>();mesh->minimum.fill(std::numeric_limits<float>::max());mesh->maximum.fill(std::numeric_limits<float>::lowest());
    if(cards.size()>16384)throw std::runtime_error("Leaf-card vertex budget exceeded");
    mesh->vertices.reserve(cards.size()*4);mesh->indices.reserve(cards.size()*6);
    for(auto const& card:cards){u16 base=u16(mesh->vertices.size());
        for(unsigned corner=0;corner<4;++corner){static_vertex vertex={};
            for(unsigned axis=0;axis<3;++axis){float value=card.center[axis]+scale*(card.offsets[corner][0]*inverse.elements[0][axis]
                +card.offsets[corner][1]*inverse.elements[1][axis]+card.planar[corner]*inverse.elements[2][axis]);
                if(!std::isfinite(value)||std::abs(value)>1e6)throw std::runtime_error("Expanded leaf card exceeds coordinate bounds");
                vertex.position[axis]=value;mesh->minimum[axis]=std::min(mesh->minimum[axis],value);mesh->maximum[axis]=std::max(mesh->maximum[axis],value);}
            vertex.normal=card.basis[corner][0];vertex.tangent=card.basis[corner][1];vertex.binormal=card.basis[corner][2];
            // SpeedTree .srt leaf-card UVs use OpenGL bottom-up V while Vulkan TGA textures load top-down.
            vertex.uv[0]=card.uv[corner][0];vertex.uv[1]=1.0f-card.uv[corner][1];mesh->vertices.push_back(vertex);}
        for(unsigned index:{0,1,2,0,2,3})mesh->indices.push_back(u16(base+index));}
    return mesh;
}
tree_asset::tree_asset(std::shared_ptr<scene_registry> state,std::string root,resources::unmanaged_resource_ptr dependency,
    std::shared_ptr<std::vector<leaf_card> const> leaves):owner(std::move(state)),model_root(std::move(root)),model(std::move(dependency)),cards(std::move(leaves)) {
    auto* source=dynamic_cast<static_model_asset_resource*>(model.c_ptr());
    if(!owner || !owner->accepting() || !source || source->owner!=owner || !cards)throw std::runtime_error("Invalid tree asset dependencies");
    auto const& p=source->data->properties;auto low=p.minimum,high=p.maximum;
    // The public tree bounds must contain every camera-facing orientation too.
    for(auto const& card:*cards){float radius=0;for(unsigned c=0;c<4;++c)radius=std::max(radius,std::sqrt(card.offsets[c][0]*card.offsets[c][0]
        +card.offsets[c][1]*card.offsets[c][1]+card.planar[c]*card.planar[c]));
        for(unsigned a=0;a<3;++a){low[a]=std::min(low[a],card.center[a]-radius);high[a]=std::max(high[a],card.center[a]+radius);}}
    m_bbox=math::create_aabb_min_max({low[0],low[1],low[2]},{high[0],high[1],high[2]});
}
tree_instance::tree_instance(std::shared_ptr<scene_registry> state,speedtree_tree_ptr tree,render_model_instance_ptr proxy)
    :speedtree_instance(tree),owner(std::move(state)),model(std::move(proxy)) {m_transform.identity();}
namespace {
tree_instance& tree(speedtree_instance_ptr const& value,std::shared_ptr<scene_registry> const& owner) {
    auto* result=dynamic_cast<tree_instance*>(value.c_ptr());
    if(!result || result->owner!=owner || !owner->accepting() || !owner->on_owner_thread())throw std::runtime_error("Foreign/closed tree instance or owner thread");
    return *result;
}
auto find_tree(std::vector<speedtree_instance_ptr>& items,speedtree_instance_ptr const& value){return std::find(items.begin(),items.end(),value);}
}
void scene_runtime::add_tree(scene_ptr const& target,speedtree_instance_ptr const& value,math::float4x4 const& matrix) {
    auto& destination=scene(target);auto& resource=tree(value,m_owner);validate_tree_transform(matrix);
    if(find_tree(destination.models->trees,value)!=destination.models->trees.end())throw std::runtime_error("Tree already belongs to this scene");
    size_t vertices=0;
    for(auto const& item:destination.models->trees)for(auto const& surface:model(tree(item,m_owner).model).surfaces)vertices+=surface.mesh->vertices.size();
    for(auto const& surface:model(resource.model).surfaces)vertices+=surface.mesh->vertices.size();
    if(destination.models->trees.size()>=64 || vertices>262144)throw std::runtime_error("Tree scene exceeds 64 instances / 262144 vertices");
    destination.models->trees.push_back(value);
    try{add_model(target,resource.model,matrix);}catch(...){destination.models->trees.pop_back();throw;}
    resource.m_transform=matrix;
}
void scene_runtime::update_tree(scene_ptr const& target,speedtree_instance_ptr const& value,math::float4x4 const& matrix) {
    auto& items=scene(target).models->trees;auto& resource=tree(value,m_owner);validate_tree_transform(matrix);
    if(find_tree(items,value)==items.end())throw std::runtime_error("Tree is outside this scene");
    update_model(target,resource.model,matrix);resource.m_transform=matrix;
}
void scene_runtime::remove_tree(scene_ptr const& target,speedtree_instance_ptr const& value) {
    auto& items=scene(target).models->trees;auto& resource=tree(value,m_owner);auto it=find_tree(items,value);
    if(it==items.end())throw std::runtime_error("Tree is outside this scene");remove_model(target,resource.model);items.erase(it);
}
void scene_runtime::populate_trees(scene_ptr const& target){
    if(!m_owner->on_owner_thread())throw std::runtime_error("Tree population requires its scene owner thread");
    for(auto const& value:scene(target).models->trees)tree(value,m_owner);
}
void scene_runtime::tree_material(speedtree_instance_ptr const& value,fs_new::virtual_path_string const& surface,resources::unmanaged_resource_ptr const& material) {
    set_material(tree(value,m_owner).model,surface,material);
}
void scene_runtime::tree_visible(speedtree_instance_ptr const& value,bool visible){tree(value,m_owner).visible=visible;++model(tree(value,m_owner).model).temporal_revision;}
void scene_runtime::prepare_trees(scene_resource& target,math::float4x4 const& view) {
    size_t bytes=0;for(auto const& value:target.models->trees){auto& resource=tree(value,m_owner);auto const& asset=*static_cast<tree_asset*>(resource.m_speedtree_tree_ptr.c_ptr());
        if(resource.visible)bytes+=asset.cards->size()*(4*sizeof(static_vertex)+6*sizeof(u16));}
    if(bytes>16u*1024u*1024u-m_tree_frame_bytes)throw std::runtime_error("Tree frame exceeds 16 MiB expanded leaf cards");
    for(auto const& value:target.models->trees){auto& resource=tree(value,m_owner);auto& proxy=model(resource.model);
        auto placement=std::find_if(target.models->items.begin(),target.models->items.end(),[&](auto const& item){return item.model==resource.model;});
        if(placement==target.models->items.end())throw std::runtime_error("Tree scene membership is inconsistent");
        proxy.transform=placement->transform;
        for(auto& surface:proxy.surfaces)surface.visible=resource.visible;
        auto const& asset=*static_cast<tree_asset*>(resource.m_speedtree_tree_ptr.c_ptr());
        if(!resource.visible || asset.cards->empty())continue;
        auto index=proxy.surface_index(fs_new::virtual_path_string("leafcard"));
        auto next=expand_leaf_cards(*asset.cards,proxy.transform,view);
        auto const& previous=*proxy.surfaces[index].mesh;
        if(previous.indices!=next->indices || previous.vertices.size()!=next->vertices.size()
            || std::memcmp(previous.vertices.data(),next->vertices.data(),next->vertices.size()*sizeof(static_vertex)))
            proxy.surfaces[index].mesh=std::move(next);
        proxy.geometry_minimum.fill(std::numeric_limits<float>::max());proxy.geometry_maximum.fill(std::numeric_limits<float>::lowest());
        for(auto const& surface:proxy.surfaces)for(unsigned a=0;a<3;++a){proxy.geometry_minimum[a]=std::min(proxy.geometry_minimum[a],surface.mesh->minimum[a]);proxy.geometry_maximum[a]=std::max(proxy.geometry_maximum[a],surface.mesh->maximum[a]);}}
    m_tree_frame_bytes+=bytes;
}
} } }
