[[vk::binding(0, 2)]] cbuffer LightingConstants : register(b0, space2) {
    row_major float4x4 inverse_projection;
    float4 viewport;
    float4 light_direction;
    float4 light_color;
    float4 light_factors;
    float4 light_position_range;
    float4 spot_parameters;
    float4 ambient;
    float4 fog_color_distance;
    float4 fog_parameters;
#if USE_SCENE_SHADOW
    row_major float4x4 shadow_matrix0;
    row_major float4x4 shadow_matrix1;
    row_major float4x4 shadow_matrix2;
    row_major float4x4 shadow_matrix3;
    float4 shadow_splits;
    float4 shadow_parameters;
#endif
};
#if SAMPLE_COUNT > 1
#define SCENE_TEXTURE Texture2DMS<float4, SAMPLE_COUNT>
#define READ_SCENE(tex,uv) tex.Load(int2(input.position.xy),input.sample_index)
#else
#define SCENE_TEXTURE Texture2D<float4>
#define READ_SCENE(tex,uv) tex.SampleLevel(nearest_sampler,uv,0)
#endif
[[vk::binding(0, 1)]] SCENE_TEXTURE input0 : register(t0, space1);
[[vk::binding(1, 1)]] SamplerState nearest_sampler : register(s0, space1);
[[vk::binding(2, 1)]] SCENE_TEXTURE input1 : register(t1, space1);
[[vk::binding(3, 1)]] SCENE_TEXTURE input2 : register(t2, space1);
[[vk::binding(4, 1)]] SCENE_TEXTURE input3 : register(t3, space1);
#if USE_SSAO
[[vk::binding(5,1)]] Texture2D<float4> ambient_occlusion;
#endif
struct PixelInput { float4 position : SV_Position; float2 uv : TEXCOORD0;
#if SAMPLE_COUNT > 1
    uint sample_index : SV_SampleIndex;
#endif
};
struct Accumulators { float4 diffuse : SV_Target0; float4 specular : SV_Target1; };
#if USE_SCENE_SHADOW
[[vk::binding(7,1)]] Texture2D<float> shadow_atlas;
[[vk::binding(6,1)]] SamplerComparisonState shadow_sampler;
float atlas_visibility(float3 q,uint cascade) {
    float2 uv=q.xy*float2(.5,-.5)+.5;
    if (any(uv<0) || any(uv>1) || q.z<0 || q.z>1) return 1;
    float grid=shadow_parameters.y;
    float2 tile=float2(cascade%uint(grid),cascade/uint(grid))/grid;
    float2 low=tile+shadow_parameters.z*.5,high=tile+1/grid-shadow_parameters.z*.5;
    uv=clamp(tile+uv/grid,low,high);
    return shadow_atlas.SampleCmpLevelZero(shadow_sampler,uv,q.z-spot_parameters.w);
}
float cascade_visibility(float3 position,uint cascade) {
    float4 p=float4(position,1);
    float4 q=cascade==0 ? mul(shadow_matrix0,p) : cascade==1 ? mul(shadow_matrix1,p)
        : cascade==2 ? mul(shadow_matrix2,p) : mul(shadow_matrix3,p);
    return atlas_visibility(q.xyz/q.w,cascade);
}
float point_visibility(float3 position) {
    float3 d=mul(shadow_matrix0,float4(position,1)).xyz,a=abs(d),q;
    uint face;
    // Stable X/Y/Z tie ownership and the same face roll as the caster cameras.
    if (a.x>=a.y && a.x>=a.z) {
        face=d.x>=0 ? 0 : 1; q=d.x>=0 ? float3(-d.z,d.y,d.x) : float3(d.z,d.y,-d.x);
    } else if (a.y>=a.z) {
        face=d.y>=0 ? 2 : 3; q=d.y>=0 ? float3(d.x,-d.z,d.y) : float3(d.x,d.z,-d.y);
    } else {
        face=d.z>=0 ? 4 : 5; q=d.z>=0 ? d : float3(-d.x,d.y,-d.z);
    }
    if (!(uint(shadow_splits.z) & (1u<<face)) || q.z<=0) return 1;
    return atlas_visibility(float3(q.xy/q.z,shadow_splits.x+shadow_splits.y/q.z),face);
}
float shadow_visibility(float3 position) {
    if (light_direction.w>1.5) return lerp(cascade_visibility(position,0),1,shadow_parameters.w);
    if (light_direction.w>.5) return lerp(point_visibility(position),1,shadow_parameters.w);
    uint count=uint(shadow_parameters.x),cascade=0;
    while (cascade+1<count && position.z>shadow_splits[cascade]) ++cascade;
    float visibility=cascade_visibility(position,cascade);
    if (cascade+1<count) {
        float previous=cascade ? shadow_splits[cascade-1] : inverse_projection[2][3]/inverse_projection[3][3];
        float start=shadow_splits[cascade]-.1*(shadow_splits[cascade]-previous);
        float blend=saturate((position.z-start)/max(shadow_splits[cascade]-start,1e-6));
        if (blend>0) visibility=lerp(visibility,cascade_visibility(position,cascade+1),blend);
    }
    return lerp(visibility,1,shadow_parameters.w);
}
#endif
Accumulators lighting_ps(PixelInput input) {
    float2 uv=viewport.xy+input.uv*viewport.zw;
    float4 tangent=READ_SCENE(input2,uv);
    Accumulators output; output.diffuse=output.specular=0;
    if (tangent.a<.5) return output;
    float4 packed=READ_SCENE(input0,uv);
    float4 normal=READ_SCENE(input1,uv);
    float2 xy=normal.xy*2-1;
    float3 n=normalize(float3(xy,sqrt(saturate(1-dot(xy,xy)))*(normal.w>.5 ? -1 : 1)));
    float depth=READ_SCENE(input3,uv).r;
    float4 p=mul(inverse_projection,float4(input.uv*float2(2,-2)+float2(-1,1),depth,1));
    float3 position=p.xyz/p.w;
    bool perspective=dot(abs(inverse_projection[3].xyz),float3(1,1,1))>1e-8;
    float3 v=perspective && dot(position,position)>1e-12 ? normalize(-position) : float3(0,0,-1);
    float3 l=-light_direction.xyz;
    float attenuation=1;
    if (light_direction.w>.5) {
        float3 delta=light_position_range.xyz-position;
        float squared_distance=dot(delta,delta);
        float range=light_position_range.w;
        // A coincident light has no incident direction; outside range contributes zero.
        if (!isfinite(squared_distance) || squared_distance<1e-12 || squared_distance>=range*range) return output;
        l=delta*rsqrt(squared_distance);
        attenuation=pow(max(1-saturate(squared_distance/(range*range)),.001),light_factors.w);
        if (light_direction.w>1.5) {
            float cosine=dot(-l,light_direction.xyz);
            if (cosine<spot_parameters.x) {
                if (cosine<=spot_parameters.y) return output;
                attenuation*=pow(saturate((cosine-spot_parameters.y)/max(spot_parameters.x-spot_parameters.y,.0001)),spot_parameters.z);
            }
        }
    }
    float diffuse=saturate(dot(l,n)),specular=0;
    float translucency=saturate((packed.g-round(packed.g*4.0)*0.25)*10.0);
    if (translucency>1e-3) {
        float back=saturate(dot(-l,v))*0.6+saturate(-dot(l,n))*0.4;
        diffuse=max(diffuse,back*translucency*0.75);
    }
    uint sss_id=(normal.w<-1.5 || normal.w>2.5) ? 2u : (normal.w<-0.5 || normal.w>1.5) ? 1u : 0u;
    float3 sss_scatter=0;
    if (sss_id>0u) {
        float ndotl=dot(l,n);
        float wrap=saturate((ndotl+0.55)/1.55);
        float back_lit=saturate(dot(-l,v))*0.35;
        float terminator=saturate(1.0-abs(ndotl))*wrap;
        float3 tint_profile=sss_id==1u ? float3(0.92,0.32,0.18) : float3(0.35,0.65,0.28);
        sss_scatter=tint_profile*(terminator*0.55+back_lit)*(0.5+translucency*0.5);
    }
    float alpha_param=saturate((1.0-tangent.a)/0.45);
    float3 sum=l+v;
    if (diffuse>0 && dot(sum,sum)>1e-12) {
        float3 h=normalize(sum);
        float cosine_squared=dot(n,h)*dot(n,h);
        float vh=saturate(dot(v,h));
        if (cosine_squared>1e-8 && vh>1e-6) {
            float3 half_projection=h-n*dot(n,h);
            float cosine_phi_squared=.5;
            if (dot(half_projection,half_projection)>1e-12) {
                float cosine_phi=dot(normalize(half_projection),tangent.xyz*2-1);
                cosine_phi_squared=saturate(cosine_phi*cosine_phi);
            }
            float2 roughness=max(packed.zw,float2(.01,.01));
            float distribution=exp(-(max(1-cosine_squared,0)/cosine_squared)
                *(cosine_phi_squared/(roughness.x*roughness.x)+(1-cosine_phi_squared)/(roughness.y*roughness.y)));
            distribution/=3.14159267*roughness.x*roughness.y*cosine_squared*cosine_squared;
            float fresnel=normal.z+(1-normal.z)*pow(1-vh,5);
            float nl=max(saturate(dot(n,l)),1e-4), nv=max(saturate(dot(n,v)),1e-4);
            float geom_factor=alpha_param>1e-4 ? pow(max(nl*nv,1e-4),alpha_param*0.5) : 1.0;
            specular=clamp(diffuse*fresnel*distribution/(4*vh)*geom_factor,0,4);
        }
    }
    diffuse*=attenuation; specular*=attenuation; sss_scatter*=attenuation;
    // Preserve light_ps_helper.h's asymmetric gamma placement.
    float3 lrgb=pow(max(light_color.rgb,0),2.2)*light_color.a;
    output.diffuse=float4((pow(light_color.rgb*diffuse*light_factors.x,2.2)*light_color.a)+lrgb*sss_scatter*light_factors.x,0);
    output.specular=float4(pow(light_color.rgb,2.2)*specular*light_factors.y*light_color.a,0);
#if USE_SCENE_SHADOW
    float visibility=shadow_visibility(position);
    output.diffuse.rgb*=visibility; output.specular.rgb*=visibility;
#endif
    return output;
}
float4 combine_ps(PixelInput input) : SV_Target0 {
    float2 uv=viewport.xy+input.uv*viewport.zw;
    float3 color=READ_SCENE(input0,uv).rgb*READ_SCENE(input2,uv).rgb
        +READ_SCENE(input1,uv).rgb*READ_SCENE(input3,uv).rgb;
    if(fog_parameters.w>.5)color=READ_SCENE(input2,uv).rgb+READ_SCENE(input3,uv).rgb;
    return float4(clamp(color*light_factors.z,0,65504),1);
}
float4 environment_ps(PixelInput input) : SV_Target0 {
    float2 uv=viewport.xy+input.uv*viewport.zw;
    float4 emission=READ_SCENE(input1,uv);
    if (emission.a<.5) return float4(0,0,0,1);
    float ao=1;
#if USE_SSAO
    ao=ambient_occlusion.SampleLevel(nearest_sampler,uv,0).r;
#endif
    float3 color=READ_SCENE(input0,uv).rgb+emission.rgb
        +READ_SCENE(input2,uv).rgb*pow(ambient.rgb,2.2)*ao;
    if(fog_parameters.z>.5)return float4(READ_SCENE(input2,uv).rgb*pow(ambient.rgb,2.2)*ao,1);
    if (fog_parameters.y>.5) {
        float depth=READ_SCENE(input3,uv).r;
        float4 position=mul(inverse_projection,float4(input.uv*float2(2,-2)+float2(-1,1),depth,1));
        float view_z=position.z/position.w;
        float fog=1-exp(-max(view_z-fog_parameters.x,0)/fog_color_distance.w);
        color=lerp(color,fog_color_distance.rgb,saturate(fog));
    }
    return float4(clamp(color,0,65504),1);
}
