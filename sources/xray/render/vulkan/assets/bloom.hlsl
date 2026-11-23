[[vk::binding(0, 2)]] cbuffer PostprocessConstants : register(b0, space2) {
    float4 viewport;
    float4 source_bounds;
    float4 filter;
    float4 bloom;
};
[[vk::binding(0, 1)]] Texture2D<float4> scene_texture : register(t0, space1);
[[vk::binding(2, 1)]] Texture2D<float4> bloom_texture : register(t1, space1);
[[vk::binding(1, 1)]] SamplerState scene_sampler : register(s0, space1);
struct PixelInput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
float2 scene_uv(PixelInput input) { return viewport.xy+input.uv*viewport.zw; }
float3 sample_scene(float2 uv) { return scene_texture.SampleLevel(scene_sampler,clamp(uv,source_bounds.xy,source_bounds.zw),0).rgb; }
float4 extract_ps(PixelInput input) : SV_Target0 {
    float3 color=max(sample_scene(scene_uv(input)),0);
    float peak=max(color.r,max(color.g,color.b));
    return float4(color*(max(peak-bloom.x,0)/max(peak,1e-6)),1);
}
float4 blur_ps(PixelInput input) : SV_Target0 {
    static const float weights[9]={1,8,28,56,70,56,28,8,1};
    float2 uv=scene_uv(input); float3 color=0;
    [unroll] for (int i=-4;i<=4;++i) color+=sample_scene(uv+float(i)*filter.xy)*weights[i+4]/256;
    return float4(min(color,65504),1);
}
float4 composite_ps(PixelInput input) : SV_Target0 {
    float2 uv=clamp(scene_uv(input),source_bounds.xy,source_bounds.zw);
    float3 color=max(scene_texture.SampleLevel(scene_sampler,uv,0).rgb,0);
    float3 glow=max(bloom_texture.SampleLevel(scene_sampler,uv,0).rgb,0);
    return float4(min(color+glow*bloom.y,65504),1);
}
