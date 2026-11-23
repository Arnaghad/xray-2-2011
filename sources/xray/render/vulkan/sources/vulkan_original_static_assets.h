#ifndef XRAY_VULKAN_ORIGINAL_STATIC_ASSETS_H_INCLUDED
#define XRAY_VULKAN_ORIGINAL_STATIC_ASSETS_H_INCLUDED
#include <xray/render/vulkan/model_resources.h>
#include <xray/resources_cook_classes.h>
#include <map>
#include <functional>

namespace xray { namespace render { namespace vulkan { namespace original {
using bytes=std::vector<unsigned char>;
using files=std::map<std::string,bytes>;
XRAY_RENDER_VULKAN_API files read_export_archive(const_buffer);
XRAY_RENDER_VULKAN_API bytes convert_static_surface(const_buffer vertices,const_buffer indices);
// Bounded source values, independent of the global editor Lua VM.
struct value {
    std::string name,text;
    u16 type=3;
    double number=0;
    std::vector<float> vector;
    std::vector<value> children;
    value const& field(pcstr) const;
    value& field(pcstr);
};
XRAY_RENDER_VULKAN_API std::vector<std::string> source_references(const_buffer);
XRAY_RENDER_VULKAN_API value read_source_config(files const&,std::string const& entry);
XRAY_RENDER_VULKAN_API bytes binary_config(value);
XRAY_RENDER_VULKAN_API value static_material(value);
XRAY_RENDER_VULKAN_API std::string validate_cache_directory(std::string const&,std::vector<std::string> const& source_roots);
void query_static_model(resources::query_result_for_cook&,std::shared_ptr<scene_registry>,memory::base_allocator&,
    std::string const& model,std::string const& cache,std::function<void(std::shared_ptr<static_model_data const>)>);
} } } }
#endif
