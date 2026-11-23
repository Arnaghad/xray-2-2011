#include "static_opaque.hlsl"

void shadow_ps(PixelInput input) {
#if USE_ALPHA_TEST
    float alpha=tint.a;
#if USE_DIFFUSE_TEXTURE
    alpha*=material_texture.SampleLevel(material_sampler,input.uv*parameters.zw,0).a;
#endif
    clip(alpha-parameters.y);
#endif
}
