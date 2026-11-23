#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <limits>
#include <span>
#include <stdexcept>
#include <string>
#include <vector>

namespace xray::maya_cook::detail {

inline constexpr float epsilon_3 = 0.001f;
inline constexpr float epsilon_5 = 0.00001f;

struct float2 {
    float x = 0.0f;
    float y = 0.0f;

    float2() = default;
    constexpr float2(float x_, float y_) : x(x_), y(y_) {}

    constexpr float2 operator+(float2 const& r) const { return {x + r.x, y + r.y}; }
    constexpr float2 operator-(float2 const& r) const { return {x - r.x, y - r.y}; }
    constexpr float2 operator*(float s) const { return {x * s, y * s}; }

    bool is_similar(float2 const& r, float eps = epsilon_5) const {
        return std::abs(x - r.x) < eps && std::abs(y - r.y) < eps;
    }
    bool is_finite() const { return std::isfinite(x) && std::isfinite(y); }
};

struct float3 {
    float x = 0.0f;
    float y = 0.0f;
    float z = 0.0f;

    float3() = default;
    constexpr float3(float x_, float y_, float z_) : x(x_), y(y_), z(z_) {}

    constexpr float3 operator+(float3 const& r) const { return {x + r.x, y + r.y, z + r.z}; }
    constexpr float3 operator-(float3 const& r) const { return {x - r.x, y - r.y, z - r.z}; }
    constexpr float3 operator-() const { return {-x, -y, -z}; }
    constexpr float3 operator*(float s) const { return {x * s, y * s, z * s}; }
    constexpr float3 operator/(float s) const { return {x / s, y / s, z / s}; }
    float3& operator+=(float3 const& r) { x += r.x; y += r.y; z += r.z; return *this; }
    float3& operator-=(float3 const& r) { x -= r.x; y -= r.y; z -= r.z; return *this; }
    float3& operator*=(float s) { x *= s; y *= s; z *= s; return *this; }

    constexpr float dot(float3 const& r) const { return x * r.x + y * r.y + z * r.z; }
    constexpr float3 cross(float3 const& r) const {
        return {y * r.z - z * r.y, z * r.x - x * r.z, x * r.y - y * r.x};
    }
    constexpr float squared_length() const { return x * x + y * y + z * z; }
    float length() const { return std::sqrt(squared_length()); }

    bool is_similar(float3 const& r, float eps = epsilon_5) const {
        return std::abs(x - r.x) < eps && std::abs(y - r.y) < eps && std::abs(z - r.z) < eps;
    }
    bool is_finite() const {
        return std::isfinite(x) && std::isfinite(y) && std::isfinite(z);
    }

    float3 normalized_safe(float3 const& fallback = {0.0f, 1.0f, 0.0f}) const {
        float const len = length();
        if (!(len > 1e-12f) || !std::isfinite(len)) {
            return fallback;
        }
        return *this / len;
    }
};

struct aabb {
    float3 min = {
        std::numeric_limits<float>::max(),
        std::numeric_limits<float>::max(),
        std::numeric_limits<float>::max(),
    };
    float3 max = {
        std::numeric_limits<float>::lowest(),
        std::numeric_limits<float>::lowest(),
        std::numeric_limits<float>::lowest(),
    };
    bool initialized = false;

    void modify(float3 const& p) {
        if (!initialized) {
            min = max = p;
            initialized = true;
            return;
        }
        min.x = std::min(min.x, p.x);
        min.y = std::min(min.y, p.y);
        min.z = std::min(min.z, p.z);
        max.x = std::max(max.x, p.x);
        max.y = std::max(max.y, p.y);
        max.z = std::max(max.z, p.z);
    }

    void modify(aabb const& other) {
        if (!other.initialized) {
            return;
        }
        modify(other.min);
        modify(other.max);
    }

    static aabb identity() {
        aabb r;
        r.min = {-0.5f, -0.5f, -0.5f};
        r.max = {+0.5f, +0.5f, +0.5f};
        r.initialized = true;
        return r;
    }

    float3 center() const { return min + (max - min) * 0.5f; }

    float sphere_radius() const {
        float3 const c = center();
        return (c - min).length();
    }
};

inline std::uint32_t crc32_ieee(std::string_view data) {
    std::uint32_t crc = 0xFFFFFFFFu;
    for (unsigned char b : data) {
        crc ^= b;
        for (int bit = 0; bit < 8; ++bit) {
            crc = (crc & 1u) ? ((crc >> 1) ^ 0xEDB88320u) : (crc >> 1);
        }
    }
    return ~crc;
}

template <typename T>
inline T read_le(std::span<std::uint8_t const> buf, std::size_t offset) {
    if (offset > buf.size() || sizeof(T) > buf.size() - offset) {
        throw std::runtime_error("Binary buffer read out of bounds");
    }
    T val{};
    std::memcpy(&val, buf.data() + offset, sizeof(T));
    return val;
}

template <typename T>
inline void append_le(std::vector<std::uint8_t>& out, T const& value) {
    auto const* bytes = reinterpret_cast<std::uint8_t const*>(&value);
    out.insert(out.end(), bytes, bytes + sizeof(T));
}

} // namespace xray::maya_cook::detail

