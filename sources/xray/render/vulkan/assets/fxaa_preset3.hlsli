// FXAA preset 3, Timothy Lottes / NVIDIA (2011).
// Adapted from the engine's fxaa_common.h by Nikolay Partas, GSC Game World (2011).
// Copyright (C) GSC Game World - 2011.
// Algorithm: https://developer.download.nvidia.com/assets/gamedev/files/sdk/11/FXAA_WhitePaper.pdf
// The fixed HLSL preset is retained; sampling is clamped to this view's pixel centers.
struct FxaaTex { SamplerState smpl; Texture2D<float4> tex; };
float4 FxaaTexLod0(FxaaTex tex, float2 pos) {
        return tex.tex.SampleLevel(tex.smpl, clamp(pos.xy,source_bounds.xy,source_bounds.zw), 0.0);
}
float4 FxaaTexOff(FxaaTex tex, float2 pos, int2 off, float2 rcpFrame) {
        return FxaaTexLod0(tex,pos.xy+float2(off)*rcpFrame);
}
float FxaaLuma(float3 rgb) {
    return rgb.y * (0.587/0.299) + rgb.x; }
float3 FxaaLerp3(float3 a, float3 b, float amountOfA) {
    return (float3((-amountOfA), (-amountOfA), (-amountOfA)) * b) +
        ((a * float3((amountOfA), (amountOfA), (amountOfA))) + b); }
float3 FxaaFilterReturn(float3 rgb) {
        return rgb;
}
float3 FxaaPixelShader(
float2 pos,
FxaaTex tex,
float2 rcpFrame) {
    float3 rgbN = FxaaTexOff(tex, pos.xy, int2( 0,-1), rcpFrame).xyz;
    float3 rgbW = FxaaTexOff(tex, pos.xy, int2(-1, 0), rcpFrame).xyz;
    float3 rgbM = FxaaTexOff(tex, pos.xy, int2( 0, 0), rcpFrame).xyz;
    float3 rgbE = FxaaTexOff(tex, pos.xy, int2( 1, 0), rcpFrame).xyz;
    float3 rgbS = FxaaTexOff(tex, pos.xy, int2( 0, 1), rcpFrame).xyz;
    float lumaN = FxaaLuma(rgbN);
    float lumaW = FxaaLuma(rgbW);
    float lumaM = FxaaLuma(rgbM);
    float lumaE = FxaaLuma(rgbE);
    float lumaS = FxaaLuma(rgbS);
    float rangeMin = min(lumaM, min(min(lumaN, lumaW), min(lumaS, lumaE)));
    float rangeMax = max(lumaM, max(max(lumaN, lumaW), max(lumaS, lumaE)));
    float range = rangeMax - rangeMin;
    if(range < max((1.0/24.0), rangeMax * (1.0/8.0))) {
        return FxaaFilterReturn(rgbM); }
            float3 rgbL = rgbN + rgbW + rgbM + rgbE + rgbS;
        float lumaL = (lumaN + lumaW + lumaE + lumaS) * 0.25;
        float rangeL = abs(lumaL - lumaM);
        float blendL = max(0.0,
            (rangeL / range) - (1.0/4.0)) * (1.0/(1.0 - (1.0/4.0)));
        blendL = min((3.0/4.0), blendL);
    float3 rgbNW = FxaaTexOff(tex, pos.xy, int2(-1,-1), rcpFrame).xyz;
    float3 rgbNE = FxaaTexOff(tex, pos.xy, int2( 1,-1), rcpFrame).xyz;
    float3 rgbSW = FxaaTexOff(tex, pos.xy, int2(-1, 1), rcpFrame).xyz;
    float3 rgbSE = FxaaTexOff(tex, pos.xy, int2( 1, 1), rcpFrame).xyz;
        rgbL += (rgbNW + rgbNE + rgbSW + rgbSE);
        rgbL *= float3((1.0/9.0), (1.0/9.0), (1.0/9.0));
    float lumaNW = FxaaLuma(rgbNW);
    float lumaNE = FxaaLuma(rgbNE);
    float lumaSW = FxaaLuma(rgbSW);
    float lumaSE = FxaaLuma(rgbSE);
    float edgeVert =
        abs((0.25 * lumaNW) + (-0.5 * lumaN) + (0.25 * lumaNE)) +
        abs((0.50 * lumaW ) + (-1.0 * lumaM) + (0.50 * lumaE )) +
        abs((0.25 * lumaSW) + (-0.5 * lumaS) + (0.25 * lumaSE));
    float edgeHorz =
        abs((0.25 * lumaNW) + (-0.5 * lumaW) + (0.25 * lumaSW)) +
        abs((0.50 * lumaN ) + (-1.0 * lumaM) + (0.50 * lumaS )) +
        abs((0.25 * lumaNE) + (-0.5 * lumaE) + (0.25 * lumaSE));
    bool horzSpan = edgeHorz >= edgeVert;
    float lengthSign = horzSpan ? -rcpFrame.y : -rcpFrame.x;
    if(!horzSpan) lumaN = lumaW;
    if(!horzSpan) lumaS = lumaE;
    float gradientN = abs(lumaN - lumaM);
    float gradientS = abs(lumaS - lumaM);
    lumaN = (lumaN + lumaM) * 0.5;
    lumaS = (lumaS + lumaM) * 0.5;
    bool pairN = gradientN >= gradientS;
    if(!pairN) lumaN = lumaS;
    if(!pairN) gradientN = gradientS;
    if(!pairN) lengthSign *= -1.0;
    float2 posN;
    posN.x = pos.x + (horzSpan ? 0.0 : lengthSign * 0.5);
    posN.y = pos.y + (horzSpan ? lengthSign * 0.5 : 0.0);
    gradientN *= (1.0/4.0);
    float2 posP = posN;
    float2 offNP = horzSpan ?
        float2(rcpFrame.x, 0.0) :
        float2(0.0f, rcpFrame.y);
    float lumaEndN = lumaN;
    float lumaEndP = lumaN;
    bool doneN = false;
    bool doneP = false;
        posN += offNP * float2(-1.0, -1.0);
        posP += offNP * float2( 1.0, 1.0);
    for(int i = 0; i < 16; i++) {
            if(!doneN) lumaEndN =
                FxaaLuma(FxaaTexLod0(tex, posN.xy).xyz);
            if(!doneP) lumaEndP =
                FxaaLuma(FxaaTexLod0(tex, posP.xy).xyz);
        doneN = doneN || (abs(lumaEndN - lumaN) >= gradientN);
        doneP = doneP || (abs(lumaEndP - lumaN) >= gradientN);
        if(doneN && doneP) break;
        if(!doneN) posN -= offNP;
        if(!doneP) posP += offNP; }
    float dstN = horzSpan ? pos.x - posN.x : pos.y - posN.y;
    float dstP = horzSpan ? posP.x - pos.x : posP.y - pos.y;
    bool directionN = dstN < dstP;
    lumaEndN = directionN ? lumaEndN : lumaEndP;
    if(((lumaM - lumaN) < 0.0) == ((lumaEndN - lumaN) < 0.0))
        lengthSign = 0.0;
    float spanLength = (dstP + dstN);
    dstN = directionN ? dstN : dstP;
    float subPixelOffset = (0.5 + (dstN * (-1.0/spanLength))) * lengthSign;
    float3 rgbF = FxaaTexLod0(tex, float2(
        pos.x + (horzSpan ? 0.0 : subPixelOffset),
        pos.y + (horzSpan ? subPixelOffset : 0.0))).xyz;
        return FxaaFilterReturn(FxaaLerp3(rgbL, rgbF, blendL));
}
