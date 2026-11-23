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
    float4 specular;
    float4 emissive;
    float4 sequence_uv;
    float4 variation_parameters;
    float4 variation_color;
    float4 diffuse_attenuation;
    float4 detail_diffuse;
    float4 reflection_parameters;
    float4 bindless_indices;
    float4 bindless_indices_extra;
};
#if USE_BINDLESS
[[vk::binding(0, 3)]] Texture2D<float4> bindless_2d[1024] : register(t0, space3);
[[vk::binding(1, 3)]] TextureCube<float4> bindless_cube[128] : register(t1, space3);
#endif
[[vk::binding(0, 1)]] Texture2D<float4> material_texture : register(t0, space1);
[[vk::binding(1, 1)]] SamplerState material_sampler : register(s0, space1);
#if USE_EMISSIVE_TEXTURE
[[vk::binding(2, 1)]] Texture2D<float4> emissive_texture : register(t1, space1);
#endif
#if USE_DETAIL_DIFFUSE_TEXTURE
[[vk::binding(3, 1)]] Texture2D<float4> detail_diffuse_texture : register(t2, space1);
#endif
[[vk::binding(4, 1)]] Texture2D<float4> specular_texture : register(t3, space1);
#if USE_VARIATION_TEXTURE
[[vk::binding(5, 1)]] Texture2D<float4> variation_mask_texture : register(t4, space1);
#endif
#if USE_DIFFUSE_POWER_TEXTURE
[[vk::binding(7, 1)]] Texture2D<float4> diffuse_power_texture : register(t5, space1);
#endif
#if USE_PARALLAX
[[vk::binding(8, 1)]] Texture2D<float4> bump_texture : register(t6, space1);
#endif
#if USE_WHIRL_TEXTURE
[[vk::binding(9, 1)]] Texture2D<float4> whirl_texture : register(t7, space1);
#endif
struct VertexInput { float3 position : POSITION; float2 uv : TEXCOORD0; float4 normal : NORMAL; float4 tangent : TANGENT; float4 binormal : BINORMAL; };
struct PixelInput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float3 world_pos : TEXCOORD1;
    float3 view_position : TEXCOORD2;
    float3 normal : TEXCOORD3;
    float3 tangent : TEXCOORD4;
    float3 binormal : TEXCOORD5;
    float3 world_normal : TEXCOORD6;
    float3 world_view_dir : TEXCOORD7;
};
PixelInput material_vs(VertexInput input) {
    PixelInput output;
    // Keep the position expression identical to the geometry pre-pass for EQUAL depth.
    float4 world_p=mul(world,float4(input.position,1));
    float4 p=mul(view,world_p);
    output.position=mul(projection,p); output.uv=input.uv; output.world_pos=world_p.xyz;
    output.view_position=p.xyz;
    output.world_view_dir=mul(transpose((float3x3)view),-p.xyz);
    float3x3 m=(float3x3)mul(view,world);
    float3x3 cofactors=float3x3(cross(m[1],m[2]),cross(m[2],m[0]),cross(m[0],m[1]));
    float3 n=input.normal.bgr*2-1, t=input.tangent.bgr*2-1;
    output.normal=normalize(mul(cofactors,n)/dot(m[0],cofactors[0]));
    output.tangent=mul(m,t);
    output.binormal=mul(m,input.binormal.bgr*2-1);
    float3x3 wm=(float3x3)world;
    float3x3 wcof=float3x3(cross(wm[1],wm[2]),cross(wm[2],wm[0]),cross(wm[0],wm[1]));
    output.world_normal=normalize(mul(wcof,n)/dot(wm[0],wcof[0]));
    return output;
}
struct Material { float4 albedo : SV_Target0; float4 specular_color : SV_Target1; float4 emission : SV_Target2; };
Material material_ps(PixelInput input, bool front : SV_IsFrontFace) {
    float2 uv=input.uv*parameters.zw*sequence_uv.zw+sequence_uv.xy;
#if USE_PARALLAX
    {
        float3 n=normalize(input.normal);
        float3 t=input.tangent-n*dot(input.tangent,n);
        if (dot(t,t)<1e-12) t=cross(n,abs(n.z)<.9 ? float3(0,0,1) : float3(0,1,0));
        t=normalize(t);
        float3 b=normalize(cross(n,t)); if (dot(b,input.binormal)<0) b=-b;
        if (!front) n=-n;
        float3 v_view=normalize(-input.view_position);
        float3 v_ts=normalize(float3(dot(v_view,t),dot(v_view,b),dot(v_view,n)));
        float2 p_dir=(v_ts.xy/max(abs(v_ts.z),0.25))*detail_diffuse.z;
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
    #define SAMPLE_MAT(tex,t_uv) (tex.Sample(material_sampler,uv_x)*tw.x+tex.Sample(material_sampler,uv_y)*tw.y+tex.Sample(material_sampler,uv_z)*tw.z)
#else
    #define SAMPLE_MAT(tex,t_uv) tex.Sample(material_sampler,t_uv)
#endif
    float4 color=float4(pow(tint.rgb,2.2),tint.a);
#if USE_DIFFUSE_TEXTURE
    color*=SAMPLE_MAT(material_texture,uv);
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
#if USE_DETAIL_DIFFUSE_TEXTURE
    color.rgb*=detail_diffuse_texture.Sample(material_sampler,uv*detail_diffuse.xy).rgb*2.0;
#endif
#if USE_VARIATION_TEXTURE
    float2 var_p=input.world_pos.xz*variation_parameters.x;
    float2 var_rot=float2(var_p.x*variation_parameters.y-var_p.y*variation_parameters.z,var_p.x*variation_parameters.z+var_p.y*variation_parameters.y)*variation_parameters.w;
    float var_mask=variation_mask_texture.Sample(material_sampler,var_rot).r;
    color.rgb=lerp(color.rgb,color.rgb*pow(max(variation_color.rgb,0),2.2),saturate(var_mask));
#endif
    color.rgb*=diffuse_attenuation.rgb;
    float diff_power=diffuse_attenuation.w;
#if USE_DIFFUSE_POWER_TEXTURE
    diff_power*=diffuse_power_texture.Sample(material_sampler,uv).r;
#endif
    if (abs(diff_power-1.0)>1e-4) color.rgb=pow(max(color.rgb,1e-5),diff_power);
    float3 specular_color=pow(specular.rgb,2.2);
#if USE_SPECULAR_TEXTURE
    specular_color*=SAMPLE_MAT(specular_texture,uv).rgb;
#endif
    float3 out_emissive=emissive.rgb;
#if USE_EMISSIVE_TEXTURE
    out_emissive*=SAMPLE_MAT(emissive_texture,uv).rgb;
#endif
#if USE_BINDLESS
    uint4 bi=asuint(bindless_indices);
    if (bi.z>0) {
        float3 subdermal_col=bindless_2d[NonUniformResourceIndex(bi.z)].Sample(material_sampler,uv).rgb;
        color.rgb=lerp(color.rgb,color.rgb*0.65+subdermal_col*0.35,saturate(reflection_parameters.w*0.5));
    }
    if (bi.w>0) {
        float ao_val=bindless_2d[NonUniformResourceIndex(bi.w)].Sample(material_sampler,uv).r;
        color.rgb*=ao_val;
    }
    if (bi.x>0 && reflection_parameters.y>0.0) {
        float3 n_w=normalize(input.world_normal);
        float3 v_w=normalize(input.world_view_dir);
        float ndotv=saturate(dot(n_w,v_w));
        float min_cos=cos(radians(reflection_parameters.x));
        float fresnel_angle=smoothstep(0.0,max(1.0-min_cos,0.05),1.0-ndotv)+step(reflection_parameters.x,0.5)*0.25;
        float refl_weight=reflection_parameters.y*saturate(fresnel_angle);
        if (bi.y>0) refl_weight*=bindless_2d[NonUniformResourceIndex(bi.y)].Sample(material_sampler,uv).r;
        float3 refl_dir=reflect(-v_w,n_w);
        float3 env_col=bindless_cube[NonUniformResourceIndex(bi.x)].Sample(material_sampler,refl_dir).rgb;
        if ((uint(reflection_parameters.z) & 2u)!=0u)
            color.rgb=lerp(color.rgb,env_col,saturate(refl_weight));
        else
            out_emissive+=env_col*specular_color*refl_weight;
    }
#endif
#if USE_BURNING_WOOD
    float burn_mask=0.5;
#if USE_WHIRL_TEXTURE
    float w0=whirl_texture.Sample(material_sampler,uv+float2(detail_diffuse.w*0.03,detail_diffuse.w*0.02)).r;
    float w1=whirl_texture.Sample(material_sampler,uv*1.7-float2(detail_diffuse.w*0.02,detail_diffuse.w*0.04)).g;
    burn_mask=saturate(w0*0.6+w1*0.4);
#endif
    color.rgb*=lerp(float3(0.08,0.06,0.05),float3(1.0,1.0,1.0),saturate(burn_mask*1.2));
    float ember=smoothstep(0.55,0.85,burn_mask);
    out_emissive+=float3(2.2,0.55,0.08)*ember;
#endif
    #undef SAMPLE_MAT
    Material output;
    output.albedo=color;
    output.specular_color=float4(specular_color,1);
    output.emission=float4(out_emissive,1);
    return output;
}
