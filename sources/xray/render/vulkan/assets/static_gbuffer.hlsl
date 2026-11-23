[[vk::binding(0, 0)]] cbuffer FrameConstants : register(b0, space0) {
    row_major float4x4 view;
    row_major float4x4 projection;
};
[[vk::binding(0, 2)]] cbuffer ObjectConstants : register(b0, space2) {
    row_major float4x4 world;
    float4 tint;
    float4 parameters;
    float4 surface;
    float4 map_parameters;
    float4 detail_normal_parameters;
    float4 sequence_uv;
    float4 extra_parameters;
    float4 reflection_parameters;
    float4 bindless_indices;
    float4 sss_parameters;
};
[[vk::binding(0, 1)]] Texture2D<float4> material_texture : register(t0, space1);
[[vk::binding(1, 1)]] SamplerState material_sampler : register(s0, space1);
[[vk::binding(2, 1)]] Texture2D<float4> normal_texture : register(t1, space1);
[[vk::binding(3, 1)]] Texture2D<float4> roughness_texture : register(t2, space1);
#if USE_FRESNEL_TEXTURE
[[vk::binding(4, 1)]] Texture2D<float4> fresnel_texture : register(t3, space1);
#endif
[[vk::binding(5, 1)]] Texture2D<float4> detail_normal_texture : register(t4, space1);
#if USE_ANISOTROPY_DIR_TEXTURE
[[vk::binding(7, 1)]] Texture2D<float4> anisotropy_dir_texture : register(t5, space1);
#endif
#if USE_PARALLAX
[[vk::binding(8, 1)]] Texture2D<float4> bump_texture : register(t6, space1);
#endif
#if USE_TRANSLUCENCY_TEXTURE
[[vk::binding(9, 1)]] Texture2D<float4> translucency_texture : register(t7, space1);
#endif
#if USE_DETAIL_NORMAL_TEXTURE && !USE_NORMAL_TEXTURE
#error Detail normal requires a base normal map
#endif
struct VertexInput { float3 position : POSITION; float2 uv : TEXCOORD0; float4 normal : NORMAL; float4 tangent : TANGENT; float4 binormal : BINORMAL; };
struct PixelInput {
    float4 position : SV_Position;
    float3 view_position : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float3 tangent : TEXCOORD2;
    float2 uv : TEXCOORD3;
    float3 binormal : TEXCOORD4;
    float3 world_pos : TEXCOORD5;
    float3 world_normal : TEXCOORD6;
};
PixelInput gbuffer_vs(VertexInput input) {
    PixelInput output;
    float4 world_p=mul(world,float4(input.position,1));
    float4 p=mul(view,world_p);
    output.position=mul(projection,p); output.view_position=p.xyz; output.uv=input.uv;
    output.world_pos=world_p.xyz;
    float3x3 m=(float3x3)mul(view,world);
    float3x3 cofactors=float3x3(cross(m[1],m[2]),cross(m[2],m[0]),cross(m[0],m[1]));
    // The on-disk D3DCOLOR bytes are B,G,R,A. Normalization cancels the
    // historical unpack_bx4 scale while preserving the signed direction.
    float3 n=input.normal.bgr*2-1, t=input.tangent.bgr*2-1;
    output.normal=normalize(mul(cofactors,n)/dot(m[0],cofactors[0]));
    output.tangent=mul(m,t);
    output.binormal=mul(m,input.binormal.bgr*2-1);
    float3x3 wm=(float3x3)world;
    float3x3 wcof=float3x3(cross(wm[1],wm[2]),cross(wm[2],wm[0]),cross(wm[0],wm[1]));
    output.world_normal=normalize(mul(wcof,n)/dot(wm[0],wcof[0]));
    return output;
}
struct GBuffer { float4 depth : SV_Target0; float4 normal : SV_Target1; float4 tangent : SV_Target2; };
GBuffer gbuffer_ps(PixelInput input, bool front : SV_IsFrontFace) {
    float2 uv = input.uv * parameters.zw * sequence_uv.zw + sequence_uv.xy;
    float3 n=normalize(input.normal);
    float3 t=input.tangent-n*dot(input.tangent,n);
    if (dot(t,t)<1e-12) t=cross(n,abs(n.z)<.9 ? float3(0,0,1) : float3(0,1,0));
    t=normalize(t);
    // Retain the disk binormal handedness, including mirrored UV islands.
    float3 b=normalize(cross(n,t)); if (dot(b,input.binormal)<0) b=-b;
    if (!front) n=-n;
#if USE_PARALLAX
    {
        float3 v_view=normalize(-input.view_position);
        float3 v_ts=normalize(float3(dot(v_view,t),dot(v_view,b),dot(v_view,n)));
        float2 p_dir=(v_ts.xy/max(abs(v_ts.z),0.25))*extra_parameters.x;
        float2 dx=ddx(uv),dy=ddy(uv);
        const int steps=8;
        float layer_depth=1.0/steps;
        float cur_layer=0.0;
        float2 delta_uv=p_dir*layer_depth;
        float2 cur_uv=uv;
        float h=1.0-bump_texture.SampleGrad(material_sampler,cur_uv,dx,dy).r;
        [unroll] for (int i=0;i<steps;++i) {
            if (cur_layer<h) {
                cur_uv-=delta_uv;
                h=1.0-bump_texture.SampleGrad(material_sampler,cur_uv,dx,dy).r;
                cur_layer+=layer_depth;
            }
        }
        float2 prev_uv=cur_uv+delta_uv;
        float after_h=h-cur_layer;
        float before_h=(1.0-bump_texture.SampleGrad(material_sampler,prev_uv,dx,dy).r)-cur_layer+layer_depth;
        float weight=saturate(after_h/min(after_h-before_h,-1e-5));
        uv=lerp(cur_uv,prev_uv,weight);
    }
#endif
#if USE_TRIPLANAR
    float3 wn=normalize(input.world_normal);
    float3 tw=pow(abs(wn),4.0);
    tw/=max(tw.x+tw.y+tw.z,1e-5);
    float2 uv_x=input.world_pos.zy*parameters.zw*sequence_uv.zw+sequence_uv.xy;
    float2 uv_y=input.world_pos.xz*parameters.zw*sequence_uv.zw+sequence_uv.xy;
    float2 uv_z=input.world_pos.xy*parameters.zw*sequence_uv.zw+sequence_uv.xy;
#endif
#if USE_ALPHA_TEST
#if USE_DIFFUSE_TEXTURE
#if USE_TRIPLANAR
    float alpha=(material_texture.SampleLevel(material_sampler,uv_x,0).a*tw.x
        +material_texture.SampleLevel(material_sampler,uv_y,0).a*tw.y
        +material_texture.SampleLevel(material_sampler,uv_z,0).a*tw.z)*tint.a;
#else
    float alpha=material_texture.SampleLevel(material_sampler,uv,0).a*tint.a;
#endif
#else
    float alpha=tint.a;
#endif
    clip(alpha-parameters.y);
#endif
#if USE_ANISOTROPY_DIR_TEXTURE
    float2 aniso_dir=anisotropy_dir_texture.Sample(material_sampler,uv).rg*2-1;
    float3 t_aniso=t*aniso_dir.x+b*aniso_dir.y;
    if (dot(t_aniso,t_aniso)>1e-12) t=normalize(t_aniso);
#endif
#if USE_NORMAL_TEXTURE
#if USE_TRIPLANAR
    float2 nxy_x=normal_texture.Sample(material_sampler,uv_x).ag*2-1;
    float2 nxy_y=normal_texture.Sample(material_sampler,uv_y).ag*2-1;
    float2 nxy_z=normal_texture.Sample(material_sampler,uv_z).ag*2-1;
    float2 xy=nxy_x*tw.x+nxy_y*tw.y+nxy_z*tw.z;
#else
    float2 xy=normal_texture.Sample(material_sampler,uv).ag*2-1;
#endif
    float3 mapped=normalize(float3(xy.x,-xy.y,sqrt(saturate(1-dot(xy,xy)))));
#if USE_DETAIL_NORMAL_TEXTURE
    float2 detail=detail_normal_texture.Sample(material_sampler,uv*detail_normal_parameters.zw).ag*2-1;
    detail*=detail_normal_parameters.xy;
    float3 detailed=mapped+float3(detail.x,-detail.y,0);
    // Opposing tangent-plane normals can cancel exactly; retain the base direction.
    if (dot(detailed,detailed)>1e-12) mapped=detailed;
#endif
    n=normalize(t*mapped.x+b*mapped.y+n*mapped.z);
#endif
    float2 roughness=surface.xy;
#if USE_ROUGHNESS_TEXTURE
#if USE_TRIPLANAR
    float value=max(roughness_texture.Sample(material_sampler,uv_x).g*tw.x
        +roughness_texture.Sample(material_sampler,uv_y).g*tw.y
        +roughness_texture.Sample(material_sampler,uv_z).g*tw.z,.01);
#else
    float value=max(roughness_texture.Sample(material_sampler,uv).g,.01);
#endif
    roughness=map_parameters.x+(map_parameters.y-map_parameters.x)*value;
#endif
#if USE_BURNING_WOOD
    roughness=max(roughness,float2(0.75,0.75));
#endif
    float fresnel=surface.z;
#if USE_FRESNEL_TEXTURE
    fresnel=saturate(fresnel_texture.Sample(material_sampler,uv).r);
#endif
    float translucency=saturate(extra_parameters.y);
#if USE_TRANSLUCENCY_TEXTURE
    translucency=saturate(translucency_texture.Sample(material_sampler,uv).r);
#endif
    GBuffer output;
    // ABI of geometry_pre_pass.ps: sqrt(view_z/far), receiver + packed translucency, roughness UV.
    output.depth=float4(sqrt(saturate(input.view_position.z/parameters.x)),surface.w+translucency*0.10,roughness);
    float sss_profile=round(sss_parameters.x);
    float n_sign=n.z>0 ? 0.0 : 1.0;
    if (sss_profile>1.5) n_sign=n.z>0 ? -2.0 : 3.0;
    else if (sss_profile>0.5) n_sign=n.z>0 ? -1.0 : 2.0;
    output.normal=float4(n.xy*.5+.5,fresnel,n_sign);
    float3 tangent_cross=cross(t,n);
    if (dot(tangent_cross,tangent_cross)<1e-12) tangent_cross=cross(n,abs(n.z)<.9 ? float3(0,0,1) : float3(0,1,0));
    output.tangent=float4(normalize(tangent_cross)*.5+.5,1.0-saturate(extra_parameters.z)*0.45);
    return output;
}
