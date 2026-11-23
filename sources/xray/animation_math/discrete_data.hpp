#pragma once

#include "bspline_fit.hpp"

#include <span>

namespace xray::animation_math {

inline constexpr std::size_t channel_count = 9;
inline constexpr std::size_t max_bones = 256;
inline constexpr std::size_t max_frames = 1000000;
using sample_frame = std::array<float, channel_count>;

struct sampled_bone {
    float start_time = 0.0f; // Engine time, in 30-fps frames.
    std::array<std::vector<float>, channel_count> channels;
};

// Value-owned, host-independent sampled animation. No DCC world or allocator API.
struct discrete_data {
    float sample_interval = 1.0f; // 30 / source fps.
    std::array<float, channel_count> precisions{0.005f, 0.005f, 0.005f,
        0.005f, 0.005f, 0.005f, 0.005f, 0.005f, 0.005f};
    std::vector<sampled_bone> bones;

    std::size_t bone_count() const { return bones.size(); }
    std::size_t frame_count() const { return bones.empty() ? 0 : bones.front().channels[0].size(); }
    float precision(std::size_t channel) const { return precisions.at(channel); }
    float time(std::size_t bone, std::size_t frame) const {
        if (frame >= frame_count()) throw std::out_of_range("Animation frame index out of range");
        return bones.at(bone).start_time + float(frame) * sample_interval;
    }
    sample_frame frame(std::size_t bone, std::size_t number) const {
        sample_frame result{};
        for (std::size_t c = 0; c < channel_count; ++c) result[c] = bones.at(bone).channels[c].at(number);
        return result;
    }
    void validate() const {
        if (bones.empty() || bones.size() > max_bones) throw std::invalid_argument("Animation must contain 1..256 bones");
        if (!(sample_interval > 0.0f) || !std::isfinite(sample_interval))
            throw std::invalid_argument("Animation sample interval must be positive finite");
        for (float epsilon : precisions)
            if (!(epsilon > 0.0f) || !std::isfinite(epsilon))
                throw std::invalid_argument("Animation precisions must be positive finite");
        auto const count = frame_count();
        if (count == 0 || count > max_frames) throw std::invalid_argument("Animation must contain 1..1000000 frames");
        for (auto const& bone : bones) {
            if (!std::isfinite(bone.start_time) || !std::isfinite(bone.start_time + float(count - 1) * sample_interval))
                throw std::invalid_argument("Animation sample times must be finite");
            for (auto const& channel : bone.channels) {
                if (channel.size() != count) throw std::invalid_argument("Animation channels must have equal frame counts");
                for (float sample : channel)
                    if (!std::isfinite(sample)) throw std::invalid_argument("Animation samples must be finite");
            }
        }
    }
};

inline fitted_bspline_channel fit_track(std::vector<float> const& samples, float interval,
                                       float precision, float start_time = 0.0f) {
    if (!(interval > 0.0f) || !std::isfinite(interval) || !(precision > 0.0f) || !std::isfinite(precision) ||
        !std::isfinite(start_time) || samples.empty() || samples.size() > max_frames)
        throw std::invalid_argument("Invalid animation track fitting parameters");
    float const end_time = start_time + float(std::max<std::size_t>(samples.size(), 2) - 1) * interval;
    if (!std::isfinite(end_time) || !(end_time > start_time))
        throw std::invalid_argument("Animation track must have a distinct finite time range");
    auto result = fit_cubic_bspline_channel(samples, interval, precision);
    // The legacy channel fitter is shared verbatim with the cooker. Editor
    // processing additionally verifies the requested postcondition explicitly.
    for (std::size_t i = 0; i < samples.size(); ++i) {
        float const actual = evaluate_cubic_bspline(result.full_knots, result.coefs, float(i) * interval);
        if (!std::isfinite(actual) || std::abs(double(actual) - samples[i]) > precision)
            throw std::runtime_error("Animation B-spline fit exceeds requested precision");
    }
    if (start_time != 0.0f) {
        for (float& knot : result.full_knots) knot += start_time;
        for (auto& pair : result.clip_pairs) pair.first += start_time;
        // Check the stored float32 time coordinates too (large offsets can lose
        // the local knot spacing even when the unshifted fit was accurate).
        for (std::size_t i = 0; i < samples.size(); ++i) {
            float const actual = evaluate_cubic_bspline(result.full_knots, result.coefs, start_time + float(i) * interval);
            if (!std::isfinite(actual) || std::abs(double(actual) - samples[i]) > precision)
                throw std::runtime_error("Animation track offset loses fitting precision");
        }
    }
    return result;
}

using fitted_bone = std::array<fitted_bspline_channel, channel_count>;
inline std::vector<fitted_bone> fit_animation(discrete_data const& data) {
    data.validate();
    std::vector<fitted_bone> result(data.bone_count());
    for (std::size_t b = 0; b < data.bone_count(); ++b)
        for (std::size_t c = 0; c < channel_count; ++c)
            result[b][c] = fit_track(data.bones[b].channels[c], data.sample_interval, data.precisions[c], data.bones[b].start_time);
    return result;
}

struct validation_result {
    bool bone_count_matches = true;
    std::size_t mismatches = 0;
    double max_error = 0.0;
    std::size_t bone = 0, channel = 0, frame = 0;
    float expected = 0.0f, actual = 0.0f;
    bool passed() const { return bone_count_matches && mismatches == 0; }
};

template<typename Evaluate>
validation_result compare_samples(discrete_data const& data, std::size_t actual_bones, Evaluate&& evaluate,
                                  float precision_multiplier = 1.0f) {
    data.validate();
    if (!(precision_multiplier > 0.0f) || !std::isfinite(precision_multiplier))
        throw std::invalid_argument("Animation validation multiplier must be positive finite");
    validation_result result;
    result.bone_count_matches = data.bone_count() == actual_bones;
    if (!result.bone_count_matches) return result;
    for (std::size_t b = 0; b < data.bone_count(); ++b)
        for (std::size_t c = 0; c < channel_count; ++c)
            for (std::size_t f = 0; f < data.frame_count(); ++f) {
                float const expected = data.bones[b].channels[c][f];
                float const actual = evaluate(b, c, data.time(b, f));
                double const error = std::isfinite(actual) ? std::abs(double(actual) - expected) : std::numeric_limits<double>::infinity();
                if (error > double(data.precisions[c]) * precision_multiplier) ++result.mismatches;
                if (error > result.max_error) {
                    result.max_error = error;
                    result.bone = b; result.channel = c; result.frame = f;
                    result.expected = expected; result.actual = actual;
                }
            }
    return result;
}

} // namespace xray::animation_math
