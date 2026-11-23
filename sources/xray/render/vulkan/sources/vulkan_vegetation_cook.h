#ifndef XRAY_VULKAN_VEGETATION_COOK_H_INCLUDED
#define XRAY_VULKAN_VEGETATION_COOK_H_INCLUDED
#include <xray/render/vulkan/vegetation_resources.h>
#include <xray/resources_cook_classes.h>
namespace xray { namespace render { namespace vulkan {
class vegetation_cook final : public resources::translate_query_cook {
    bool m_instance;
    std::shared_ptr<scene_registry> m_owner;
    memory::base_allocator* m_allocator;
public:
    vegetation_cook(bool,std::shared_ptr<scene_registry>);
    bool cache_by_game_resources_manager() override{return false;}
    void translate_query(resources::query_result_for_cook&) override;
    void delete_resource(resources::resource_base*) override;
};
} } }
#endif
