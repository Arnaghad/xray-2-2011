[[vk::binding(0,2)]] cbuffer ScreenEffects {
    row_major float4x4 inverse_projection;
    float4 viewport;
    float4 source_bounds;
    float4 ao_parameters; // pixel radius, world range, angle bias, strength
    float4 filter_parameters; // output size XY, depth threshold, normal threshold
};
[[vk::binding(0,1)]] Texture2D<float4> input0;
[[vk::binding(1,1)]] SamplerState nearest_sampler;
[[vk::binding(2,1)]] Texture2D<float4> input1;
[[vk::binding(3,1)]] Texture2D<float4> input2;
struct PixelInput {float4 position:SV_Position;float2 uv:TEXCOORD0;};
float3 decode_normal(float4 value){float2 xy=value.xy*2-1;return normalize(float3(xy,sqrt(saturate(1-dot(xy,xy)))*(value.w>.5 ? -1 : 1)));}
float3 position_at(int2 pixel,float depth){
    float2 uv=((pixel+.5)/filter_parameters.xy-viewport.xy)/viewport.zw;
    float4 p=mul(inverse_projection,float4(uv*float2(2,-2)+float2(-1,1),depth,1));return p.xyz/p.w;
}
bool inside(int2 pixel){float2 uv=(pixel+.5)/filter_parameters.xy;return all(uv>=source_bounds.xy) && all(uv<=source_bounds.zw);}
float4 ssao_ps(PixelInput input):SV_Target0 {
    int2 pixel=int2(input.position.xy);float2 uv=(pixel+.5)/filter_parameters.xy;
    float depth=input0.SampleLevel(nearest_sampler,uv,0).r;if(depth>=1)return 1;
    float3 p=position_at(pixel,depth),n=decode_normal(input1.SampleLevel(nearest_sampler,uv,0));
    const int2 directions[8]={int2(1,0),int2(-1,0),int2(0,1),int2(0,-1),int2(1,1),int2(-1,1),int2(1,-1),int2(-1,-1)};
    float sum=0;
    [unroll]for(uint i=0;i<8;++i){int2 q=pixel+directions[i]*int(ao_parameters.x);if(!inside(q))continue;
        float z=input0.Load(int3(q,0)).r;if(z>=1)continue;float3 delta=position_at(q,z)-p;float distance=length(delta);
        if(distance>1e-6)sum+=max(dot(n,delta/distance)-ao_parameters.z,0)*saturate(1-distance/ao_parameters.y);
    }
    return float4(saturate(1-ao_parameters.w*sum/8).xxx,1);
}
float4 ssao_filter_ps(PixelInput input):SV_Target0 {
    int2 pixel=int2(input.position.xy);float2 uv=(pixel+.5)/filter_parameters.xy;
    float depth=input1.SampleLevel(nearest_sampler,uv,0).r;if(depth>=1)return 1;
    float3 p=position_at(pixel,depth),n=decode_normal(input2.SampleLevel(nearest_sampler,uv,0));float sum=0,count=0;
    [unroll]for(int y=-1;y<=1;++y)[unroll]for(int x=-1;x<=1;++x){int2 q=pixel+int2(x,y);if(!inside(q))continue;
        float z=input1.Load(int3(q,0)).r;if(z>=1)continue;float3 other=position_at(q,z);
        float3 normal=decode_normal(input2.Load(int3(q,0)));
        if(abs(other.z-p.z)<=filter_parameters.z && dot(normal,n)>=filter_parameters.w){sum+=input0.Load(int3(q,0)).r;++count;}
    }
    return float4((count ? sum/count : 1).xxx,1);
}
