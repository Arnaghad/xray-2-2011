#ifndef XRAY_VULKAN_TERRAIN_COOK_H_INCLUDED
#define XRAY_VULKAN_TERRAIN_COOK_H_INCLUDED
#include <xray/render/vulkan/terrain_resources.h>
#include <xray/resources_cook_classes.h>

namespace xray { namespace render { namespace vulkan {
class terrain_cook final : public resources::translate_query_cook {
    std::shared_ptr<scene_registry> m_owner;
    memory::base_allocator* m_allocator;
    resources::class_id_enum m_kind;
    void load_layers(resources::query_result_for_cook&,std::shared_ptr<terrain_cell_data const>);
    void load_file(resources::query_result_for_cook&,std::string const&);
    void load_wrapper(resources::query_result_for_cook&,resources::unmanaged_resource_ptr);
public:
    terrain_cook(resources::class_id_enum,std::shared_ptr<scene_registry>);
    bool cache_by_game_resources_manager() override { return false; }
    void translate_query(resources::query_result_for_cook&) override;
    void delete_resource(resources::resource_base*) override;
};
} } }
#endif
