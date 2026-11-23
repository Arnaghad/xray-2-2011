[[vk::binding(0, 0)]] cbuffer FrameConstants : register(b0, space0) {
    row_major float4x4 view;
    row_major float4x4 projection;
};
[[vk::binding(0, 1)]] Texture2D<float4> diffuse : register(t0, space1);
[[vk::binding(1, 1)]] SamplerState diffuse_sampler : register(s0, space1);
struct VertexInput { float3 position : POSITION; float4 color : COLOR0; float2 uv : TEXCOORD0; };
struct PixelInput { float4 position : SV_Position; float4 color : COLOR0; float2 uv : TEXCOORD0; };
PixelInput particle_vs(VertexInput input) {
    PixelInput output;output.position=mul(projection,mul(view,float4(input.position,1)));
    output.color=input.color;output.uv=input.uv;return output;
}
float4 particle_ps(PixelInput input) : SV_Target {return diffuse.SampleLevel(diffuse_sampler,input.uv,0)*input.color;}
