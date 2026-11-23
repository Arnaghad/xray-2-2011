#ifndef XRAY_VULKAN_TERRAIN_RESOURCES_H_INCLUDED
#define XRAY_VULKAN_TERRAIN_RESOURCES_H_INCLUDED
#include <xray/render/vulkan/model_resources.h>
#include <xray/render/facade/terrain_memory_profile.h>
#include <span>
#include <optional>

namespace xray { namespace render { namespace vulkan {
// Chunk 102/103 uses the newer tex_shift layout, not facade's old 38-byte terrain_data.
#pragma pack(push, 1)
struct terrain_sample {
    float height;
    float shift[2];
    u8 texture_ids[3];
    u8 weights[3];
    u32 color;
};
#pragma pack(pop)
static_assert(sizeof(terrain_sample)==22);
struct terrain_cell_data {
    static constexpr size_t byte_limit=2u*1024u*1024u;
    u32 row_size=0;
    float physical_size=0;
    math::float4x4 transform;
    std::vector<std::string> textures;
    std::vector<terrain_sample> samples;
    // Local-space diagnostic geometry only. Layer weights/color/shift stay in samples.
    std::shared_ptr<static_mesh_data const> mesh;
};
struct terrain_cell_fragment {
    u32 x,y,width,height; // Half-open local grid rectangle, tightly packed rows.
    std::span<terrain_sample const> samples;
};
XRAY_RENDER_VULKAN_API std::shared_ptr<terrain_cell_data const> load_terrain_cell(const_buffer);
XRAY_RENDER_VULKAN_API void validate_terrain_texture_name(std::string const&);
// No publication on failure; unchanged edits retain the original snapshot identity.
XRAY_RENDER_VULKAN_API std::shared_ptr<terrain_cell_data const> update_terrain_cell(
    std::shared_ptr<terrain_cell_data const> const&, std::span<terrain_cell_fragment const>);
// Empty runtime slots cannot be referenced by sample or interpolated IDs, including zero-weight roles.
XRAY_RENDER_VULKAN_API std::shared_ptr<terrain_cell_data const> rebind_terrain_cell(
    std::shared_ptr<terrain_cell_data const> const&, std::vector<std::string> const&);
struct terrain_layer_resources {
    std::string name;
    resources::unmanaged_resource_ptr diffuse,normal,specular;
};
struct terrain_texture_set {
    std::vector<std::shared_ptr<texture_data const>> diffuse,normal,specular;
    terrain_memory_profile memory_profile=terrain_memory_profile::standard_256_mib;
};
struct terrain_texture_plan {
    std::array<u32,3> dimensions={},mip_levels={};
    std::array<bool,3> normalized={};
    size_t array_bytes=0,new_texture_bytes=0;
};
// Metadata-only preflight. No downsampling, format conversion or missing-mip synthesis.
XRAY_RENDER_VULKAN_API terrain_texture_plan plan_terrain_textures(terrain_texture_set const&);
// Previous source/output pairs must originate from this normalizer; unchanged inputs retain identities.
XRAY_RENDER_VULKAN_API std::shared_ptr<terrain_texture_set const> normalize_terrain_textures(terrain_texture_set const&,
    terrain_texture_set const* previous_sources=nullptr,std::shared_ptr<terrain_texture_set const> previous={});
class terrain_binding_update {
    friend class terrain_instance_resource;
    std::shared_ptr<u8 const> source_generation,next_generation;
    std::shared_ptr<terrain_cell_data const> previous,cell;
    std::vector<terrain_layer_resources> layers;
    std::vector<std::shared_ptr<texture_state>> states;
    std::shared_ptr<terrain_texture_set const> sources,textures;
};
class terrain_instance_resource;
struct terrain_binding_commit {
    terrain_instance_resource* instance;
    std::shared_ptr<terrain_binding_update const> candidate;
};
XRAY_RENDER_VULKAN_API void validate_terrain_textures(terrain_texture_set const&);
struct terrain_asset_data {
    static constexpr size_t texture_byte_limit=256u*1024u*1024u;
    std::shared_ptr<terrain_cell_data const> cell;
    std::vector<terrain_layer_resources> layers; // Original user-slot order, aliases retained.
    terrain_memory_profile memory_profile=terrain_memory_profile::standard_256_mib;
};
class terrain_mesh_resource final : public resources::unmanaged_resource {
public:
    std::shared_ptr<scene_registry> const owner;
    std::shared_ptr<terrain_asset_data const> const data;
    terrain_mesh_resource(std::shared_ptr<scene_registry> state,std::shared_ptr<terrain_asset_data const> asset)
        : owner(std::move(state)),data(std::move(asset)) {}
};
class XRAY_RENDER_VULKAN_API terrain_instance_resource final : public render_model_instance {
    bool m_game_geometry_locked=false;
    std::shared_ptr<terrain_cell_data const> m_cell;
    std::optional<math::aabb> m_bounds;
    std::shared_ptr<terrain_texture_set const> m_texture_sources,m_textures;
    std::vector<terrain_layer_resources> m_layers;
    std::shared_ptr<u8 const> m_binding_generation=std::make_shared<u8>(0);
    std::vector<std::shared_ptr<texture_state>> m_texture_states;
    std::vector<std::weak_ptr<texture_data const>> m_rejected_textures;
    u64 m_texture_commits=0,m_texture_rejections=0,m_binding_revision=0;
public:
    std::shared_ptr<scene_registry> const owner;
    resources::unmanaged_resource_ptr const asset;
    explicit terrain_instance_resource(std::shared_ptr<scene_registry>,resources::unmanaged_resource_ptr);
    ~terrain_instance_resource() override;
    std::shared_ptr<terrain_cell_data const> cell() const { return m_cell; }
    terrain_memory_profile memory_profile() const { return static_cast<terrain_mesh_resource*>(asset.c_ptr())->data->memory_profile; }
    void update_cell(std::span<terrain_cell_fragment const>);
    void update_transform(math::float4x4 const&);
    void seal_game_geometry();
    bool game_geometry_locked() const { return m_game_geometry_locked; }
    std::vector<terrain_layer_resources> const& texture_bindings() const { return m_layers; }
    u64 binding_revision() const { return m_binding_revision; }
    // Preflight already-loaded dependencies; no resource queries or publication. Render-thread only.
    std::shared_ptr<terrain_binding_update const> prepare_texture_bindings(std::vector<terrain_layer_resources>) const;
    // Reject stale geometry/bindings/reloads before publishing the complete slot table and triplet.
    void commit_texture_bindings(std::shared_ptr<terrain_binding_update const> const&);
    static void commit_texture_binding_batch(std::span<terrain_binding_commit const>);
    // Local culling hint unioned with current geometry; never changes mesh/cache identity.
    math::aabb local_bounds() const;
    void update_bounds(math::aabb const&);
    // Render-thread aggregate publication; pending/failed/incompatible reloads retain the previous set.
    std::shared_ptr<terrain_texture_set const> textures();
    std::array<u64,2> texture_set_updates() const { return {m_texture_commits,m_texture_rejections}; }
};
} } }
#endif
