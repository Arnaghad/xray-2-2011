[[vk::binding(0, 0)]] cbuffer FrameConstants : register(b0, space0) {
    row_major float4x4 view;
    row_major float4x4 projection;
};
struct VertexInput { float3 position : POSITION; float4 color : COLOR0; };
struct PixelInput { float4 position : SV_Position; float4 color : COLOR0; };
PixelInput debug_vs(VertexInput input) {
    PixelInput output;
    output.position=mul(projection,mul(view,float4(input.position,1)));
    output.color=input.color;
    return output;
}
float4 debug_ps(PixelInput input) : SV_Target { return input.color; }
