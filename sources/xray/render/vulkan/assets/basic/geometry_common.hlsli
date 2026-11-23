#ifndef SMOKE_DEPTH
#define SMOKE_DEPTH 0.25
#endif
struct VertexInput {
    [[vk::location(0)]] float3 position : POSITION;
    [[vk::location(1)]] float3 color : COLOR;
};
struct VertexOutput {
    float4 position : SV_Position;
    [[vk::location(0)]] float3 color : COLOR;
};
