#include "lua_and_binary_config.hpp"

#include <algorithm>
#include <cctype>
#include <charconv>
#include <cmath>
#include <cstring>
#include <iomanip>
#include <sstream>
#include <stdexcept>

namespace xray::maya_cook::detail {

config_value config_value::make_bool(std::string key, bool v) {
    config_value c;
    c.kind = kind_t::boolean;
    c.name = std::move(key);
    c.number = v ? 1.0 : 0.0;
    return c;
}

config_value config_value::make_int(std::string key, std::int32_t v) {
    config_value c;
    c.kind = kind_t::integer;
    c.name = std::move(key);
    c.number = static_cast<double>(v);
    return c;
}

config_value config_value::make_float(std::string key, float v) {
    if (!std::isfinite(v)) {
        throw std::runtime_error("Non-finite float in config_value: " + key);
    }
    config_value c;
    c.kind = kind_t::number;
    c.name = std::move(key);
    c.number = static_cast<double>(v);
    return c;
}

config_value config_value::make_string(std::string key, std::string v) {
    config_value c;
    c.kind = kind_t::text;
    c.name = std::move(key);
    c.str = std::move(v);
    return c;
}

config_value config_value::make_float2(std::string key, float2 v) {
    config_value c;
    c.kind = kind_t::vec2;
    c.name = std::move(key);
    c.vec = {v.x, v.y, 0.0f, 0.0f};
    return c;
}

config_value config_value::make_float3(std::string key, float3 v) {
    config_value c;
    c.kind = kind_t::vec3;
    c.name = std::move(key);
    c.vec = {v.x, v.y, v.z, 0.0f};
    return c;
}

config_value config_value::make_float4(std::string key, float x, float y, float z, float w) {
    config_value c;
    c.kind = kind_t::vec4;
    c.name = std::move(key);
    c.vec = {x, y, z, w};
    return c;
}

config_value config_value::make_table(std::string key, bool indexed) {
    config_value c;
    c.kind = indexed ? kind_t::table_indexed : kind_t::table_named;
    c.name = std::move(key);
    return c;
}

config_value& config_value::set(config_value child) {
    for (auto& existing : children) {
        if (existing.name == child.name && !child.name.empty()) {
            existing = std::move(child);
            return existing;
        }
    }
    children.push_back(std::move(child));
    return children.back();
}

config_value* config_value::find(std::string_view key) {
    for (auto& child : children) {
        if (child.name == key) return &child;
    }
    return nullptr;
}

config_value const* config_value::find(std::string_view key) const {
    for (auto const& child : children) {
        if (child.name == key) return &child;
    }
    return nullptr;
}

namespace {

bool is_valid_lua_ident(std::string_view s) {
    if (s.empty()) return false;
    auto is_alpha = [](char c) {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    };
    if (!is_alpha(s[0])) return false;
    for (char c : s.substr(1)) {
        if (!is_alpha(c) && !(c >= '0' && c <= '9')) return false;
    }
    return s != "and" && s != "break" && s != "do" && s != "else" && s != "elseif" &&
           s != "end" && s != "false" && s != "for" && s != "function" && s != "if" &&
           s != "in" && s != "local" && s != "nil" && s != "not" && s != "or" &&
           s != "repeat" && s != "return" && s != "then" && s != "true" && s != "until" &&
           s != "while";
}

std::string escape_lua_string(std::string_view s) {
    std::string out;
    out.reserve(s.size() + 2);
    out.push_back('"');
    for (char c : s) {
        switch (c) {
        case '\\': out += "\\\\"; break;
        case '"': out += "\\\""; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default: out.push_back(c); break;
        }
    }
    out.push_back('"');
    return out;
}

std::string format_number(double d) {
    if (!std::isfinite(d)) {
        throw std::runtime_error("Non-finite number in Lua serializer");
    }
    if (d == 0.0) return "0";
    std::ostringstream oss;
    oss << std::setprecision(17) << d;
    return oss.str();
}

void write_lua_entry(std::ostream& os, config_value const& node, int indent, bool top_level) {
    auto pad = [&](int depth) {
        for (int i = 0; i < depth; ++i) os << '\t';
    };
    auto write_key = [&](std::string const& key) {
        if (is_valid_lua_ident(key)) {
            os << key << " = ";
        } else {
            os << "[" << escape_lua_string(key) << "] = ";
        }
    };

    if (!top_level) {
        pad(indent);
        if (!node.name.empty()) {
            write_key(node.name);
        }
    } else if (!node.name.empty()) {
        write_key(node.name);
    }

    switch (node.kind) {
    case config_value::kind_t::boolean:
        os << (node.number != 0.0 ? "true" : "false");
        break;
    case config_value::kind_t::integer:
        os << static_cast<std::int64_t>(node.number);
        break;
    case config_value::kind_t::number:
        os << format_number(node.number);
        break;
    case config_value::kind_t::text:
        os << escape_lua_string(node.str);
        break;
    case config_value::kind_t::vec2:
        os << "float2( " << format_number(node.vec[0]) << ", " << format_number(node.vec[1]) << " )";
        break;
    case config_value::kind_t::vec3:
        os << "float3( " << format_number(node.vec[0]) << ", " << format_number(node.vec[1])
           << ", " << format_number(node.vec[2]) << " )";
        break;
    case config_value::kind_t::vec4:
        os << "float4( " << format_number(node.vec[0]) << ", " << format_number(node.vec[1])
           << ", " << format_number(node.vec[2]) << ", " << format_number(node.vec[3]) << " )";
        break;
    case config_value::kind_t::table_named:
    case config_value::kind_t::table_indexed: {
        os << "{\n";
        std::vector<config_value const*> ordered;
        ordered.reserve(node.children.size());
        for (auto const& c : node.children) ordered.push_back(&c);
        if (node.kind == config_value::kind_t::table_named) {
            std::sort(ordered.begin(), ordered.end(), [](auto const* a, auto const* b) {
                return a->name < b->name;
            });
        }
        for (auto const* child : ordered) {
            write_lua_entry(os, *child, indent + 1, false);
            os << ",\n";
            if (child->kind == config_value::kind_t::table_named ||
                child->kind == config_value::kind_t::table_indexed) {
                os << "\n";
            }
        }
        pad(indent);
        os << "}";
        if (!node.name.empty()) {
            os << " -- " << node.name;
        }
        break;
    }
    }

    if (top_level) {
        os << "\n\n";
    }
}

class lua_parser {
public:
    explicit lua_parser(std::string_view src) : m_src(src) {}

