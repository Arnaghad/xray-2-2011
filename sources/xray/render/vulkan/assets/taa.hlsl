[[vk::binding(0,2)]] cbuffer TemporalConstants {
    row_major float4x4 inverse_view_projection;
    row_major float4x4 previous_view_projection;
    float4 viewport;
    float4 source_bounds;
    float4 filter;
    float4 temporal;
};
[[vk::binding(0,1)]] Texture2D<float4> scene_texture;
[[vk::binding(1,1)]] SamplerState history_sampler;
[[vk::binding(2,1)]] Texture2D<float4> history_texture;
[[vk::binding(3,1)]] Texture2D<float> history_depth;
#if SAMPLE_COUNT > 1
[[vk::binding(4,1)]] Texture2DMS<float,SAMPLE_COUNT> scene_depth;
#else
[[vk::binding(4,1)]] Texture2D<float> scene_depth;
#endif
struct PixelInput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
struct TemporalOutput { float4 color : SV_Target0; float depth : SV_Target1; };
TemporalOutput taa_ps(PixelInput input) {
    int2 location=int2(input.position.xy);
    float2 uv=input.position.xy*filter.xy;
    float depth=1;
#if SAMPLE_COUNT > 1
    [unroll] for (uint sample_index=0;sample_index<SAMPLE_COUNT;++sample_index) depth=min(depth,scene_depth.Load(location,sample_index));
#else
    depth=scene_depth.Load(int3(location,0));
#endif
    float3 current=scene_texture.Load(int3(location,0)).rgb;
    float3 low=current,high=current;
    [unroll] for (int y=-1;y<=1;++y) [unroll] for (int x=-1;x<=1;++x) {
        int2 p=int2(clamp((float2(location+int2(x,y))+.5)*filter.xy,source_bounds.xy,source_bounds.zw)/filter.xy);
        float3 neighbor=scene_texture.Load(int3(p,0)).rgb;
        low=min(low,neighbor); high=max(high,neighbor);
    }
    float2 local_uv=(uv-viewport.xy)/viewport.zw;
    float4 world=mul(inverse_view_projection,float4(local_uv*float2(2,-2)+float2(-1,1),depth,1));
    float4 previous=mul(previous_view_projection,world);
    float3 clip=previous.xyz/(abs(previous.w)>1e-6 ? previous.w : 1);
    float2 history_uv=viewport.xy+(clip.xy*float2(.5,-.5)+.5)*viewport.zw;
    bool valid=temporal.y>.5 && depth<.99999 && previous.w>0 && clip.z>=0 && clip.z<=1
        && all(history_uv>=source_bounds.xy) && all(history_uv<=source_bounds.zw);
    int2 previous_pixel=int2(clamp(history_uv,source_bounds.xy,source_bounds.zw)/filter.xy);
    float old_depth=history_depth.Load(int3(previous_pixel,0));
    valid=valid && abs(old_depth-clip.z)<temporal.z;
    float3 old_color=history_texture.SampleLevel(history_sampler,clamp(history_uv,source_bounds.xy,source_bounds.zw),0).rgb;
    TemporalOutput output;
    output.color=float4(clamp(lerp(current,clamp(old_color,low,high),valid ? temporal.x : 0),0,65504),1);
    output.depth=depth;
    return output;
}
