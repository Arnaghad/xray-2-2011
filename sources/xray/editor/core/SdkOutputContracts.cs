using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace xray.editor_base;

public enum SdkOutputKind : uint { Camera, Model, Texture, Material }

[StructLayout(LayoutKind.Sequential)]
public struct SdkOutputSettings
{
    public uint Version, Size;
    public SdkOutputKind Kind;
    public uint Flags;
    public ulong Id, Window;
    internal nint Asset;
    public Matrix4x4 Camera;
    public float Fov, Near, Far, Reserved;
    public static SdkOutputSettings Create(SdkOutputKind kind, nint window) => new()
    {
        Version = 1,
        Size = 120,
        Kind = kind,
        Flags = 1,
        Window = (ulong)window,
        Camera = Matrix4x4.CreateTranslation(0, 1, -5),
        Fov = 60,
        Near = .1f,
        Far = 1000
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct SdkOutputStatus
{
    public uint Version, Size, State, Pending;
    public ulong Id, Window;
    public uint Width, Height;
    public ulong Frames;
    public Vector3 Minimum, Maximum;
}

public interface ISdkOutputScene
{
    SdkOutputStatus OpenOutput(SdkOutputSettings settings, string? asset);
    SdkOutputStatus ConfigureOutput(SdkOutputSettings settings);
    SdkOutputStatus PollOutput(ulong id);
    SdkOutputStatus CloseOutput(ulong id);
}

public interface ISdkSkeletalOutputScene { void PoseOutput(ulong output, Matrix4x4[] matrices); }
