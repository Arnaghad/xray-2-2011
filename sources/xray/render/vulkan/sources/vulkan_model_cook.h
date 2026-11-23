#ifndef XRAY_VULKAN_MODEL_COOK_H_INCLUDED
#define XRAY_VULKAN_MODEL_COOK_H_INCLUDED
#include <xray/render/vulkan/model_resources.h>
#include <xray/resources_cook_classes.h>

namespace xray { namespace render { namespace vulkan {
class static_model_cook final : public resources::translate_query_cook {
    std::shared_ptr<scene_registry> m_owner;
    memory::base_allocator* m_allocator;
    bool m_instance;
    bool m_skeletal;
    void finish(resources::query_result_for_cook&,std::shared_ptr<static_model_data const>);
    void load_root(resources::query_result_for_cook&,std::string const&,std::vector<std::string>);
    void load_materials(resources::query_result_for_cook&,std::string const&,std::shared_ptr<static_model_data>);
public:
    static_model_cook(bool instance,std::shared_ptr<scene_registry>,bool skeletal=false);
    bool cache_by_game_resources_manager() override { return false; }
    void translate_query(resources::query_result_for_cook&) override;
    void delete_resource(resources::resource_base*) override;
};
class skeleton_model_cook final : public resources::translate_query_cook {
    std::shared_ptr<scene_registry> m_owner;
    memory::base_allocator* m_allocator;
public:
    explicit skeleton_model_cook(std::shared_ptr<scene_registry>);
    bool cache_by_game_resources_manager() override { return false; }
    void translate_query(resources::query_result_for_cook&) override;
    void delete_resource(resources::resource_base*) override;
};
} } }
#endif
