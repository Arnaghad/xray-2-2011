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
