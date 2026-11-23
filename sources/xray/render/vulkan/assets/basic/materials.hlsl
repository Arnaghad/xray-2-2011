#include "materials_common.hlsli"
struct VertexInput { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct PixelInput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
PixelInput material_vs(VertexInput input) {
    PixelInput output;
    output.position = mul(projection, mul(view, mul(world, float4(input.position, 1))));
    output.uv = input.uv;
    return output;
}
float4 material_ps(PixelInput input) : SV_Target {
    return material_texture.SampleLevel(material_sampler, input.uv, parameters.x) * tint;
}
