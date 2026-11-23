[[vk::binding(0,2)]] cbuffer ObjectConstants {
    row_major float4x4 world;
    float4 tint;
    float4 parameters; // scale XY /255, captured panner phase XY
};
[[vk::binding(0,1)]] Texture2D<float4> field_texture;
[[vk::binding(1,1)]] SamplerState field_sampler;
struct PixelInput {float4 position:SV_Position;float2 uv:TEXCOORD0;};
float4 distortion_ps(PixelInput input):SV_Target0 {
    float2 direction=field_texture.SampleLevel(field_sampler,input.uv+parameters.zw,0).rg*2-1;
    float alpha=field_texture.SampleLevel(field_sampler,input.uv,0).a*tint.a;
    return float4(direction*tint.rg*parameters.xy*alpha,0,1);
}
