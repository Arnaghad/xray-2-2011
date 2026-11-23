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
#if TERRAIN_MATERIAL
    float4 specular;
#endif
};
[[vk::binding(0, 1)]] Texture2DArray<float4> diffuse_layers : register(t0, space1);
[[vk::binding(1, 1)]] SamplerState layer_sampler : register(s0, space1);
[[vk::binding(2, 1)]] Texture2DArray<float4> normal_layers : register(t1, space1);
[[vk::binding(4, 1)]] Texture2DArray<float4> specular_layers : register(t3, space1);
struct VertexInput {
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    float3 weights : TEXCOORD1;
    float3 ids : TEXCOORD2;
    float3 color : TEXCOORD3;
    float2 shift : TEXCOORD4;
};
struct SurfaceVertexInput {
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    float3 weights : TEXCOORD1;
    float3 ids : TEXCOORD2;
    float3 color : TEXCOORD3;
    float2 shift : TEXCOORD4;
    float4 normal : NORMAL;
    float4 tangent : TANGENT;
    float4 binormal : BINORMAL;
};
struct MaterialInput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float3 weights : TEXCOORD1;
    float3 ids : TEXCOORD2;
    float3 color : TEXCOORD3;
};
struct SurfaceInput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float3 weights : TEXCOORD1;
    float3 ids : TEXCOORD2;
    float3 color : TEXCOORD3;
    float3 view_position : TEXCOORD4;
    float3 normal : TEXCOORD5;
    float3 tangent : TEXCOORD6;
    float3 binormal : TEXCOORD7;
};
MaterialInput terrain_material_vs(VertexInput input) {
    MaterialInput output;
    float4 p=mul(view,mul(world,float4(input.position,1)));
    output.position=mul(projection,p);
    output.uv=(input.uv+input.shift*float2(-1,1)/parameters.y)*8;
    output.weights=input.weights; output.ids=input.ids; output.color=input.color;
    return output;
}
SurfaceInput terrain_deferred_vs(SurfaceVertexInput input) {
    SurfaceInput output;
    // Identical position arithmetic in both passes is required by EQUAL depth.
    float4 p=mul(view,mul(world,float4(input.position,1)));
    output.position=mul(projection,p); output.view_position=p.xyz;
    output.uv=(input.uv+input.shift*float2(-1,1)/parameters.y)*8;
    output.weights=input.weights; output.ids=input.ids; output.color=input.color;
    float3x3 m=(float3x3)mul(view,world);
    float3x3 cofactors=float3x3(cross(m[1],m[2]),cross(m[2],m[0]),cross(m[0],m[1]));
    output.normal=normalize(mul(cofactors,input.normal.bgr*2-1)/dot(m[0],cofactors[0]));
    output.tangent=mul(m,input.tangent.bgr*2-1);
    output.binormal=mul(m,input.binormal.bgr*2-1);
    return output;
}
float3 layer_ids(float3 ids) { return clamp(floor(ids+.5),0,parameters.z-1); }
float3 mapped_normal(float2 uv,float id) {
    float2 xy=normal_layers.SampleLevel(layer_sampler,float3(uv,id),0).ag*2-1;
    return normalize(float3(xy.x,-xy.y,sqrt(saturate(1-dot(xy,xy)))));
}
struct GBuffer { float4 depth : SV_Target0; float4 normal : SV_Target1; float4 tangent : SV_Target2; };
GBuffer terrain_gbuffer_ps(SurfaceInput input) {
    float3 ids=layer_ids(input.ids);
    float3 mapped=mapped_normal(input.uv,ids.x)*input.weights.x
        +mapped_normal(input.uv,ids.y)*input.weights.y+mapped_normal(input.uv,ids.z)*input.weights.z
        +float3(0,0,1)*(1-dot(input.weights,1));
    // Legal authored weights may cancel; retain the neutral tangent-space direction.
    mapped=dot(mapped,mapped)>1e-12 ? normalize(mapped) : float3(0,0,1);
    float3 n=normalize(input.normal),t=input.tangent-n*dot(input.tangent,n);
    if (dot(t,t)<1e-12) t=cross(n,abs(n.z)<.9 ? float3(0,0,1) : float3(0,1,0));
    t=normalize(t);
    float3 b=normalize(cross(n,t)); if (dot(b,input.binormal)<0) b=-b;
    n=normalize(t*mapped.x+b*mapped.y+n*mapped.z);
    GBuffer output;
    output.depth=float4(sqrt(saturate(input.view_position.z/parameters.x)),.5,surface.xy);
    output.normal=float4(n.xy*.5+.5,surface.z,n.z>0 ? 0 : 1);
    float3 tangent_cross=cross(t,n);
    if (dot(tangent_cross,tangent_cross)<1e-12) tangent_cross=cross(n,abs(n.z)<.9 ? float3(0,0,1) : float3(0,1,0));
    output.tangent=float4(normalize(tangent_cross)*.5+.5,1);
    return output;
}
struct Material { float4 albedo : SV_Target0; float4 specular_color : SV_Target1; float4 emission : SV_Target2; };
Material terrain_material_ps(MaterialInput input) {
    float3 ids=layer_ids(input.ids);
    float3 a=diffuse_layers.SampleLevel(layer_sampler,float3(input.uv,ids.x),0).rgb;
    float3 b=diffuse_layers.SampleLevel(layer_sampler,float3(input.uv,ids.y),0).rgb;
    float3 c=diffuse_layers.SampleLevel(layer_sampler,float3(input.uv,ids.z),0).rgb;
    float s=specular_layers.SampleLevel(layer_sampler,float3(input.uv,ids.x),0).r*input.weights.x
        +specular_layers.SampleLevel(layer_sampler,float3(input.uv,ids.y),0).r*input.weights.y
        +specular_layers.SampleLevel(layer_sampler,float3(input.uv,ids.z),0).r*input.weights.z;
    Material output;
    output.albedo=float4((a*input.weights.x+b*input.weights.y+c*input.weights.z+(1-dot(input.weights,1))*.5)*input.color*2,1);
    output.specular_color=float4(s,s,s,1); output.emission=float4(0,0,0,1);
    return output;
}
