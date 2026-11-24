#define XRAY_LOG_MODULE_INITIATOR "test_math_and_binary_config_contract"
#include "test_check.hpp"

#include <xray/extensions.h>
#include <xray/configs_checked_binary.h>
#include <xray/crc32.h>
#include <xray/memory_crt_allocator.h>

#include <algorithm>
#include <array>
#include <bit>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <iomanip>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

template <typename Fn>
void expect_throw(Fn&& fn, char const* label) {
    bool threw = false;
    try {
        fn();
    } catch (std::exception const&) {
        threw = true;
    }
    if (!threw) {
        std::cerr << "Expected std::exception for: " << label << '\n';
        std::abort();
    }
}

struct raw_disk_value {
    std::uint64_t id;
    std::uint64_t data;
    std::uint32_t crc;
    std::uint16_t type;
    std::uint16_t count;
};
static_assert(sizeof(raw_disk_value) == 24);
static_assert(sizeof(raw_disk_value) == sizeof(xray::configs::binary_config_value));

std::uint32_t calc_crc32(char const* s) {
    xray::crc32_type crc;
    crc.process_block(s, s + std::strlen(s));
    return crc.checksum();
}

struct kv_node {
    std::string key;
    std::uint32_t crc = 0;
    std::uint16_t type = 0;
    std::uint32_t scalar_bits = 0;
    std::vector<std::uint8_t> blob;
    std::vector<kv_node> children;
};

// Serializes a checked-compatible Windows 64-bit binary config tree.
std::vector<std::uint8_t> build_binary_config_blob() {
    using namespace xray::configs;

    auto make_scalar = [](char const* k, std::uint16_t t, std::uint32_t bits) {
        kv_node n;
        n.key = k ? k : "";
        n.crc = k ? calc_crc32(k) : 0u;
        n.type = t;
        n.scalar_bits = bits;
        return n;
    };
    auto make_blob = [](char const* k, std::uint16_t t, std::vector<std::uint8_t> b) {
        kv_node n;
        n.key = k ? k : "";
        n.crc = k ? calc_crc32(k) : 0u;
        n.type = t;
        n.blob = std::move(b);
        return n;
    };
    auto pack_floats = [](std::initializer_list<float> fs) {
        std::vector<std::uint8_t> b(fs.size() * 4);
        std::size_t idx = 0;
        for (float f : fs) {
            std::memcpy(b.data() + idx * 4, &f, 4);
            ++idx;
        }
        return b;
    };

    kv_node nested_arr;
    nested_arr.key = "arr";
    nested_arr.crc = calc_crc32("arr");
    nested_arr.type = t_table_indexed;
    nested_arr.children.push_back(make_scalar(nullptr, t_integer, 100u));
    nested_arr.children.push_back(make_scalar(nullptr, t_float, std::bit_cast<std::uint32_t>(-2.5f)));
    nested_arr.children.push_back(make_scalar(nullptr, t_boolean, 1u));

    kv_node root;
    root.type = t_table_named;
    root.children.push_back(make_scalar("b_false", t_boolean, 0u));
    root.children.push_back(make_scalar("b_true", t_boolean, 1u));
    root.children.push_back(make_scalar("i_neg", t_integer, static_cast<std::uint32_t>(s32(-123456))));
    root.children.push_back(make_scalar("i_pos", t_integer, 42u));
    root.children.push_back(make_scalar("f_neg_zero", t_float, 0x80000000u));
    root.children.push_back(make_scalar("f_frac", t_float, std::bit_cast<std::uint32_t>(-3.25f)));
    root.children.push_back(make_scalar("f_small", t_float, std::bit_cast<std::uint32_t>(1e-6f)));
    root.children.push_back(make_blob("str_val", t_string, {'x', 'r', 'a', 'y', '-', '0', '3', '\0'}));
    root.children.push_back(make_blob("v2", t_float2, pack_floats({1.5f, -2.5f})));
    root.children.push_back(make_blob("v3", t_float3, pack_floats({0.25f, 0.5f, -0.75f})));
    root.children.push_back(make_blob("v4", t_float4, pack_floats({1.f, 2.f, 3.f, -4.f})));
    root.children.push_back(std::move(nested_arr));

    auto sort_named = [](auto&& self, kv_node& node) -> void {
        if (node.type == t_table_named) {
            std::sort(node.children.begin(), node.children.end(), [](kv_node const& a, kv_node const& b) {
                return a.crc < b.crc;
            });
        }
        for (auto& c : node.children) {
            self(self, c);
        }
    };
    sort_named(sort_named, root);

    // Flatten FAT nodes in BFS order (matching binary_config & checked_config contiguous FAT area)
    std::vector<raw_disk_value> fat;
    std::vector<kv_node const*> flat_nodes;
    fat.push_back(raw_disk_value{0, 0, 0, root.type, static_cast<std::uint16_t>(root.children.size())});
    flat_nodes.push_back(&root);

    std::size_t q = 0;
    while (q < flat_nodes.size()) {
        kv_node const* cur = flat_nodes[q];
        if (!cur->children.empty()) {
            std::size_t const first_child_idx = fat.size();
            fat[q].data = first_child_idx * sizeof(raw_disk_value);
            for (kv_node const& child : cur->children) {
                raw_disk_value dv{};
                dv.crc = child.crc;
                dv.type = child.type;
                if (child.type == t_table_named || child.type == t_table_indexed) {
                    dv.count = static_cast<std::uint16_t>(child.children.size());
                } else if (child.type == t_boolean || child.type == t_integer || child.type == t_float) {
                    dv.data = child.scalar_bits;
                    dv.count = 4;
                } else {
                    dv.count = static_cast<std::uint16_t>(child.blob.size());
                }
                fat.push_back(dv);
                flat_nodes.push_back(&child);
            }
        }
        ++q;
    }

    std::size_t const fat_bytes = fat.size() * sizeof(raw_disk_value);
    std::vector<std::uint8_t> out(fat_bytes, 0);
    auto append_blob = [&](std::vector<std::uint8_t> const& b) -> std::uint64_t {
        std::uint64_t off = out.size();
        out.insert(out.end(), b.begin(), b.end());
        return off;
    };
    auto append_str = [&](std::string const& s) -> std::uint64_t {
        std::uint64_t off = out.size();
        out.insert(out.end(), s.begin(), s.end());
        out.push_back('\0');
        return off;
    };

    for (std::size_t i = 0; i < flat_nodes.size(); ++i) {
        kv_node const* n = flat_nodes[i];
        if (!n->key.empty()) {
            fat[i].id = append_str(n->key);
        }
        if (!n->blob.empty()) {
            fat[i].data = append_blob(n->blob);
        }
    }

    std::memcpy(out.data(), fat.data(), fat_bytes);
    return out;
}

