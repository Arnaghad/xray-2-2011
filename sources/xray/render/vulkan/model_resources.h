#ifndef XRAY_VULKAN_MODEL_RESOURCES_H_INCLUDED
#define XRAY_VULKAN_MODEL_RESOURCES_H_INCLUDED
#include <xray/physics/model_physics.h>
#include <xray/render/vulkan/scene_resources.h>
#include <xray/render/facade/model.h>
#include <xray/render/base/common_types.h>
#include <xray/vectora.h>
#include <xray/render/vulkan/decal_resources.h>
#include <vector>
#include <string>

namespace xray { namespace render { namespace vulkan {
struct texture_data;
struct skeletal_mesh_data;
// The existing model_converter static_decl / vb_sw_struct disk layout.
struct static_vertex {
    float position[3];
    u32 normal, tangent, binormal;
    float uv[2];
};
static_assert(sizeof(static_vertex) == 32);
struct static_mesh_data {
    std::vector<static_vertex> vertices;
    std::vector<u16> indices;
    std::array<float,3> minimum, maximum;
};
XRAY_RENDER_VULKAN_API std::shared_ptr<static_mesh_data const> load_static_mesh(const_buffer);
XRAY_RENDER_VULKAN_API void validate_model_buffer_fragments(vectora<buffer_fragment> const&, size_t byte_size);
struct model_properties {
    u32 type=0;
    std::array<float,3> minimum={}, maximum={};
    std::vector<model_locator_item> locators;
};
XRAY_RENDER_VULKAN_API model_properties load_model_properties(const_buffer, memory::base_allocator&);
struct static_surface_data {
    std::string name;
    std::shared_ptr<static_mesh_data const> mesh;
    model_properties properties;
    resources::unmanaged_resource_ptr material;
    std::shared_ptr<skeletal_mesh_data const> skin;
};
struct model_lod_entry {
    float min_screen_coverage=0.f;
    std::vector<std::string> surfaces;
};
struct static_model_data {
    std::string identity,source_path;
    model_properties properties;
    std::vector<static_surface_data> surfaces;
    std::vector<model_lod_entry> lods;
    std::vector<math::float4x4> inverse_bind_pose;
    std::vector<std::string> bone_names;
    physics::model_physics_parameters physics;
};
// A cook-owned independent static instance of an existing immutable asset.
// Mutable material, visibility, transform and buffer overrides are not copied.
struct static_model_clone_request {render_model_instance_ptr source;};
struct authored_model_request {std::shared_ptr<static_model_data const> data;};
// Explicit opt-in to native cooking of original exported/source static assets.
// The caller mounts original.exported and resources (original sources) read-only
// and supplies a separate writable cache directory.
struct original_static_model_request {std::string cache_directory;};
class static_model_asset_resource final : public resources::unmanaged_resource {
public:
    std::shared_ptr<scene_registry> const owner;
    std::shared_ptr<static_model_data const> const data;
    static_model_asset_resource(std::shared_ptr<scene_registry> state, std::shared_ptr<static_model_data const> model)
        : owner(std::move(state)),data(std::move(model)) {}
};
struct surface_instance {
    bool visible=true;
    resources::unmanaged_resource_ptr material;
    std::shared_ptr<static_mesh_data const> mesh;
    bool material_override=false;
};
class XRAY_RENDER_VULKAN_API static_model_resource final : public render_model_instance {
public:
    std::shared_ptr<scene_registry> const owner;
    std::shared_ptr<static_model_data const> const data;
    std::vector<surface_instance> surfaces; // Render-thread state, indexed by data->surfaces.
    std::array<float,3> geometry_minimum,geometry_maximum;
    math::float4x4 transform;
    u64 temporal_revision=1;
    std::shared_ptr<std::vector<math::float4x4> const> bone_palette;
    u64 skin_revision=0;
    static_model_resource(std::shared_ptr<scene_registry>, const_buffer);
    static_model_resource(std::shared_ptr<scene_registry>, std::shared_ptr<static_model_data const>);
    ~static_model_resource() override;
    size_t surface_index(fs_new::virtual_path_string const&) const;
    void update_buffer(vectora<buffer_fragment> const&, bool indices);
    void update_skeleton(math::float4x4 const*,u32);
    bool get_locator(pcstr, model_locator_item&) const override;
};
struct model_instance_entry {
    render_model_instance_ptr model;
    math::float4x4 transform;
    bool selected=false;
};
struct model_draw {
    render_model_instance_ptr model;
    std::shared_ptr<static_mesh_data const> mesh;
    math::float4x4 transform;
    bool visible;
    resources::unmanaged_resource_ptr material;
    std::shared_ptr<texture_data const> texture;
    std::shared_ptr<texture_data const> normal_texture,roughness_texture;
    std::shared_ptr<texture_data const> specular_texture;
    std::shared_ptr<texture_data const> detail_normal_texture;
    std::shared_ptr<texture_data const> emissive_texture,detail_diffuse_texture,variation_mask_texture,fresnel_texture,anisotropy_dir_texture,diffuse_power_texture;
    std::shared_ptr<texture_data const> bump_texture,translucency_texture,whirl_texture,transparency_texture;
    std::shared_ptr<texture_data const> cubemap_texture,cubemap_mask_texture,subdermal_texture,ao_texture,back_illumination_texture,scattering_amount_texture,scattering_depth_texture;
    std::shared_ptr<skeletal_mesh_data const> skin;
    std::shared_ptr<std::vector<math::float4x4> const> bone_palette;
    u64 skin_revision=0;
    // Packed-depth G channel: 0 excluded, 1/4 static, 1/2 terrain.
    float decal_receiver=.25f;
};
struct scene_models { std::vector<model_instance_entry> items,terrain; std::vector<speedtree_instance_ptr> trees; std::vector<scene_decal> decals; };
} } }
#endif
