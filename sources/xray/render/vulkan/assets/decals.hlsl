[[vk::binding(0,2)]] cbuffer DecalConstants {
    row_major float4x4 inverse_projection;
    row_major float4x4 view_to_decal;
    float4 viewport;
    float4 extents; // half XYZ, camera far
    float4 tint;
    float4 normal_parameters; // multiplier XYZ; receiver mask + blend mode
    float4 reflection_parameters;
    float4 bindless_indices;
};
#if USE_BINDLESS
[[vk::binding(0,3)]] Texture2D<float4> bindless_2d[1024] : register(t0, space3);
[[vk::binding(1,3)]] TextureCube<float4> bindless_cube[128] : register(t1, space3);
#endif
[[vk::binding(0,1)]] Texture2D<float4> original_normal;
[[vk::binding(1,1)]] SamplerState linear_clamp;
[[vk::binding(2,1)]] Texture2D<float4> original_albedo;
[[vk::binding(3,1)]] Texture2D<float4> decal_diffuse;
[[vk::binding(4,1)]] Texture2D<float4> decal_normal;
[[vk::binding(5,1)]] Texture2D<float4> packed_depth;
struct PixelInput {float4 position:SV_Position;float2 uv:TEXCOORD0;};
struct Output {float4 normal:SV_Target0;float4 albedo:SV_Target1;};
Output decal_ps(PixelInput input){
    int2 pixel=int2(input.position.xy);
    Output output;output.normal=original_normal.Load(int3(pixel,0));output.albedo=original_albedo.Load(int3(pixel,0));
    float4 packed=packed_depth.Load(int3(pixel,0));
    uint receiver=uint(round(packed.g*4)),mask=uint(normal_parameters.w)%4;
    if(receiver==0 || (receiver & mask)==0)return output;
    float2 clip_xy=input.uv*float2(2,-2)+float2(-1,1);
    float4 a=mul(inverse_projection,float4(clip_xy,0,1)),b=mul(inverse_projection,float4(clip_xy,1,1));
    float3 start=a.xyz/a.w,delta=b.xyz/b.w-start;
    float view_z=packed.r*packed.r*extents.w;
    float3 position=start+delta*((view_z-start.z)/delta.z);
    float3 local=mul(view_to_decal,float4(position,1)).xyz;
    if(any(abs(local)>extents.xyz))return output;
    float2 uv=local.xy/extents.xy*float2(.5,-.5)+.5;
    float4 color=decal_diffuse.SampleLevel(linear_clamp,uv,0)*tint;
    float2 xy=decal_normal.SampleLevel(linear_clamp,uv,0).ag*2-1;
    float3 mapped=float3(xy.x,-xy.y,sqrt(saturate(1-dot(xy,xy))))*normal_parameters.xyz;
    float2 original_xy=output.normal.xy*2-1;
    float3 original=normalize(float3(original_xy,sqrt(saturate(1-dot(original_xy,original_xy)))*(output.normal.w>.5 ? -1 : 1)));
    float3x3 decal_to_view=transpose((float3x3)view_to_decal);
    float3 base_normal=uint(normal_parameters.w)>=4 ? original : -mul(decal_to_view,float3(0,0,1));
    float3 tangent=mul(decal_to_view,float3(1,0,0));
    tangent-=base_normal*dot(tangent,base_normal);
    if(dot(tangent,tangent)<1e-10)tangent=cross(base_normal,abs(base_normal.z)<.9 ? float3(0,0,1) : float3(0,1,0));
    tangent=normalize(tangent);
    float3 binormal=-normalize(cross(tangent,base_normal));
    float3 normal=tangent*mapped.x+binormal*mapped.y+base_normal*mapped.z;
    if(dot(normal,normal)<1e-10)normal=base_normal;
    float3 blended=lerp(original,normalize(normal),color.a);
    if(color.a>0 && dot(blended,blended)>1e-10){normal=normalize(blended);output.normal.xy=normal.xy*.5+.5;output.normal.w=normal.z>0 ? 0 : 1;}
#if USE_BINDLESS
    uint cube_idx=asuint(bindless_indices.x);
    if(cube_idx>0 && reflection_parameters.z>0.5 && reflection_parameters.y>0.0){
        float3 r_v=reflect(normalize(position),normal);
        color.rgb+=bindless_cube[NonUniformResourceIndex(cube_idx)].SampleLevel(linear_clamp,r_v,0).rgb*reflection_parameters.y;
    }
#endif
    output.albedo.rgb=lerp(output.albedo.rgb,color.rgb,color.a);
    return output;
}