std::uint64_t fnv1a_mix(std::uint64_t h, void const* ptr, std::size_t n) {
    auto const* p = static_cast<std::uint8_t const*>(ptr);
    for (std::size_t i = 0; i < n; ++i) {
        h ^= p[i];
        h *= 1099511628211ull;
    }
    return h;
}

void test_math_edge_cases_and_benchmark() {
    float const flt_min_norm = std::numeric_limits<float>::min();
    float const samples[] = {
        +0.0f, -0.0f,
        flt_min_norm, -flt_min_norm,
        1e-30f, -1e-30f,
        1e-6f, -1e-6f,
        0.25f, -0.25f,
        0.5f, -0.5f,
        0.99999994f, -0.99999994f,
        1.0f, -1.0f,
        1.0000001f, -1.0000001f,
        1.5f, -1.5f,
        2.0f, -2.0f,
        123.75f, -123.75f,
        16777215.0f, -16777215.0f,
        16777216.0f, -16777216.0f,
        1e8f, -1e8f,
    };

    for (float v : samples) {
        float const abs_val = xray::math::abs(v);
        std::uint32_t const expected_abs_bits = std::bit_cast<std::uint32_t>(v) & 0x7fffffffu;
        XRAY_TEST_CHECK(std::bit_cast<std::uint32_t>(abs_val) == expected_abs_bits);

        bool const expected_neg = (std::bit_cast<std::uint32_t>(v) & 0x80000000u) != 0;
        XRAY_TEST_CHECK(xray::math::negative(v) == expected_neg);

        int const expected_floor = static_cast<int>(std::floor(v));
        int const expected_ceil = static_cast<int>(std::ceil(v));
        XRAY_TEST_CHECK(xray::math::floor(v) == expected_floor);
        XRAY_TEST_CHECK(xray::math::ceil(v) == expected_ceil);
    }

    // Subnormal handling: valid() in non-MASTER_GOLD flushes subnormals to +0.0f cleanly without shift UB.
    float const denorm = std::numeric_limits<float>::denorm_min();
    for (float sv : {denorm, -denorm}) {
        float const abs_sv = xray::math::abs(sv);
        XRAY_TEST_CHECK(abs_sv == 0.0f || std::bit_cast<std::uint32_t>(abs_sv) == std::bit_cast<std::uint32_t>(denorm));
        int const fl = xray::math::floor(sv);
        int const cl = xray::math::ceil(sv);
        XRAY_TEST_CHECK(fl == -1 || fl == 0);
        XRAY_TEST_CHECK(cl == 0 || cl == 1);
    }

    // s64 abs checks (previously truncated 64-bit shift mask into 32-bit int)
    XRAY_TEST_CHECK(xray::math::abs(s64(0)) == 0);
    XRAY_TEST_CHECK(xray::math::abs(s64(1)) == 1);
    XRAY_TEST_CHECK(xray::math::abs(s64(-1)) == 1);
    XRAY_TEST_CHECK(xray::math::abs(-(s64(1) << 40)) == (s64(1) << 40));
    XRAY_TEST_CHECK(xray::math::abs(std::numeric_limits<s64>::max()) == std::numeric_limits<s64>::max());
    XRAY_TEST_CHECK(xray::math::abs(-std::numeric_limits<s64>::max()) == std::numeric_limits<s64>::max());

    // Micro-benchmark for math::abs, floor, ceil throughput
    constexpr std::size_t kVecLen = 4096;
    constexpr int kRounds = 500; // 2,048,000 elements
    std::array<float, kVecLen> inputs{};
    for (std::size_t i = 0; i < kVecLen; ++i) {
        inputs[i] = (static_cast<float>(i) - 2048.0f) * 0.0137f;
    }

    auto const t0 = std::chrono::steady_clock::now();
    std::int64_t sink_int = 0;
    float sink_float = 0.0f;
    for (int r = 0; r < kRounds; ++r) {
        for (std::size_t i = 0; i < kVecLen; ++i) {
            float const x = inputs[i];
            sink_float += xray::math::abs(x);
            sink_int += xray::math::floor(x) + xray::math::ceil(x);
        }
    }
    auto const t1 = std::chrono::steady_clock::now();
    double const ms = std::chrono::duration<double, std::milli>(t1 - t0).count();
    double const total_ops = static_cast<double>(kVecLen) * static_cast<double>(kRounds) * 3.0;
    double const mops_per_s = (ms > 0.0) ? (total_ops / (ms * 1000.0)) : 0.0;

    XRAY_TEST_CHECK(sink_float > 0.0f);
    std::cout << "BENCH math(abs+floor+ceil): " << std::fixed << std::setprecision(3)
              << ms << " ms (" << mops_per_s << " M ops/s), sink=" << sink_int << '\n';
}

