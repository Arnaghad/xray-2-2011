#ifndef XRAY_VULKAN_TEXTURE_COOK_H_INCLUDED
#define XRAY_VULKAN_TEXTURE_COOK_H_INCLUDED
#include <xray/render/vulkan/material_resources.h>
#include <xray/resources_cook_classes.h>

namespace xray { namespace render { namespace vulkan {
class texture_cook final : public resources::translate_query_cook {
    std::shared_ptr<scene_registry> m_owner;
    memory::base_allocator* m_allocator;
    void finish(resources::query_result_for_cook&,const_buffer,texture_configuration const&,texture_configuration const&,std::string const&,std::string const& virtual_path={});
    void load_file(resources::query_result_for_cook&,std::string const&,texture_configuration,std::string const& virtual_path={});
public:
    explicit texture_cook(std::shared_ptr<scene_registry>);
    bool cache_by_game_resources_manager() override { return false; }
    void translate_query(resources::query_result_for_cook&) override;
    void delete_resource(resources::resource_base*) override;
};
} } }
#endif
