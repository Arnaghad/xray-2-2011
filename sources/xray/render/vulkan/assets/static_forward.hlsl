[[vk::binding(0,0)]] cbuffer FrameConstants : register(b0, space0) {
    row_major float4x4 view;
    row_major float4x4 projection;
};
[[vk::binding(0,2)]] cbuffer ObjectConstants : register(b0, space2) {
    row_major float4x4 world;
    float4 tint;
    float4 parameters;
    float4 forward_params;
    float4 light_direction;
    float4 light_color;
    float4 ambient_color;
    float4 projection_params;
    float4 volume_params;
    float4 viewport_params;
    float4 extra_forward;
    float4 reflection_parameters;
    float4 bindless_indices;
};
#if USE_BINDLESS
[[vk::binding(0,3)]] Texture2D<float4> bindless_2d[1024] : register(t0, space3);
[[vk::binding(1,3)]] TextureCube<float4> bindless_cube[128] : register(t1, space3);
#endif
[[vk::binding(0,1)]] Texture2D<float4> material_texture : register(t0, space1);
[[vk::binding(1,1)]] SamplerState material_sampler : register(s0, space1);
#if USE_TRANSPARENCY_TEXTURE
[[vk::binding(2,1)]] Texture2D<float4> transparency_texture : register(t1, space1);
#endif
#if USE_SOFT_DEPTH
#if SAMPLE_COUNT > 1
[[vk::binding(3,1)]] Texture2DMS<float, SAMPLE_COUNT> scene_depth : register(t2, space1);
#else
[[vk::binding(3,1)]] Texture2D<float> scene_depth : register(t2, space1);
#endif
#endif
struct VertexInput { float3 position : POSITION; float2 uv : TEXCOORD0; float4 normal : NORMAL; };
struct PixelInput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float3 view_position : TEXCOORD1;
    float3 view_normal : TEXCOORD2;
    float3 world_pos : TEXCOORD3;
    float3 world_normal : TEXCOORD4;
    float3 local_pos : TEXCOORD5;
    float3 world_view_dir : TEXCOORD6;
};
PixelInput forward_vs(VertexInput input) {
    PixelInput output;
    float4 world_p=mul(world,float4(input.position,1));
    float4 view_p=mul(view,world_p);
    output.position=mul(projection,view_p);
    output.uv=input.uv;
    output.view_position=view_p.xyz;
    float3 n=input.normal.bgr*2-1;
    float3x3 vm=(float3x3)mul(view,world);
    output.view_normal=normalize(mul(vm,n));
    output.world_pos=world_p.xyz;
    output.world_normal=normalize(mul((float3x3)world,n));
    output.local_pos=input.position;
    output.world_view_dir=mul(transpose((float3x3)view),-view_p.xyz);
    return output;
}
float4 forward_ps(PixelInput input) : SV_Target0 {
    float2 uv=input.uv*parameters.zw+parameters.xy;
    float4 color=float4(pow(tint.rgb,2.2),tint.a);
#if USE_TRIPLANAR
    float3 tw=pow(abs(normalize(input.world_normal)),4.0);
    tw/=max(tw.x+tw.y+tw.z,1e-5);
    float2 uv_x=input.world_pos.zy*parameters.zw+parameters.xy;
    float2 uv_y=input.world_pos.xz*parameters.zw+parameters.xy;
    float2 uv_z=input.world_pos.xy*parameters.zw+parameters.xy;
#if USE_DIFFUSE_TEXTURE
    color*=material_texture.SampleLevel(material_sampler,uv_x,0)*tw.x
        +material_texture.SampleLevel(material_sampler,uv_y,0)*tw.y
        +material_texture.SampleLevel(material_sampler,uv_z,0)*tw.z;
#endif
#else
#if USE_DIFFUSE_TEXTURE
#if USE_VOLUME_SPHERE
    float2 sphere_uv=(uv+volume_params.xy)*volume_params.w;
    color*=material_texture.SampleLevel(material_sampler,sphere_uv,0);
#else
    color*=material_texture.SampleLevel(material_sampler,uv,0);
#endif
#endif
#endif
#if USE_TRANSPARENCY_TEXTURE
    color.a*=transparency_texture.SampleLevel(material_sampler,uv,0).r;
#endif
#if USE_ALPHA_TEST
    clip(color.a-forward_params.w);
#endif
#if USE_COLOR_POWER
    color.rgb=pow(max(color.rgb,1e-5),forward_params.z);
#endif
    color.rgb*=extra_forward.w;
#if USE_FORWARD_LIGHTING
    float3 n=normalize(input.view_normal);
    float3 l=-normalize(light_direction.xyz);
    float wrap=saturate((dot(n,l)+0.5)/1.5);
    float3 lit=pow(ambient_color.rgb,2.2)+pow(max(light_color.rgb,0),2.2)*light_color.a*wrap;
    color.rgb*=lit;
#endif
#if USE_FIRE_FRESNEL
    float3 fn=normalize(input.view_normal);
    float3 fv=normalize(-input.view_position);
    float fresnel=pow(saturate(1.0-abs(dot(fn,fv))),2.0);
    color.a*=fresnel;
    color.rgb*=1.0+fresnel;
#endif
#if USE_VOLUME_CONE
    float3 cn=normalize(input.view_normal);
    float3 cv=normalize(-input.view_position);
    float cone_fade=pow(saturate(abs(dot(cn,cv))),max(1.0+forward_params.y*4.0,1.0));
    color.a*=cone_fade;
#endif
#if USE_VOLUME_SPHERE
    float3 local_norm=input.local_pos/max(extra_forward.xyz,float3(1e-4,1e-4,1e-4));
    float r=length(local_norm);
    float sphere_fade=pow(saturate(1.0-r),max(forward_params.y,0.25));
    float3 sn=normalize(input.view_normal);
    float3 sv=normalize(-input.view_position);
    sphere_fade*=pow(saturate(abs(dot(sn,sv))),1.5);
    color.a*=sphere_fade;
#endif
#if USE_SOFT_DEPTH
#if SAMPLE_COUNT > 1
    float raw_depth=scene_depth.Load(int2(input.position.xy),0);
#else
    float raw_depth=scene_depth.Load(int3(int2(input.position.xy),0));
#endif
    float denom=projection_params.x-raw_depth*projection_params.z;
    float scene_view_z=abs(denom)>1e-6 ? (raw_depth*projection_params.w-projection_params.y)/denom : ambient_color.w;
    float soft_factor=raw_depth>=0.99999 ? 1.0 : saturate((scene_view_z-input.view_position.z)/max(forward_params.x,1e-4));
    color.a*=soft_factor;
#endif
#if USE_BINDLESS
    uint cube_idx=asuint(bindless_indices.x);
    if (cube_idx>0 && reflection_parameters.z>0.5 && reflection_parameters.y>0.0) {
        float3 r_w=reflect(-normalize(input.world_view_dir),normalize(input.world_normal));
        float3 env_col=bindless_cube[NonUniformResourceIndex(cube_idx)].SampleLevel(material_sampler,r_w,0).rgb;
        color.rgb+=env_col*reflection_parameters.y;
    }
#endif
    return color;
}
