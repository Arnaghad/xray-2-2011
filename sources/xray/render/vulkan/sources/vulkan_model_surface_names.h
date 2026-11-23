#pragma once
#include <string>
#include <string_view>
#include <vector>
#include <stdexcept>
namespace xray::render::vulkan {
inline std::string source_surface_name(std::string_view folder,std::vector<std::string> const& fields) {
    auto folded=[](std::string_view name){std::string value(name);for(char& c:value)if(c>='A' && c<='Z')c=char(c-'A'+'a');return value;};
    auto key=folded(folder);std::string matched;
    for(auto const& field:fields)if(folded(field)==key){if(!matched.empty())throw std::runtime_error("Ambiguous model surface names after VFS case folding");matched=field;}
    if(matched.empty())throw std::runtime_error("Model settings omit the surface folder: "+std::string(folder));return matched;
}
}