    config_value parse_root() {
        config_value root = config_value::make_table({}, false);
        while (true) {
            skip_ws_and_comments();
            if (m_pos >= m_src.size()) break;
            std::string key = parse_key();
            skip_ws_and_comments();
            expect('=');
            config_value val = parse_value(std::move(key));
            root.set(std::move(val));
            skip_ws_and_comments();
            if (m_pos < m_src.size() && (m_src[m_pos] == ',' || m_src[m_pos] == ';')) {
                ++m_pos;
            }
        }
        return root;
    }

private:
    std::string_view m_src;
    std::size_t m_pos = 0;

    void skip_ws_and_comments() {
        while (m_pos < m_src.size()) {
            char c = m_src[m_pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') {
                ++m_pos;
                continue;
            }
            if (m_pos + 1 < m_src.size() && c == '-' && m_src[m_pos + 1] == '-') {
                m_pos += 2;
                while (m_pos < m_src.size() && m_src[m_pos] != '\n') ++m_pos;
                continue;
            }
            break;
        }
    }

    char peek() {
        skip_ws_and_comments();
        if (m_pos >= m_src.size()) {
            throw std::runtime_error("Unexpected end of Lua config");
        }
        return m_src[m_pos];
    }

    void expect(char expected) {
        if (peek() != expected) {
            throw std::runtime_error(std::string("Expected '") + expected + "' in Lua config");
        }
        ++m_pos;
    }

    std::string parse_ident() {
        skip_ws_and_comments();
        std::size_t start = m_pos;
        if (m_pos >= m_src.size() ||
            !(std::isalpha(static_cast<unsigned char>(m_src[m_pos])) || m_src[m_pos] == '_')) {
            throw std::runtime_error("Expected identifier in Lua config");
        }
        while (m_pos < m_src.size() &&
               (std::isalnum(static_cast<unsigned char>(m_src[m_pos])) || m_src[m_pos] == '_')) {
            ++m_pos;
        }
        return std::string(m_src.substr(start, m_pos - start));
    }

    std::string parse_string() {
        expect('"');
        std::string out;
        while (m_pos < m_src.size()) {
            char c = m_src[m_pos++];
            if (c == '"') return out;
            if (c == '\\' && m_pos < m_src.size()) {
                char esc = m_src[m_pos++];
                switch (esc) {
                case 'n': out.push_back('\n'); break;
                case 'r': out.push_back('\r'); break;
                case 't': out.push_back('\t'); break;
                case '\\': out.push_back('\\'); break;
                case '"': out.push_back('"'); break;
                default: out.push_back(esc); break;
                }
            } else {
                out.push_back(c);
            }
        }
        throw std::runtime_error("Unterminated Lua string");
    }

