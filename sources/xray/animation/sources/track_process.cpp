#include "pch.h"
#include <xray/animation/track_processing.h>
#include "bi_spline_skeleton_animation.h"

#include <set>
#include <stdexcept>

namespace xray::animation {

void process_track(animation_math::discrete_data const& samples,
    std::span<std::string const> names, bi_spline_skeleton_animation& output, bool is_additive) {
    samples.validate();
    if (names.size() != samples.bone_count()) throw std::invalid_argument("Animation bone names do not match sampled tracks");
    std::set<std::string> seen;
    for (auto const& name : names) {
        if (name.empty() || name.size() > 63 || name.find_first_of("/\\:\0", 0, 4) != std::string::npos ||
            !seen.insert(name).second)
            throw std::invalid_argument("Invalid or duplicate animation bone name: " + name);
    }
    // Fit and verify all channels before replacing the destination's data.
    auto const fitted = animation_math::fit_animation(samples);
    output.set_num_bones(u32(samples.bone_count()));
    output.set_bone_names_num_bones(u32(samples.bone_count()));
    output.set_animation_type(is_additive ? animation_type_additive : animation_type_full);
    for (u32 b = 0; b < samples.bone_count(); ++b) {
        output.set_bone_name(b, names[b].c_str());
        for (u32 c = 0; c < animation_math::channel_count; ++c) {
            auto& channel = output.bone(b).channel(enum_channel_id(c));
            channel.clear();
            for (auto const& point : fitted[b][c].clip_pairs) channel.add_point(point);
        }
    }
    output.finalize_bone_names();
}

} // namespace xray::animation
