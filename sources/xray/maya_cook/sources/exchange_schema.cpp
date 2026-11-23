#include "exchange_schema.hpp"
#include "model_cook_common.hpp"

#include <initializer_list>
#include <limits>
#include <set>

namespace xray::maya_cook::detail {
namespace {

std::string const& enum_value(json_value const& value, std::initializer_list<std::string_view> options,
                              std::string_view label) {
    auto const& text = value.as_string();
    for (auto option : options) if (text == option) return text;
    throw std::runtime_error("Invalid " + std::string(label) + ": " + text);
}

double finite_number(json_value const& value, char const* label) {
    double const number = value.as_double();
    if (!std::isfinite(number)) throw std::runtime_error(std::string(label) + " must be finite");
    return number;
}

void validate_curves(json_value const& curves, json_value const& sampled_tracks) {
    if (curves.require("time_unit").as_string() != "frames")
        throw std::runtime_error("curves.time_unit must be 'frames'");
    std::set<std::string> sampled_bones, seen;
    for (auto const& track : sampled_tracks.as_array())
        sampled_bones.insert(track.require("bone_name").as_string());
    auto const& tracks = curves.require("tracks").as_array();
    if (tracks.size() > 128) throw std::runtime_error("curves.tracks exceeds 128 bones");
    std::size_t total_keys = 0;
    for (auto const& track : tracks) {
        auto const& bone = track.require("bone_name").as_string();
        validate_safe_segment(bone, "curves.bone_name", 127);
        if (!sampled_bones.contains(bone)) throw std::runtime_error("curves bone has no sampled track: " + bone);
        if (!seen.insert(bone).second) throw std::runtime_error("Duplicate curves bone: " + bone);
        enum_value(track.require("rotation_order"), {"XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX"}, "curves.rotation_order");
        auto const& channels = track.require("channels").as_object();
        if (channels.empty() || channels.size() > 9) throw std::runtime_error("curves.channels must contain 1..9 channels");
        for (auto const& [name, curve] : channels) {
            if (name != "tx" && name != "ty" && name != "tz" && name != "rx" && name != "ry" &&
                name != "rz" && name != "sx" && name != "sy" && name != "sz")
                throw std::runtime_error("Unsupported curves channel: " + name);
            for (auto infinity : {"pre_infinity", "post_infinity"})
                if (auto const* value = curve.find(infinity))
                    enum_value(*value, {"CONSTANT", "LINEAR", "CYCLE", "CYCLE_RELATIVE", "OSCILLATE"}, infinity);
            auto const& keys = curve.require("keys").as_array();
            total_keys += keys.size();
            if (keys.empty() || total_keys > 1000000) throw std::runtime_error("curves keys must be non-empty and total <= 1000000");
            double previous = -std::numeric_limits<double>::infinity();
            for (auto const& key : keys) {
                double const time = finite_number(key.require("time"), "curves key time");
                if (!(time > previous)) throw std::runtime_error("curves keys times must strictly increase");
                previous = time;
                finite_number(key.require("value"), "curves key value");
                auto const& interpolation = enum_value(key.require("interpolation"), {"CONSTANT", "LINEAR", "BEZIER"}, "curves interpolation");
                for (auto handle : {"handle_left", "handle_right"}) {
                    auto const* node = key.find(handle);
                    if (!node && interpolation != "BEZIER") continue;
                    if (!node) throw std::runtime_error("BEZIER key requires both handles");
                    auto const& pair = node->as_array();
                    if (pair.size() != 2) throw std::runtime_error("Curve handle must be [time, value]");
                    double const handle_time = finite_number(pair[0], "curve handle time");
                    finite_number(pair[1], "curve handle value");
                    if ((std::string_view(handle) == "handle_left" && handle_time > time) ||
                        (std::string_view(handle) == "handle_right" && handle_time < time))
                        throw std::runtime_error("Curve handle is on the wrong side of the key");
                }
            }
        }
    }
}

} // namespace

void validate_exchange_header(json_value const& manifest) {
    auto const& schema = enum_value(manifest.require("schema"), {"xray-maya-exchange/v1", "xray-dcc-exchange/v2"}, "exchange schema");
    (void)parse_coord_transform(manifest); // Also validate coordinate declarations for skeletons.
    if (schema != "xray-dcc-exchange/v2") return;
    enum_value(manifest.require("source_dcc"), {"blender", "maya", "legacy_clip", "legacy_ma", "legacy_mb", "legacy_asset"}, "source_dcc");
    auto const& kind = manifest.require("asset_kind").as_string();
    if (auto const* start = manifest.find("frame_start")) {
        if (kind != "animation") throw std::runtime_error("frame_start is only valid for animation");
        finite_number(*start, "frame_start");
    }
    if (auto const* relative = manifest.find("root_already_relative")) (void)relative->as_bool();
    if (auto const* curves = manifest.find("curves")) {
        if (kind != "animation") throw std::runtime_error("curves are only valid for animation");
        validate_curves(*curves, manifest.require("tracks"));
    }
}

} // namespace xray::maya_cook::detail