void test_checked_binary_config() {
    auto blob = build_binary_config_blob();
    std::vector<std::byte> byte_span_copy(blob.size());
    std::memcpy(byte_span_copy.data(), blob.data(), blob.size());

    xray::memory::crt_allocator alloc;
    xray::configs::checked::checked_config cfg_span(std::span<std::byte const>(byte_span_copy), alloc);
    xray::configs::checked::checked_config cfg_buf(xray::const_buffer(blob.data(), blob.size()), alloc);

    for (auto const* cfg : {&cfg_span, &cfg_buf}) {
        auto const& root = cfg->root();
        XRAY_TEST_CHECK(bool(root["b_false"]) == false);
        XRAY_TEST_CHECK(bool(root["b_true"]) == true);
        XRAY_TEST_CHECK(s32(root["i_neg"]) == -123456);
        XRAY_TEST_CHECK(u32(root["i_pos"]) == 42u);

        float const neg_zero = float(root["f_neg_zero"]);
        XRAY_TEST_CHECK(std::bit_cast<std::uint32_t>(neg_zero) == 0x80000000u);
        XRAY_TEST_CHECK(float(root["f_frac"]) == -3.25f);
        XRAY_TEST_CHECK(float(root["f_small"]) == 1e-6f);
        XRAY_TEST_CHECK(std::string(pcstr(root["str_val"])) == "xray-03");

        auto const& v2 = static_cast<xray::math::float2 const&>(root["v2"]);
        XRAY_TEST_CHECK(v2.x == 1.5f && v2.y == -2.5f);

        auto const& v3 = static_cast<xray::math::float3 const&>(root["v3"]);
        XRAY_TEST_CHECK(v3.x == 0.25f && v3.y == 0.5f && v3.z == -0.75f);

        auto const& v4 = static_cast<xray::math::float4 const&>(root["v4"]);
        XRAY_TEST_CHECK(v4.x == 1.f && v4.y == 2.f && v4.z == 3.f && v4.w == -4.f);

        auto const& arr = root["arr"];
        XRAY_TEST_CHECK(arr.size() == 3);
        XRAY_TEST_CHECK(s32(arr[0]) == 100);
        XRAY_TEST_CHECK(float(arr[1]) == -2.5f);
        XRAY_TEST_CHECK(bool(arr[2]) == true);
    }

    // Compute deterministic decoded digest for Debug vs Release comparison
    auto const& root = cfg_span.root();
    std::uint64_t digest = 14695981039346656037ull;
    bool b0 = bool(root["b_false"]);
    bool b1 = bool(root["b_true"]);
    s32 i0 = s32(root["i_neg"]);
    u32 u0 = u32(root["i_pos"]);
    float f0 = float(root["f_neg_zero"]);
    float f1 = float(root["f_frac"]);
    float f2 = float(root["f_small"]);
    pcstr s0 = pcstr(root["str_val"]);
    digest = fnv1a_mix(digest, &b0, sizeof(b0));
    digest = fnv1a_mix(digest, &b1, sizeof(b1));
    digest = fnv1a_mix(digest, &i0, sizeof(i0));
    digest = fnv1a_mix(digest, &u0, sizeof(u0));
    digest = fnv1a_mix(digest, &f0, sizeof(f0));
    digest = fnv1a_mix(digest, &f1, sizeof(f1));
    digest = fnv1a_mix(digest, &f2, sizeof(f2));
    digest = fnv1a_mix(digest, s0, std::strlen(s0));
    std::cout << "DIGEST checked_config: 0x" << std::hex << digest << std::dec << '\n';

    // Negative controls on bounded span validator
    expect_throw([&]() {
        xray::configs::checked::checked_config bad(std::span<std::byte const>{}, alloc);
    }, "empty span");

    expect_throw([&]() {
        xray::configs::checked::checked_config bad(
            std::span<std::byte const>(byte_span_copy).first(16), alloc);
    }, "truncated header");

    {
        auto corrupted = byte_span_copy;
        // Corrupt root's child array offset to be unaligned
        raw_disk_value root_dv{};
        std::memcpy(&root_dv, corrupted.data(), sizeof(root_dv));
        root_dv.data += 1;
        std::memcpy(corrupted.data(), &root_dv, sizeof(root_dv));
        expect_throw([&]() {
            xray::configs::checked::checked_config bad(corrupted, alloc);
        }, "unaligned child offset");
    }

    {
        auto corrupted = byte_span_copy;
        // First child is sorted; corrupt a boolean node to invalid scalar > 1
        std::size_t const node_count = 13; // root + 12 keys
        bool patched = false;
        for (std::size_t i = 0; i < node_count; ++i) {
            raw_disk_value dv{};
            std::memcpy(&dv, corrupted.data() + i * sizeof(dv), sizeof(dv));
            if (dv.type == xray::configs::t_boolean) {
                dv.data = 2u;
                std::memcpy(corrupted.data() + i * sizeof(dv), &dv, sizeof(dv));
                patched = true;
                break;
            }
        }
        XRAY_TEST_CHECK(patched);
        expect_throw([&]() {
            xray::configs::checked::checked_config bad(corrupted, alloc);
        }, "invalid boolean > 1");
    }

    {
        auto corrupted = byte_span_copy;
        // Point a string value inside FAT node area to trigger node aliasing error
        bool patched = false;
        for (std::size_t i = 0; i < 16; ++i) {
            raw_disk_value dv{};
            std::memcpy(&dv, corrupted.data() + i * sizeof(dv), sizeof(dv));
            if (dv.type == xray::configs::t_string) {
                dv.data = 0;
                dv.count = 1;
                std::memcpy(corrupted.data() + i * sizeof(dv), &dv, sizeof(dv));
                patched = true;
                break;
            }
        }
        XRAY_TEST_CHECK(patched);
        expect_throw([&]() {
            xray::configs::checked::checked_config bad(corrupted, alloc);
        }, "payload aliases node area");
    }
}

} // namespace

int main() {
    test_math_edge_cases_and_benchmark();
    test_checked_binary_config();
    std::cout << "PASS: xray_math_and_binary_config_contract\n";
    return 0;
}
