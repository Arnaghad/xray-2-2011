#ifndef XRAY_VULKAN_PARTICLE_MATERIAL_COOK_H_INCLUDED
#define XRAY_VULKAN_PARTICLE_MATERIAL_COOK_H_INCLUDED
#include <xray/render/vulkan/particle_resources.h>
#include <xray/resources_cook_classes.h>

namespace xray { namespace render { namespace vulkan {
class particle_material_cook final : public resources::translate_query_cook {
    std::shared_ptr<scene_registry> m_owner;
    memory::base_allocator* m_allocator;
    void finish(resources::query_result_for_cook&,resources::unmanaged_resource_ptr);
public:
    explicit particle_material_cook(std::shared_ptr<scene_registry>);
    bool cache_by_game_resources_manager() override { return false; }
    void translate_query(resources::query_result_for_cook&) override;
    void delete_resource(resources::resource_base*) override;
};
} } }
#endif