    std::string parse_key() {
        char c = peek();
        if (c == '[') {
            ++m_pos;
            char k = peek();
            std::string key;
            if (k == '"') {
                key = parse_string();
            } else {
                auto num = parse_number({});
                key = std::to_string(static_cast<std::int64_t>(num.number));
            }
            expect(']');
            return key;
        }
        return parse_ident();
    }

    config_value parse_number(std::string key) {
        skip_ws_and_comments();
        std::size_t start = m_pos;
        if (m_pos < m_src.size() && (m_src[m_pos] == '-' || m_src[m_pos] == '+')) ++m_pos;
        bool is_float = false;
        while (m_pos < m_src.size()) {
            char c = m_src[m_pos];
            if (std::isdigit(static_cast<unsigned char>(c))) {
                ++m_pos;
            } else if (c == '.' || c == 'e' || c == 'E') {
                is_float = true;
                ++m_pos;
                if ((c == 'e' || c == 'E') && m_pos < m_src.size() &&
                    (m_src[m_pos] == '+' || m_src[m_pos] == '-')) {
                    ++m_pos;
                }
            } else {
                break;
            }
        }
        if (!is_float) {
            std::int64_t iv = 0;
            auto [ptr, ec] = std::from_chars(m_src.data() + start, m_src.data() + m_pos, iv);
            if (ec == std::errc{} && ptr == m_src.data() + m_pos &&
                iv >= -2147483648ll && iv <= 2147483647ll) {
                return config_value::make_int(std::move(key), static_cast<std::int32_t>(iv));
            }
        }
        double dv = 0.0;
        auto [ptr, ec] = std::from_chars(m_src.data() + start, m_src.data() + m_pos, dv);
        if (ec != std::errc{} || ptr != m_src.data() + m_pos || !std::isfinite(dv)) {
            throw std::runtime_error("Invalid number in Lua config");
        }
        return config_value::make_float(std::move(key), static_cast<float>(dv));
    }

    config_value parse_value(std::string key) {
        char c = peek();
        if (c == '"') return config_value::make_string(std::move(key), parse_string());
        if (c == '{') return parse_table(std::move(key));
        if (std::isdigit(static_cast<unsigned char>(c)) || c == '-' || c == '+') {
            return parse_number(std::move(key));
        }
        std::string id = parse_ident();
        if (id == "true") return config_value::make_bool(std::move(key), true);
        if (id == "false") return config_value::make_bool(std::move(key), false);
        if (id == "float2" || id == "float3" || id == "float4") {
            expect('(');
            std::array<float, 4> vec{};
            std::size_t count = (id == "float2") ? 2 : (id == "float3") ? 3 : 4;
            for (std::size_t i = 0; i < count; ++i) {
                vec[i] = static_cast<float>(parse_number({}).number);
                if (i + 1 < count) expect(',');
            }
            expect(')');
            if (count == 2) return config_value::make_float2(std::move(key), {vec[0], vec[1]});
            if (count == 3) return config_value::make_float3(std::move(key), {vec[0], vec[1], vec[2]});
            return config_value::make_float4(std::move(key), vec[0], vec[1], vec[2], vec[3]);
        }
        throw std::runtime_error("Unsupported Lua identifier: " + id);
    }

