using System;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace xray.editor.avalonia_controls.color_matrix_editor.shaders;

public sealed class BilinearColorDrawOperation : ICustomDrawOperation
{
    private const string SkslSource = @"
uniform float2 uSize;
uniform float4 uColor1;
uniform float4 uColor2;
uniform float4 uColor3;
uniform float4 uColor4;

half4 main(float2 fragCoord) {
    float2 uv = clamp(fragCoord / max(uSize, float2(1.0)), 0.0, 1.0);
    float4 top_color = mix(uColor1, uColor3, uv.x);
    float4 bottom_color = mix(uColor2, uColor4, uv.x);
    return half4(mix(top_color, bottom_color, uv.y));
}
";

    private static readonly object s_lock = new();
    private static SKRuntimeEffect? s_effect;
    private static string? s_compileError;
    private static bool s_compiled;

    public BilinearColorDrawOperation(Rect bounds, Vector4 color1, Vector4 color2, Vector4 color3, Vector4 color4)
    {
        Bounds = bounds;
        Color1 = color1;
        Color2 = color2;
        Color3 = color3;
        Color4 = color4;
    }

    public Rect Bounds { get; }
    public Vector4 Color1 { get; }
    public Vector4 Color2 { get; }
    public Vector4 Color3 { get; }
    public Vector4 Color4 { get; }

    public static bool CompileShader(out string? error)
    {
        lock (s_lock)
        {
            if (!s_compiled)
            {
                s_effect = SKRuntimeEffect.CreateShader(SkslSource, out s_compileError);
                s_compiled = true;
            }
            error = s_compileError;
            return s_effect != null;
        }
    }

    public void Dispose() { }

    public bool HitTest(Point p) => Bounds.Contains(p);

    public bool Equals(ICustomDrawOperation? other) =>
        other is BilinearColorDrawOperation op &&
        op.Bounds == Bounds &&
        op.Color1 == Color1 &&
        op.Color2 == Color2 &&
        op.Color3 == Color3 &&
        op.Color4 == Color4;

    public void Render(ImmediateDrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        if (!CompileShader(out _) || s_effect == null)
            return;

        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature == null)
            return;

        using var lease = leaseFeature.Lease();
        SKCanvas canvas = lease.SkCanvas;

        var uniforms = new SKRuntimeEffectUniforms(s_effect)
        {
            ["uSize"] = new[] { (float)Bounds.Width, (float)Bounds.Height },
            ["uColor1"] = new[] { Color1.X, Color1.Y, Color1.Z, Color1.W },
            ["uColor2"] = new[] { Color2.X, Color2.Y, Color2.Z, Color2.W },
            ["uColor3"] = new[] { Color3.X, Color3.Y, Color3.Z, Color3.W },
            ["uColor4"] = new[] { Color4.X, Color4.Y, Color4.Z, Color4.W }
        };

        using SKShader? shader = s_effect.ToShader(uniforms);
        if (shader == null)
            return;

        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = false
        };
        canvas.DrawRect(SKRect.Create(0, 0, (float)Bounds.Width, (float)Bounds.Height), paint);
    }
}
