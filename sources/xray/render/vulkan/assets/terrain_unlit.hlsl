[[vk::binding(0, 0)]] cbuffer FrameConstants : register(b0, space0) {
    row_major float4x4 view;
    row_major float4x4 projection;
};
[[vk::binding(0, 2)]] cbuffer ObjectConstants : register(b0, space2) {
    row_major float4x4 world;
    float4 tint;
    float4 parameters;
};
[[vk::binding(0, 1)]] Texture2DArray<float4> layers : register(t0, space1);
[[vk::binding(1, 1)]] SamplerState layer_sampler : register(s0, space1);
struct VertexInput {
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    float3 weights : TEXCOORD1;
    float3 ids : TEXCOORD2;
    float3 color : TEXCOORD3;
    float2 shift : TEXCOORD4;
};
struct PixelInput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float3 weights : TEXCOORD1;
    float3 ids : TEXCOORD2;
    float3 color : TEXCOORD3;
};
PixelInput terrain_vs(VertexInput input) {
    PixelInput output;
    output.position=mul(projection,mul(view,mul(world,float4(input.position,1))));
    output.uv=(input.uv+input.shift*float2(-1,1)/parameters.y)*8;
    output.weights=input.weights; output.ids=input.ids; output.color=input.color;
    return output;
}
float4 terrain_ps(PixelInput input) : SV_Target {
    float3 ids=clamp(floor(input.ids+.5),0,parameters.z-1);
    float3 a=layers.SampleLevel(layer_sampler,float3(input.uv,ids.x),0).rgb;
    float3 b=layers.SampleLevel(layer_sampler,float3(input.uv,ids.y),0).rgb;
    float3 c=layers.SampleLevel(layer_sampler,float3(input.uv,ids.z),0).rgb;
    float grey=1-dot(input.weights,1);
    return float4((a*input.weights.x+b*input.weights.y+c*input.weights.z+grey*.5)*input.color*2,1);
}
