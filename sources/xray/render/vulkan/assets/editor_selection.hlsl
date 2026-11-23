[[vk::binding(0, 2)]] cbuffer ObjectConstants : register(b0, space2) {row_major float4x4 world;float4 tint;float4 parameters;};
[[vk::binding(0, 1)]] Texture2D<float4> selection_mask : register(t0, space1);
struct PixelInput {float4 position:SV_Position;};
PixelInput selection_vs(uint id:SV_VertexID) {PixelInput result;float2 p=float2((id<<1)&2,id&2);result.position=float4(p*2-1,0,1);return result;}
float4 selection_ps(PixelInput input):SV_Target0 {
    int2 p=int2(input.position.xy);float center=selection_mask.Load(int3(p,0)).r,edge=0;
    int radius=(int)parameters.z;
    for(int y=-radius;y<=radius;++y)for(int x=-radius;x<=radius;++x) {
        int2 q=clamp(p+int2(x,y),int2(0,0),int2(parameters.xy)-1);
        edge=max(edge,selection_mask.Load(int3(q,0)).r);
    }
    return float4(tint.rgb,tint.a*saturate(edge-center));
}
