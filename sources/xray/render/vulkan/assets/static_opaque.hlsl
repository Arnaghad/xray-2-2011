[[vk::binding(0, 0)]] cbuffer FrameConstants : register(b0, space0) {
    row_major float4x4 view;
    row_major float4x4 projection;
};
[[vk::binding(0, 2)]] cbuffer ObjectConstants : register(b0, space2) {
    row_major float4x4 world;
    float4 tint;
    float4 parameters;
};
[[vk::binding(0, 1)]] Texture2D<float4> material_texture : register(t0, space1);
[[vk::binding(1, 1)]] SamplerState material_sampler : register(s0, space1);
struct VertexInput { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct PixelInput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
PixelInput static_vs(VertexInput input) {
    PixelInput output;
    output.position = mul(projection, mul(view, mul(world, float4(input.position, 1))));
    output.uv = input.uv;
    return output;
}
float4 static_ps(PixelInput input) : SV_Target {
#if USE_DIFFUSE_TEXTURE
    float4 color = material_texture.SampleLevel(material_sampler, input.uv * parameters.zw, parameters.x) * tint;
#else
    float4 color = tint;
#endif
#if USE_ALPHA_TEST
    clip(color.a - parameters.y);
#endif
    return color;
}
float4 mask_ps(PixelInput input) : SV_Target {
    float alpha=tint.a;
#if USE_DIFFUSE_TEXTURE && USE_ALPHA_TEST
    alpha*=material_texture.SampleLevel(material_sampler,input.uv*parameters.zw,parameters.x).a;
#endif
#if USE_ALPHA_TEST
    clip(alpha-parameters.y);
#endif
    return 1;
}
