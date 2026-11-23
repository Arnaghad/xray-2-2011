[[vk::binding(0, 0)]] cbuffer FrameConstants : register(b0, space0) {
    row_major float4x4 view;
    row_major float4x4 projection;
};
[[vk::binding(0, 1)]] Texture2D<float4> ui_texture : register(t0, space1);
[[vk::binding(1, 1)]] SamplerState ui_sampler : register(s0, space1);
struct VertexInput { float3 position : POSITION; float4 color : COLOR0; float2 uv : TEXCOORD0; };
struct PixelInput { float4 position : SV_Position; float4 color : COLOR0; float2 uv : TEXCOORD0; };
PixelInput ui_vs(VertexInput input) {
    PixelInput output;
    output.position=float4(input.position.x*view[0][0]*2-1,1-input.position.y*view[0][1]*2,0,projection[3][3]);
    output.color=input.color; output.uv=input.uv; return output;
}
float4 ui_ps(PixelInput input) : SV_Target {
#if UI_MODE == 0
    return float4(input.color.rgb,input.color.a*ui_texture.SampleLevel(ui_sampler,input.uv,0).a);
#elif UI_MODE == 1
    return input.color*ui_texture.SampleLevel(ui_sampler,input.uv,0);
#else
    return input.color;
#endif
}
