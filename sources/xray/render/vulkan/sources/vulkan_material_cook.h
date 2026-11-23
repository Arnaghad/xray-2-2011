#ifndef XRAY_VULKAN_MATERIAL_COOK_H_INCLUDED
#define XRAY_VULKAN_MATERIAL_COOK_H_INCLUDED
#include <xray/render/vulkan/material_resources.h>
#include <xray/resources_cook_classes.h>

namespace xray { namespace render { namespace vulkan {
class material_cook final : public resources::translate_query_cook {
    std::shared_ptr<scene_registry> m_owner;
    memory::base_allocator* m_allocator;
    void finish(resources::query_result_for_cook&,material_configuration const&,surface_material_parameters const& = {},surface_material_textures const& = {},material_contributions const& = {},material_blend_mode = material_blend_mode::opaque,decal_material_parameters const& = {},distortion_material_parameters const& = {},std::string identity={});
    void load_file(resources::query_result_for_cook&,std::string const&);
    void load_bytes(resources::query_result_for_cook&,const_buffer,std::string identity={});
public:
    explicit material_cook(std::shared_ptr<scene_registry>);
    bool cache_by_game_resources_manager() override { return false; }
    void translate_query(resources::query_result_for_cook&) override;
    void delete_resource(resources::resource_base*) override;
};
} } }
#endif
