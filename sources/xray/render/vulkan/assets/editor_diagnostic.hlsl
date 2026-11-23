[[vk::binding(0, 2)]] cbuffer ObjectConstants:register(b0,space2) {row_major float4x4 world;float4 tint;float4 parameters;float4 surface;};
[[vk::binding(0, 1)]] Texture2D<float4> material_texture:register(t0,space1);
[[vk::binding(1, 1)]] SamplerState material_sampler:register(s0,space1);
[[vk::binding(0, 0)]] cbuffer FrameConstants:register(b0,space0){row_major float4x4 view;row_major float4x4 projection;};
struct VertexInput {float3 position:POSITION;float2 uv:TEXCOORD0;};
struct PixelInput {float4 position:SV_Position;float2 uv:TEXCOORD0;};
PixelInput diagnostic_vs(VertexInput input){PixelInput result;result.position=mul(projection,mul(view,mul(world,float4(input.position,1))));result.uv=input.uv;return result;}
float3 heat(float value) {return saturate(float3(1.5-abs(value*4-3),1.5-abs(value*4-2),1.5-abs(value*4-1)));}
float4 diagnostic_ps(PixelInput input):SV_Target0 {
    float2 uv=input.uv*parameters.zw;float alpha=material_texture.SampleLevel(material_sampler,uv,0).a*tint.a;
    if(surface.w>.5)clip(alpha-parameters.y);
    uint mode=(uint)parameters.x;uint w,h;material_texture.GetDimensions(w,h);
    if(mode==7) {
        float lod=max(0,material_texture.CalculateLevelOfDetail(material_sampler,uv));
        return float4(heat(frac(lod/8)),1);
    }
    if(mode==11)return float4(surface.x/2048,surface.x/8192,0,1);
    if(mode==12) {
        float footprint=max(length(ddx(uv*float2(w,h))),length(ddy(uv*float2(w,h))));
        return float4(heat(saturate((log2(max(footprint,1e-6))+4)/8)),1);
    }
    if(mode==13)return float4(heat(saturate(log2(1+surface.y*256)/8)),1);
    // Linear additive fragment counter (32 fragments saturate the red channel).
    return float4(1.f/32,1.f/128,0,1);
}
