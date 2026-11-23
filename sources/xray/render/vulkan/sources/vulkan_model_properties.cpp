#include "pch.h"
#include "vulkan_binary_config.h"
#include <xray/render/vulkan/model_resources.h>
#include <cmath>
#include <cstring>
#include <set>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {
std::array<float,3> vector3(configs::binary_config_value const& table,pcstr key) {
    auto const& child=binary::field(table,key,configs::t_float3); std::array<float,3> result;
    std::memcpy(result.data(),static_cast<pcvoid>(child.data),sizeof(result));
    for (float n : result) if (!std::isfinite(n)) throw std::runtime_error("Non-finite export_properties vector");
    return result;
}
}
model_properties load_model_properties(const_buffer source,memory::base_allocator& allocator) {
    binary::checked_config config(source,allocator);
    auto const& root=config.root(); model_properties result;
    result.type=u32(binary::field(root,"type",configs::t_integer));
    auto const& bounds=binary::field(root,"bounding_box",configs::t_table_named);
    result.minimum=vector3(bounds,"min"); result.maximum=vector3(bounds,"max");
    for (unsigned axis=0;axis<3;++axis) if (result.minimum[axis]>result.maximum[axis]) throw std::runtime_error("Inverted export_properties bounds");
    if (auto* locators=binary::optional(root,"locators")) {
        if ((locators->type!=configs::t_table_indexed && !(locators->type==configs::t_table_named && locators->empty())) || locators->size()>256)
            throw std::runtime_error("Unsupported export_properties locators");
        std::set<std::string> names;
        for (auto const& locator : *locators) {
            pcstr name=binary::field(locator,"name",configs::t_string);
            if (!*name || std::strlen(name)>=32 || !names.insert(name).second) throw std::runtime_error("Invalid or duplicate locator name");
            s32 bone=binary::field(locator,"bone_idx",configs::t_integer);
            if (bone < -1 || bone>65535) throw std::runtime_error("Invalid locator bone index");
            auto p=vector3(locator,"position"),r=vector3(locator,"rotation");
            model_locator_item item={}; strings::copy(item.m_name,name); item.m_bone=u16(bone);
            item.m_offset=math::create_rotation(math::float3(r[0],r[1],r[2]))*math::create_translation(math::float3(p[0],p[1],p[2]));
            result.locators.push_back(item);
        }
    }
    return result;
}
} } }
