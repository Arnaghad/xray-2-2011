[[vk::binding(0, 2)]] cbuffer ObjectConstants : register(b0, space2) {
    row_major float4x4 world;
    float4 tint;
    float4 parameters;
    float4 surface;
    float4 map_parameters;
};
[[vk::binding(0, 1)]] Texture2D<float4> material_texture : register(t0, space1);
[[vk::binding(1, 1)]] SamplerState material_sampler : register(s0, space1);
[[vk::binding(2, 1)]] Texture2D<float4> auxiliary_texture : register(t1, space1);
struct PixelInput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
PixelInput preview_vs(uint id : SV_VertexID) {
    float2 p=float2((id<<1)&2,id&2);
    PixelInput output; output.position=float4(p*2-1,0,1); output.uv=p; return output;
}
float4 preview_ps(PixelInput input) : SV_Target0 {
    // Full-output targets are sampled through the queued normalized viewport.
    float2 uv=parameters.zw+input.uv*tint.xy;
    float4 data=material_texture.SampleLevel(material_sampler,uv,0);
    uint mode=(uint)parameters.x;
    if(mode==11)return float4(dot(data.rgb,float3(.2126,.7152,.0722)).xxx,1);
    if(mode==12){float roughness=max(.001,(data.z+data.w)*.5);float power=max(0,2/(roughness*roughness)-2);return data.x>=1?float4(0,0,0,1):float4(saturate(log2(1+power)/10).xxx,1);}
    if(mode==13)return float4(data.rgb,1);
    if(mode==14)return float4(data.rrr,1);
    if(mode==15)return float4(data.rgb*auxiliary_texture.SampleLevel(material_sampler,uv,0).r,1);
    if(mode==16)return data.a<.5?float4(0,0,0,1):float4(saturate(data.xy*512+.5),.5,1);
    if(mode==18) {
        float3 color=tint.w!=0?max(data.rgb,0)*tint.z/(1+max(data.rgb,0)*tint.z):data.rgb;
        if(input.uv.x<.35 && input.uv.y>.75) {
            uint bin=(uint)(input.uv.x/.35*32),count=0;
            for(uint y=0;y<16;++y)for(uint x=0;x<16;++x) {
                float3 value=material_texture.SampleLevel(material_sampler,parameters.zw+(float2(x,y)+.5)/16*tint.xy,0).rgb;
                float luma=dot(value,float3(.2126,.7152,.0722));uint sample_bin=(uint)clamp((log2(max(luma,1e-6))+12)/24*32,0,31);count+=sample_bin==bin?1:0;
            }
            float bar=min(1,count/32.f);color=(1-input.uv.y)/.25<bar?float3(.1,.8,.2):color*.2;
        }
        return float4(color,1);
    }
    if (mode==10 && tint.w!=0) {
        // Tone map the complete FP16 scene, before the presentation attachment's sRGB encoding.
        float3 exposed=max(data.rgb,0)*tint.z;
        return float4(exposed/(1+exposed),1);
    }
    if (mode>=8) return float4(data.rgb,1);
    if (mode==6) return data.x>=1 ? float4(0,0,0,1) : float4(data.zw,0,1);
    if (mode==7) return float4(data.zzz,1);
    if (mode==5) return float4(data.xxx,1);
    if (mode==0) return data.x>=1 ? float4(0,0,0,1) : float4(data.xxx,1);
    if (mode==1) {
        if (all(data.xy==0)) return float4(0,0,0,1);
        float2 xy=data.xy*2-1;
        float3 n=float3(xy,sqrt(saturate(1-dot(xy,xy)))*(data.w>0.5 ? -1 : 1));
        return float4(normalize(n)*.5+.5,1);
    }
    if (mode==2) return data.w==0 ? float4(0,0,0,1) : float4(data.xyz,1);
    if (mode==3) return data.x>=1 ? float4(0,0,0,1) : float4(data.xxx,1);
    if (data.x>=1) return float4(0,0,0,1);
    float4 p=mul(world,float4(input.uv.x*2-1,1-input.uv.y*2,data.x,1));
    return float4(saturate(p.xyz/p.w/parameters.y*.5+.5),1);
}
float4 stencil_ps() : SV_Target0 { return float4(1,1,1,1); }
