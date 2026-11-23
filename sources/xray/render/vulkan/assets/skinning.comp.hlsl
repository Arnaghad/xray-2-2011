struct SkeletalVertex {
    float px, py, pz;
    float nx, ny, nz;
    float tx, ty, tz;
    float bx, by, bz;
    float u, v;
    uint b0, b1, b2, b3;
    float w0, w1, w2, w3;
};

struct SkinnedVertex {
    float px, py, pz;
    uint normal;
    uint tangent;
    uint binormal;
    float u, v;
};

[[vk::binding(0, 0)]] StructuredBuffer<SkeletalVertex> source_vertices : register(t0);
[[vk::binding(1, 0)]] RWStructuredBuffer<SkinnedVertex> output_vertices : register(u1);
[[vk::binding(2, 0)]] cbuffer SkinningConstants : register(b2) {
    row_major float4x4 bones[128];
    uint4 counts; // x = vertex_count
};

uint pack_direction(float3 v, uint alpha) {
    float len_sq = dot(v, v);
    float3 n = len_sq > 1e-12 ? v * rsqrt(len_sq) : float3(0, 1, 0);
    float3 s = clamp(n * 0.5 + 0.5, 0.0, 1.0) * 255.0;
    uint r = uint(round(s.x)) & 255u;
    uint g = uint(round(s.y)) & 255u;
    uint b = uint(round(s.z)) & 255u;
    return (alpha << 24) | (r << 16) | (g << 8) | b;
}

[numthreads(64, 1, 1)]
void skin_cs(uint3 tid : SV_DispatchThreadID) {
    uint idx = tid.x;
    if (idx >= counts.x) return;

    SkeletalVertex src = source_vertices[idx];
    float3 pos = float3(src.px, src.py, src.pz);
    float3 norm = float3(src.nx, src.ny, src.nz);
    float3 tang = float3(src.tx, src.ty, src.tz);
    float3 bino = float3(src.bx, src.by, src.bz);
    uint4 bi = uint4(src.b0, src.b1, src.b2, src.b3);
    float4 bw = float4(src.w0, src.w1, src.w2, src.w3);

    float3 p = float3(0, 0, 0);
    float3 n = float3(0, 0, 0);
    float3 t = float3(0, 0, 0);
    float3 b = float3(0, 0, 0);

    [unroll] for (uint i = 0; i < 4; ++i) {
        float w = bw[i];
        if (w <= 0.0) continue;
        row_major float4x4 m = bones[min(bi[i], 127u)];
        float3x3 m3 = (float3x3)m;
        p += mul(m, float4(pos, 1.0)).xyz * w;
        n += mul(m3, norm) * w;
        t += mul(m3, tang) * w;
        b += mul(m3, bino) * w;
    }

    SkinnedVertex dst;
    dst.px = p.x;
    dst.py = p.y;
    dst.pz = p.z;
    dst.normal = pack_direction(n, 127u);
    dst.tangent = pack_direction(t, 0u);
    dst.binormal = pack_direction(b, 0u);
    dst.u = src.u;
    dst.v = src.v;
    output_vertices[idx] = dst;
}