    config_value parse_table(std::string key) {
        expect('{');
        config_value tbl = config_value::make_table(std::move(key), false);
        bool saw_named = false;
        bool saw_indexed = false;
        while (peek() != '}') {
            char c = peek();
            if (c == '[' || std::isalpha(static_cast<unsigned char>(c)) || c == '_') {
                std::size_t saved = m_pos;
                bool is_assignment = false;
                if (c == '[') {
                    is_assignment = true;
                } else {
                    std::string maybe = parse_ident();
                    if (peek() == '=' ) is_assignment = true;
                    m_pos = saved;
                }
                if (is_assignment) {
                    saw_named = true;
                    std::string k = parse_key();
                    expect('=');
                    tbl.children.push_back(parse_value(std::move(k)));
                } else {
                    saw_indexed = true;
                    tbl.children.push_back(parse_value({}));
                }
            } else {
                saw_indexed = true;
                tbl.children.push_back(parse_value({}));
            }
            if (peek() == ',') {
                ++m_pos;
            }
        }
        expect('}');
        if (saw_indexed && !saw_named) {
            tbl.kind = config_value::kind_t::table_indexed;
        }
        return tbl;
    }
};

} // namespace

std::string serialize_lua_config(config_value const& root) {
    std::ostringstream os;
    std::vector<config_value const*> ordered;
    ordered.reserve(root.children.size());
    for (auto const& c : root.children) ordered.push_back(&c);
    std::sort(ordered.begin(), ordered.end(), [](auto const* a, auto const* b) {
        return a->name < b->name;
    });
    for (auto const* entry : ordered) {
        write_lua_entry(os, *entry, 0, true);
    }
    return os.str();
}

config_value parse_lua_config(std::string_view text) {
    return lua_parser(text).parse_root();
}

std::vector<std::uint8_t> serialize_binary_config(config_value root) {
    struct disk_entry {
        std::uint64_t id;
        std::uint64_t data;
        std::uint32_t crc;
        std::uint16_t type;
        std::uint16_t count;
    };
    static_assert(sizeof(disk_entry) == 24);

    std::vector<disk_entry> fat(1);
    std::vector<std::uint8_t> blob;

    auto emit = [&](auto&& self, config_value n, std::size_t offset, unsigned depth) -> void {
        if (depth > 64 || fat.size() > 500000) {
            throw std::runtime_error("Binary config exceeds safety bounds");
        }
        disk_entry v{};
        v.type = static_cast<std::uint16_t>(n.kind);
        if (!n.name.empty()) {
            v.id = blob.size() + 1;
            v.crc = crc32_ieee(n.name);
            blob.insert(blob.end(), n.name.begin(), n.name.end());
            blob.push_back(0);
        }
        if (n.kind == config_value::kind_t::table_named ||
            n.kind == config_value::kind_t::table_indexed) {
            if (n.children.size() > 65535) {
                throw std::runtime_error("Binary config table exceeds u16 child count");
            }
            if (n.kind == config_value::kind_t::table_named) {
                std::sort(n.children.begin(), n.children.end(), [](auto const& a, auto const& b) {
                    return crc32_ieee(a.name) < crc32_ieee(b.name);
                });
            }
            std::size_t first = fat.size();
            v.data = first * sizeof(disk_entry);
            v.count = static_cast<std::uint16_t>(n.children.size());
            fat.resize(first + n.children.size());
            for (std::size_t i = 0; i < n.children.size(); ++i) {
                self(self, std::move(n.children[i]), first + i, depth + 1);
            }
        } else if (n.kind == config_value::kind_t::boolean ||
                   n.kind == config_value::kind_t::integer) {
            v.count = 4;
            v.data = static_cast<std::uint32_t>(static_cast<std::int32_t>(n.number));
        } else if (n.kind == config_value::kind_t::number) {
            v.count = 4;
            float f = static_cast<float>(n.number);
            std::uint32_t bits = 0;
            std::memcpy(&bits, &f, 4);
            v.data = bits;
        } else if (n.kind == config_value::kind_t::text) {
            v.data = blob.size();
            blob.insert(blob.end(), n.str.begin(), n.str.end());
            blob.push_back(0);
            std::size_t sz = blob.size() - v.data;
            if (sz > 65535) throw std::runtime_error("Binary config string exceeds 64KiB");
            v.count = static_cast<std::uint16_t>(sz);
        } else {
            v.data = blob.size();
            std::size_t comps = (n.kind == config_value::kind_t::vec2) ? 2 :
                                (n.kind == config_value::kind_t::vec3) ? 3 : 4;
            auto const* p = reinterpret_cast<std::uint8_t const*>(n.vec.data());
            blob.insert(blob.end(), p, p + comps * 4);
            v.count = static_cast<std::uint16_t>(comps * 4);
        }
        fat[offset] = v;
    };

    emit(emit, std::move(root), 0, 0);
    std::size_t fat_bytes = fat.size() * sizeof(disk_entry);
    for (auto& v : fat) {
        if (v.id) v.id += fat_bytes - 1;
        if (v.type > 4) v.data += fat_bytes;
    }
    auto const* fp = reinterpret_cast<std::uint8_t const*>(fat.data());
    std::vector<std::uint8_t> out(fp, fp + fat_bytes);
    out.insert(out.end(), blob.begin(), blob.end());
    return out;
}

std::pair<std::uint32_t, std::uint32_t> validate_surface_mesh_headers(
    std::vector<std::uint8_t> const& vertices,
    std::vector<std::uint8_t> const& indices,
    std::size_t vertex_stride
) {
    if (vertices.size() < 4 || indices.size() < 4) {
        throw std::runtime_error("Truncated mesh vertices or indices");
    }
    std::uint32_t count = read_le<std::uint32_t>(vertices, 0);
    std::uint32_t n = read_le<std::uint32_t>(indices, 0);
    if (!count || count > 65536 || vertices.size() != 4u + std::size_t(count) * vertex_stride ||
        !n || n > 3000000 || n % 3 != 0 || indices.size() != 4u + std::size_t(n) * 2u) {
        throw std::runtime_error("Invalid mesh buffer sizes for vulkan conversion");
    }
    for (std::uint32_t i = 0; i < n; ++i) {
        if (read_le<std::uint16_t>(indices, 4 + std::size_t(i) * 2) >= count) {
            throw std::runtime_error("Mesh index exceeds vertex count");
        }
    }
    return {count, n};
}

std::vector<std::uint8_t> convert_static_surface_to_vulkan_blob(
    std::vector<std::uint8_t> const& vertices,
    std::vector<std::uint8_t> const& indices
) {
    auto const [count, n] = validate_surface_mesh_headers(vertices, indices, 56u);
    (void)n;

    static constexpr std::uint8_t declaration[] = {
        0,0,0,0,2,0,0,0, 0,0,12,0,4,0,3,0, 0,0,16,0,4,0,6,0,
        0,0,20,0,4,0,7,0, 0,0,24,0,1,0,5,0, 255,0,0,0,17,0,0,0
    };

    std::vector<std::uint8_t> result;
    result.reserve(8 + sizeof(declaration) + 4 + count * 32 + 8 + indices.size());

    append_le<std::uint32_t>(result, 3u); // model_chunk_vertices
    append_le<std::uint32_t>(result, static_cast<std::uint32_t>(sizeof(declaration) + 4 + count * 32));
    result.insert(result.end(), std::begin(declaration), std::end(declaration));
    append_le<std::uint32_t>(result, count);

    auto uv_alpha = [](float uv) -> std::uint8_t {
        double scaled = double(uv) * 1024.0;
        double primary = std::clamp(std::floor(scaled), -32768.0, 32767.0);
        return static_cast<std::uint8_t>(std::clamp(std::floor(255.5 * (scaled - primary)), 0.0, 255.0));
    };

    for (std::uint32_t i = 0; i < count; ++i) {
        std::size_t offset = 4 + std::size_t(i) * 56u;
        std::array<float, 14> values{};
        std::memcpy(values.data(), vertices.data() + offset, 56);
        for (float f : values) {
            if (!std::isfinite(f)) throw std::runtime_error("Non-finite static vertex");
        }
        result.insert(result.end(), vertices.data() + offset, vertices.data() + offset + 12);
        for (unsigned basis = 0; basis < 3; ++basis) {
            float const* v = values.data() + 3 + basis * 3;
            double len = std::sqrt(double(v[0]) * v[0] + double(v[1]) * v[1] + double(v[2]) * v[2]);
            if (!std::isfinite(len) || len <= 1e-12) {
                throw std::runtime_error("Degenerate static vertex basis");
            }
            std::uint8_t packed[4];
            for (unsigned c = 0; c < 3; ++c) {
                packed[c] = static_cast<std::uint8_t>(std::clamp(std::floor((v[2 - c] / len + 1.0) * 127.5), 0.0, 255.0));
            }
            packed[3] = (basis == 0) ? 127 : uv_alpha(values[11 + basis]);
            result.insert(result.end(), packed, packed + 4);
        }
        result.insert(result.end(), vertices.data() + offset + 48, vertices.data() + offset + 56);
    }

    append_le<std::uint32_t>(result, 4u); // model_chunk_indices
    append_le<std::uint32_t>(result, static_cast<std::uint32_t>(indices.size()));
    result.insert(result.end(), indices.begin(), indices.end());
    return result;
}

std::vector<std::uint8_t> convert_skeletal_surface_to_vulkan_blob(
    std::vector<std::uint8_t> const& raw_vertices,
    std::vector<std::uint8_t> const& raw_indices
) {
    (void)validate_surface_mesh_headers(raw_vertices, raw_indices, 76u);

    std::vector<std::uint8_t> result;
    result.reserve(16 + raw_vertices.size() + raw_indices.size());
    append_le<std::uint32_t>(result, 3u); // model_chunk_vertices
    append_le<std::uint32_t>(result, static_cast<std::uint32_t>(raw_vertices.size()));
    result.insert(result.end(), raw_vertices.begin(), raw_vertices.end());
    append_le<std::uint32_t>(result, 4u); // model_chunk_indices
    append_le<std::uint32_t>(result, static_cast<std::uint32_t>(raw_indices.size()));
    result.insert(result.end(), raw_indices.begin(), raw_indices.end());
    return result;
}

} // namespace xray::maya_cook::detail
