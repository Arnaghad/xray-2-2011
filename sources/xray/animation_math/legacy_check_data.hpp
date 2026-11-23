#pragma once

#include "discrete_data.hpp"

namespace xray::animation_math {

// ConfigValue supplies value_exists(name), operator[](name/index), size() and
// checked conversion to float. This supports engine Lua and read-only test
// adapters, without coupling the mathematical core to a Lua implementation.
template<typename ConfigValue>
discrete_data load_legacy_check_data(ConfigValue const& input) {
    auto const root = input.value_exists("discrete_data") ? input["discrete_data"] : input;
    discrete_data result;
    auto const precisions = root["precision"];
    if (precisions.size() != channel_count) throw std::invalid_argument("Check data requires nine channel precisions");
    for (unsigned c = 0; c < channel_count; ++c) result.precisions[c] = float(precisions[c]);
    float const fps = float(root["time_scale"]);
    if (!(fps > 0.0f) || !std::isfinite(fps)) throw std::invalid_argument("Check data time_scale must be positive finite fps");
    result.sample_interval = 30.0f / fps;
    auto const bones = root["bones"];
    if (bones.value_exists("header")) {
        auto const header = bones["header"];
        if (float(header["type"]) != 1.0f) throw std::invalid_argument("Check data must use equidistant channel samples");
        float const frequency = float(header["frequency"]);
        if (!(frequency > 0.0f) || !std::isfinite(frequency) || std::abs(double(frequency) * fps - 1.0) > 1e-4)
            throw std::invalid_argument("Check data frequency disagrees with time_scale");
    }
    auto const tracks = bones["animation_data"];
    if (tracks.size() == 0 || tracks.size() > max_bones) throw std::invalid_argument("Check data must contain 1..256 bones");
    result.bones.resize(tracks.size());
    for (unsigned b = 0; b < result.bone_count(); ++b) {
        auto const track = tracks[b];
        auto const channels = track.value_exists("data") ? track["data"] : track;
        result.bones[b].start_time = track.value_exists("start_time") ? float(track["start_time"]) : 0.0f;
        for (unsigned c = 0; c < channel_count; ++c) {
            auto const channel = channels[c];
            if (channel.size() == 0 || channel.size() > max_frames)
                throw std::invalid_argument("Check data channel must contain 1..1000000 samples");
            auto& samples = result.bones[b].channels[c];
            samples.reserve(channel.size());
            for (unsigned f = 0; f < channel.size(); ++f) samples.push_back(float(channel[f]));
        }
    }
    result.validate();
    return result;
}

} // namespace xray::animation_math
