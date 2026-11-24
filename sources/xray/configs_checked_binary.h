#ifndef XRAY_CONFIGS_CHECKED_BINARY_H_INCLUDED
#define XRAY_CONFIGS_CHECKED_BINARY_H_INCLUDED
#include <xray/configs_binary_config.h>
#include <cstddef>
#include <memory>
#include <span>
#include <string>

namespace xray { namespace core { namespace configs { class binary_config; } } }
namespace xray { namespace configs { namespace checked {
class XRAY_CORE_API checked_config {
    std::unique_ptr<core::configs::binary_config> m_config;
public:
    checked_config(const_buffer,memory::base_allocator&);
    checked_config(std::span<std::byte const>,memory::base_allocator&);
    ~checked_config();
    binary_config_value const& root() const;
};
XRAY_CORE_API binary_config_value const* optional(binary_config_value const&,pcstr);
XRAY_CORE_API binary_config_value const& field(binary_config_value const&,pcstr,u16);
XRAY_CORE_API std::string converted_path(std::string);
XRAY_CORE_API void validate_path(std::string const&);
} } }
#endif
