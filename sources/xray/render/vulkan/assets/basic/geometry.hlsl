#include "geometry_common.hlsli"
VertexOutput triangle_vs(uint id : SV_VertexID) {
    float2 positions[3] = {float2(0, 0.7), float2(-0.7, -0.7), float2(0.7, -0.7)};
    float3 colors[3] = {float3(1, 0, 0), float3(0, 1, 0), float3(0, 0, 1)};
    VertexOutput output;
    output.position = float4(positions[id], SMOKE_DEPTH, 1);
    output.color = colors[id];
    return output;
}
VertexOutput mesh_vs(VertexInput input) {
    VertexOutput output;
    output.position = float4(input.position, 1);
    output.color = input.color;
    return output;
}
float4 color_ps(VertexOutput input) : SV_Target0 {
    return float4(input.color, 1);
}
