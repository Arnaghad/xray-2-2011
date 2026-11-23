[[vk::binding(0, 2)]] cbuffer PostprocessConstants : register(b0, space2) {
    float4 viewport;
    float4 source_bounds;
    float4 filter;
    float4 bloom;
};
[[vk::binding(0, 1)]] Texture2D<float4> scene_texture : register(t0, space1);
[[vk::binding(1, 1)]] SamplerState scene_sampler : register(s0, space1);
struct PixelInput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
#include "fxaa_preset3.hlsli"
float3 encode_srgb(float3 rgb) {
    return float3(rgb.r<=.0031308 ? rgb.r*12.92 : 1.055*pow(rgb.r,1.0/2.4)-.055,
        rgb.g<=.0031308 ? rgb.g*12.92 : 1.055*pow(rgb.g,1.0/2.4)-.055,
        rgb.b<=.0031308 ? rgb.b*12.92 : 1.055*pow(rgb.b,1.0/2.4)-.055);
}
float3 decode_srgb(float3 rgb) {
    return float3(rgb.r<=.04045 ? rgb.r/12.92 : pow((rgb.r+.055)/1.055,2.4),
        rgb.g<=.04045 ? rgb.g/12.92 : pow((rgb.g+.055)/1.055,2.4),
        rgb.b<=.04045 ? rgb.b/12.92 : pow((rgb.b+.055)/1.055,2.4));
}
float4 tonemap_encode_ps(PixelInput input) : SV_Target0 {
    float2 uv=clamp(input.position.xy*filter.xy,source_bounds.xy,source_bounds.zw);
    float3 exposed=max(scene_texture.SampleLevel(scene_sampler,uv,0).rgb,0)*bloom.x;
    return float4(encode_srgb(exposed/(1+exposed)),1);
}
float4 fxaa_ps(PixelInput input) : SV_Target0 {
    FxaaTex tex={scene_sampler,scene_texture};
    float3 encoded=FxaaPixelShader(input.position.xy*filter.xy,tex,filter.xy);
    // The presentation attachment will perform the single final hardware sRGB encode.
    return float4(decode_srgb(saturate(encoded)),1);
}
