[[vk::binding(0,2)]] cbuffer PostprocessConstants {
    float4 viewport;
    float4 source_bounds;
    float4 filter; // width, height, maximum displacement pixels
    float4 bloom;
};
[[vk::binding(0,1)]] Texture2D<float4> scene;
[[vk::binding(1,1)]] SamplerState linear_sampler;
[[vk::binding(2,1)]] Texture2D<float4> field;
struct PixelInput {float4 position:SV_Position;float2 uv:TEXCOORD0;};
float4 composite_ps(PixelInput input):SV_Target0 {
    int2 pixel=int2(input.position.xy);float2 uv=(pixel+.5)/filter.xy;
    float4 value=field.Load(int3(pixel,0));float2 offset=clamp(value.xy,-filter.z/filter.xy,filter.z/filter.xy);
    if(value.w<.5 || all(offset==0))return scene.Load(int3(pixel,0));
    float2 shifted=clamp(uv+offset,source_bounds.xy,source_bounds.zw);
    // Do not pull foreground pixels or sample beyond the visible field silhouette.
    if(field.Load(int3(int2(shifted*filter.xy),0)).w<.5)return scene.Load(int3(pixel,0));
    return scene.SampleLevel(linear_sampler,shifted,0);
}
