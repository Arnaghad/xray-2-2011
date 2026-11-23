struct PixelInput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
PixelInput sky_vs(uint id : SV_VertexID) {
    float2 p=float2((id<<1)&2,id&2);
    PixelInput result; result.position=float4(p*2-1,1,1); result.uv=p; return result;
}
[[vk::binding(0,2)]] cbuffer ObjectConstants { row_major float4x4 world; float4 tint; float4 parameters; };
[[vk::binding(0,1)]] TextureCube<float4> sky_texture;
[[vk::binding(1,1)]] SamplerState sky_sampler;
float4 sky_ps(PixelInput input) : SV_Target0 {
    float2 clip=input.uv*float2(2,-2)+float2(-1,1);
    float4 near_point=mul(world,float4(clip,0,1)),far_point=mul(world,float4(clip,1,1));
    float3 direction=far_point.xyz*near_point.w-near_point.xyz*far_point.w;
    return float4(clamp(sky_texture.SampleLevel(sky_sampler,direction,0).rgb*tint.rgb,0,65504),1);
}
