#ifndef XRAY_RENDER_POSTPROCESS_SETTINGS_H_INCLUDED
#define XRAY_RENDER_POSTPROCESS_SETTINGS_H_INCLUDED

namespace xray { namespace render {
// Final lit presentation: deferred MSAA, HDR TAA/bloom, exposure/Reinhard or LDR FXAA.
struct postprocess_settings {
    float exposure_ev = 0;
    bool enabled = true;
    bool bloom_enabled = false;
    // Threshold is pre-exposure scene radiance; radius is in output pixels.
    float bloom_threshold = 1;
    float bloom_strength = .1f;
    float bloom_radius = 4;
    bool fxaa_enabled = false;
    // 1 disables MSAA; 2/4/8 require format and sample-rate shading support.
    unsigned msaa_samples = 1;
    bool taa_enabled = false;
    float taa_history_weight = .9f;
    // Increment for an application-defined camera cut. TAA and FXAA are exclusive.
    unsigned taa_reset_serial = 0;
    bool ssao_enabled = false, distortion_enabled = false;
    unsigned ssao_pixel_radius = 8;
    float ssao_world_radius = 2, ssao_bias = .05f, ssao_strength = 1;
    // Captured host phase in seconds; no implicit wall-time updates during comparison.
    float distortion_time = 0, distortion_max_pixels = 64;
};
} }
#endif
