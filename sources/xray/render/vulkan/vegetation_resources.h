#ifndef XRAY_VULKAN_VEGETATION_RESOURCES_H_INCLUDED
#define XRAY_VULKAN_VEGETATION_RESOURCES_H_INCLUDED
#include <xray/render/vulkan/model_resources.h>

namespace xray { namespace render { namespace vulkan {
struct leaf_card {
    float center[3],offsets[4][2],planar[4],uv[4][2];
    u32 basis[4][3];
};
static_assert(sizeof(leaf_card)==140);
XRAY_RENDER_VULKAN_API std::shared_ptr<std::vector<leaf_card> const> load_leaf_cards(const_buffer);
XRAY_RENDER_VULKAN_API float validate_tree_transform(math::float4x4 const&);
XRAY_RENDER_VULKAN_API std::shared_ptr<static_mesh_data const> expand_leaf_cards(
    std::vector<leaf_card> const&,math::float4x4 const& world,math::float4x4 const& view);
class tree_asset final : public speedtree_tree_base {
public:
    std::shared_ptr<scene_registry> const owner;
    std::string const model_root;
    resources::unmanaged_resource_ptr const model;
    std::shared_ptr<std::vector<leaf_card> const> const cards;
    tree_asset(std::shared_ptr<scene_registry>,std::string,resources::unmanaged_resource_ptr,
        std::shared_ptr<std::vector<leaf_card> const>);
};
class tree_instance final : public speedtree_instance {
public:
    std::shared_ptr<scene_registry> const owner;
    render_model_instance_ptr const model;
    bool visible=true;
    tree_instance(std::shared_ptr<scene_registry>,speedtree_tree_ptr,render_model_instance_ptr);
};
} } }
#endif
