#pragma once

#include <cstdint>
#include <map>
#include <stdexcept>
#include <string>
#include <string_view>
#include <variant>
#include <vector>

namespace xray::maya_cook::detail {

struct json_value;
using json_array = std::vector<json_value>;
using json_object = std::map<std::string, json_value, std::less<>>;

struct json_value {
    using variant_type = std::variant<
        std::nullptr_t,
        bool,
        double,
        std::string,
        json_array,
        json_object
    >;

    variant_type storage = nullptr;

    bool is_null() const { return std::holds_alternative<std::nullptr_t>(storage); }
    bool is_bool() const { return std::holds_alternative<bool>(storage); }
    bool is_number() const { return std::holds_alternative<double>(storage); }
    bool is_string() const { return std::holds_alternative<std::string>(storage); }
    bool is_array() const { return std::holds_alternative<json_array>(storage); }
    bool is_object() const { return std::holds_alternative<json_object>(storage); }

    bool as_bool() const;
    double as_double() const;
    std::int64_t as_i64() const;
    std::uint32_t as_u32() const;
    std::uint64_t as_u64() const;
    std::string const& as_string() const;
    json_array const& as_array() const;
    json_object const& as_object() const;

    json_value const* find(std::string_view key) const;
    json_value const& require(std::string_view key) const;
};

json_value parse_json(std::string_view input);

} // namespace xray::maya_cook::detail

