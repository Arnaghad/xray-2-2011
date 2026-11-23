#include "json_value.hpp"

#include <cctype>
#include <charconv>
#include <cmath>
#include <cstdint>
#include <stdexcept>

namespace xray::maya_cook::detail {

namespace {

void append_utf8(std::string& out, std::uint32_t cp) {
    if (cp <= 0x7Fu) {
        out.push_back(static_cast<char>(cp));
    } else if (cp <= 0x7FFu) {
        out.push_back(static_cast<char>(0xC0u | (cp >> 6)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    } else if (cp <= 0xFFFFu) {
        out.push_back(static_cast<char>(0xE0u | (cp >> 12)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    } else if (cp <= 0x10FFFFu) {
        out.push_back(static_cast<char>(0xF0u | (cp >> 18)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 12) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    } else {
        throw std::runtime_error("JSON unicode code point out of range");
    }
}

class parser {
public:
    explicit parser(std::string_view s) : m_src(s) {}

    json_value parse() {
        skip_ws();
        auto val = parse_value(0);
        skip_ws();
        if (m_pos != m_src.size()) {
            throw std::runtime_error("Trailing characters after JSON document");
        }
        return val;
    }

private:
    static constexpr unsigned max_depth = 64;
    std::string_view m_src;
    std::size_t m_pos = 0;

    void skip_ws() {
        while (m_pos < m_src.size()) {
            char c = m_src[m_pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') {
                ++m_pos;
            } else {
                break;
            }
        }
    }

    char peek() const {
        if (m_pos >= m_src.size()) {
            throw std::runtime_error("Unexpected end of JSON input");
        }
        return m_src[m_pos];
    }

    char take() {
        char c = peek();
        ++m_pos;
        return c;
    }

    void expect(std::string_view lit) {
        if (m_src.substr(m_pos, lit.size()) != lit) {
            throw std::runtime_error("Invalid JSON literal");
        }
        m_pos += lit.size();
    }

    std::uint16_t parse_hex4() {
        if (m_pos + 4 > m_src.size()) {
            throw std::runtime_error("Truncated JSON \\uXXXX escape");
        }
        std::uint16_t v = 0;
        for (int i = 0; i < 4; ++i) {
            char c = m_src[m_pos++];
            v <<= 4;
            if (c >= '0' && c <= '9') v |= static_cast<std::uint16_t>(c - '0');
            else if (c >= 'a' && c <= 'f') v |= static_cast<std::uint16_t>(10 + c - 'a');
            else if (c >= 'A' && c <= 'F') v |= static_cast<std::uint16_t>(10 + c - 'A');
            else throw std::runtime_error("Invalid hex digit in JSON string escape");
        }
        return v;
    }

    std::string parse_string() {
        if (take() != '"') {
            throw std::runtime_error("Expected JSON string");
        }
        std::string out;
        while (m_pos < m_src.size()) {
            char c = take();
            if (c == '"') {
                return out;
            }
            if (static_cast<unsigned char>(c) < 0x20u) {
                throw std::runtime_error("Unescaped control character in JSON string");
            }
            if (c != '\\') {
                out.push_back(c);
                continue;
            }
            char esc = take();
            switch (esc) {
            case '"': out.push_back('"'); break;
            case '\\': out.push_back('\\'); break;
            case '/': out.push_back('/'); break;
            case 'b': out.push_back('\b'); break;
            case 'f': out.push_back('\f'); break;
            case 'n': out.push_back('\n'); break;
            case 'r': out.push_back('\r'); break;
            case 't': out.push_back('\t'); break;
            case 'u': {
                std::uint32_t cp = parse_hex4();
                if (cp >= 0xD800u && cp <= 0xDBFFu && m_pos + 2 <= m_src.size() &&
                    m_src[m_pos] == '\\' && m_src[m_pos + 1] == 'u') {
                    m_pos += 2;
                    std::uint32_t low = parse_hex4();
                    if (low < 0xDC00u || low > 0xDFFFu) {
                        throw std::runtime_error("Invalid UTF-16 surrogate pair in JSON");
                    }
                    cp = 0x10000u + (((cp - 0xD800u) << 10) | (low - 0xDC00u));
                }
                append_utf8(out, cp);
                break;
            }
            default:
                throw std::runtime_error("Invalid JSON string escape sequence");
            }
        }
        throw std::runtime_error("Unterminated JSON string");
    }

    json_value parse_number() {
        std::size_t const start = m_pos;
        if (m_src[m_pos] == '-') ++m_pos;
        if (m_pos >= m_src.size() || !std::isdigit(static_cast<unsigned char>(m_src[m_pos]))) {
            throw std::runtime_error("Invalid JSON number");
        }
        if (m_src[m_pos] == '0') {
            ++m_pos;
        } else {
            while (m_pos < m_src.size() && std::isdigit(static_cast<unsigned char>(m_src[m_pos]))) {
                ++m_pos;
            }
        }
        if (m_pos < m_src.size() && m_src[m_pos] == '.') {
            ++m_pos;
            if (m_pos >= m_src.size() || !std::isdigit(static_cast<unsigned char>(m_src[m_pos]))) {
                throw std::runtime_error("Invalid JSON number fraction");
            }
            while (m_pos < m_src.size() && std::isdigit(static_cast<unsigned char>(m_src[m_pos]))) {
                ++m_pos;
            }
        }
        if (m_pos < m_src.size() && (m_src[m_pos] == 'e' || m_src[m_pos] == 'E')) {
            ++m_pos;
            if (m_pos < m_src.size() && (m_src[m_pos] == '+' || m_src[m_pos] == '-')) {
                ++m_pos;
            }
            if (m_pos >= m_src.size() || !std::isdigit(static_cast<unsigned char>(m_src[m_pos]))) {
                throw std::runtime_error("Invalid JSON number exponent");
            }
            while (m_pos < m_src.size() && std::isdigit(static_cast<unsigned char>(m_src[m_pos]))) {
                ++m_pos;
            }
        }
        double val = 0.0;
        auto [ptr, ec] = std::from_chars(m_src.data() + start, m_src.data() + m_pos, val);
        if (ec != std::errc{} || ptr != m_src.data() + m_pos || !std::isfinite(val)) {
            throw std::runtime_error("Out-of-range or non-finite JSON number");
        }
        return json_value{val};
    }

    json_value parse_value(unsigned depth) {
        if (depth > max_depth) {
            throw std::runtime_error("JSON nesting depth exceeds limit");
        }
        skip_ws();
        char c = peek();
        if (c == 'n') { expect("null"); return json_value{nullptr}; }
        if (c == 't') { expect("true"); return json_value{true}; }
        if (c == 'f') { expect("false"); return json_value{false}; }
        if (c == '"') { return json_value{parse_string()}; }
        if (c == '[') {
            take();
            json_array arr;
            skip_ws();
            if (peek() == ']') { take(); return json_value{std::move(arr)}; }
            for (;;) {
                arr.push_back(parse_value(depth + 1));
                skip_ws();
                char next = take();
                if (next == ']') break;
                if (next != ',') throw std::runtime_error("Expected ',' or ']' in JSON array");
            }
            return json_value{std::move(arr)};
        }
        if (c == '{') {
            take();
            json_object obj;
            skip_ws();
            if (peek() == '}') { take(); return json_value{std::move(obj)}; }
            for (;;) {
                skip_ws();
                std::string key = parse_string();
                skip_ws();
                if (take() != ':') throw std::runtime_error("Expected ':' in JSON object");
                obj.insert_or_assign(std::move(key), parse_value(depth + 1));
                skip_ws();
                char next = take();
                if (next == '}') break;
                if (next != ',') throw std::runtime_error("Expected ',' or '}' in JSON object");
            }
            return json_value{std::move(obj)};
        }
        return parse_number();
    }
};

} // namespace

bool json_value::as_bool() const {
    if (!is_bool()) throw std::runtime_error("JSON value is not boolean");
    return std::get<bool>(storage);
}

double json_value::as_double() const {
    if (!is_number()) throw std::runtime_error("JSON value is not numeric");
    double v = std::get<double>(storage);
    if (!std::isfinite(v)) throw std::runtime_error("JSON value is non-finite");
    return v;
}

std::int64_t json_value::as_i64() const {
    double v = as_double();
    if (std::trunc(v) != v || v < -9007199254740991.0 || v > 9007199254740991.0) {
        throw std::runtime_error("JSON number is not an exact integer");
    }
    return static_cast<std::int64_t>(v);
}

std::uint32_t json_value::as_u32() const {
    auto i = as_i64();
    if (i < 0 || i > 0xFFFFFFFFll) throw std::runtime_error("JSON integer out of u32 range");
    return static_cast<std::uint32_t>(i);
}

std::uint64_t json_value::as_u64() const {
    auto i = as_i64();
    if (i < 0) throw std::runtime_error("JSON integer must be non-negative");
    return static_cast<std::uint64_t>(i);
}

std::string const& json_value::as_string() const {
    if (!is_string()) throw std::runtime_error("JSON value is not a string");
    return std::get<std::string>(storage);
}

json_array const& json_value::as_array() const {
    if (!is_array()) throw std::runtime_error("JSON value is not an array");
    return std::get<json_array>(storage);
}

json_object const& json_value::as_object() const {
    if (!is_object()) throw std::runtime_error("JSON value is not an object");
    return std::get<json_object>(storage);
}

json_value const* json_value::find(std::string_view key) const {
    if (!is_object()) return nullptr;
    auto const& obj = std::get<json_object>(storage);
    auto it = obj.find(key);
    return it == obj.end() ? nullptr : &it->second;
}

json_value const& json_value::require(std::string_view key) const {
    auto const* p = find(key);
    if (!p) throw std::runtime_error(std::string("Missing required JSON key: ") + std::string(key));
    return *p;
}

json_value parse_json(std::string_view input) {
    return parser(input).parse();
}

} // namespace xray::maya_cook::detail

