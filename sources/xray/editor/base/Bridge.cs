using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace xray.editor_base;

[StructLayout(LayoutKind.Sequential)]
public struct SurfaceDescriptor
{
    public uint Kind, Reserved;
    public ulong Handle, Display;

    public static SurfaceDescriptor From(NativeSurfaceDescriptor descriptor) => new()
    {
        Kind = (uint)descriptor.Kind,
        Reserved = 0,
        Handle = unchecked((ulong)(long)descriptor.Handle),
        Display = unchecked((ulong)(long)descriptor.Display)
    };

    public NativeSurfaceDescriptor ToManaged() =>
        new((NativeSurfaceKind)Kind, unchecked((nint)(long)Handle), unchecked((nint)(long)Display));
}

[StructLayout(LayoutKind.Sequential)]
public struct WindowControlRequest
{
    public uint Version, Size;
    public uint Operation, Target;
    public int Width, Height;
    public uint Arg0, Reserved;
    public SurfaceDescriptor Surface;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeCallbacks
{
    public uint Version, Size;
    public nint Context;
    public uint Flags, Reserved;
    public delegate* unmanaged[Cdecl]<nint, int> Tick;
    public delegate* unmanaged[Cdecl]<nint, uint, float*, int> Render;
    public delegate* unmanaged[Cdecl]<nint, int> Clear;
    public delegate* unmanaged[Cdecl]<nint, int> Closing;
    public delegate* unmanaged[Cdecl]<nint, int, int> RequestExit;
    public delegate* unmanaged[Cdecl]<nint, uint, int> Command;
    public delegate* unmanaged[Cdecl]<nint, SurfaceDescriptor*, uint, int> Preview;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WorldBridge
{
    public uint Version, Size;
    public nint Context;
    public delegate* unmanaged[Cdecl]<nint, int> Load, Run, Clear, Destroy;
    public delegate* unmanaged[Cdecl]<nint, SurfaceDescriptor*, int> MainSurface, ViewSurface;
    public delegate* unmanaged[Cdecl]<nint, int, int> EditorMode;
    public delegate* unmanaged[Cdecl]<nint, uint, float*, int> Render;
    public delegate* unmanaged[Cdecl]<nint, byte*, int> MessageOut;
    public delegate* unmanaged[Cdecl]<nint, uint, int> Command;
    public delegate* unmanaged[Cdecl]<nint, SurfaceDescriptor*, int> PreviewSurface;
    public delegate* unmanaged[Cdecl]<nint, int> PreviewClosed;
    public delegate* unmanaged[Cdecl]<nint, WindowControlRequest*, SurfaceDescriptor*, int> WindowControl;
}

public static unsafe partial class Bridge
{
    private const uint AbiVersion = 3;
    private static readonly Dictionary<nint, EditorWorld> Worlds = new();
    private static long nextContext;
    private static EditorWorld World(nint context)
    {
        lock (Worlds)
        {
            if (!Worlds.TryGetValue(context, out var world)) throw new InvalidOperationException("Closed editor context");
            world.CheckOwner();
            return world;
        }
    }
    private static int Guard(System.Action action)
    {
        try { action(); return 0; }
        catch (Exception error) { Console.Error.WriteLine("Editor bridge: " + error); return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "xray_editor_create_world", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CreateWorld(NativeCallbacks* native, WorldBridge* result)
    {
        if (native == null || result == null || native->Version != AbiVersion || native->Size != sizeof(NativeCallbacks)
            || result->Version != AbiVersion || result->Size != sizeof(WorldBridge) || native->Reserved != 0
            || (native->Flags & ~1u) != 0 || native->Context == 0 || native->Tick == null || native->Render == null
            || native->Clear == null || native->Closing == null || native->RequestExit == null || native->Command == null || native->Preview == null) return -1;
        return Guard(() =>
        {
            var world = new EditorWorld(*native);
            try {
            nint context = checked((nint)System.Threading.Interlocked.Increment(ref nextContext));
            lock (Worlds) Worlds.Add(context, world);
            *result = new WorldBridge {
                Version = AbiVersion, Size = (uint)sizeof(WorldBridge), Context = context,
                Load = &Load, Run = &Run, Clear = &Clear, Destroy = &Destroy,
                MainSurface = &MainSurface, ViewSurface = &ViewSurface,
                EditorMode = &EditorMode, Render = &Render, MessageOut = &MessageOut, Command = &Command,
                PreviewSurface = &PreviewSurface, PreviewClosed = &PreviewClosed,
                WindowControl = &WindowControl
            };
            } catch { world.Dispose(); throw; }
        });
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Load(nint context) => Guard(() => World(context).Load());
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Run(nint context) => Guard(() => World(context).Run());
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Clear(nint context) => Guard(() => World(context).Clear());
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Destroy(nint context) => Guard(() => {
        World(context).Dispose(); lock (Worlds) Worlds.Remove(context);
    });
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MainSurface(nint context, SurfaceDescriptor* surface) => Guard(() =>
    {
        if (surface == null) throw new ArgumentNullException(nameof(surface));
        *surface = SurfaceDescriptor.From(World(context).MainSurface);
    });
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ViewSurface(nint context, SurfaceDescriptor* surface) => Guard(() =>
    {
        if (surface == null) throw new ArgumentNullException(nameof(surface));
        *surface = SurfaceDescriptor.From(World(context).ViewSurface);
    });
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int EditorMode(nint context, int enabled) => Guard(() => World(context).SetEditorMode(enabled != 0));
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Render(nint context, uint frame, float* matrix) => Guard(() => World(context).Render(frame, matrix));
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MessageOut(nint context, byte* message) => Guard(() =>
        World(context).Message(Marshal.PtrToStringUTF8((nint)message) ?? string.Empty));
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Command(nint context, uint command) => Guard(() => World(context).Command(command));
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int PreviewSurface(nint context, SurfaceDescriptor* surface) => Guard(() =>
    {
        if (surface == null) throw new ArgumentNullException(nameof(surface));
        *surface = SurfaceDescriptor.From(World(context).PreviewSurface);
    });
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int PreviewClosed(nint context) => Guard(() => World(context).PreviewClosed());
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int WindowControl(nint context, WindowControlRequest* request, SurfaceDescriptor* surface) =>
        Guard(() => World(context).HandleWindowControl(request, surface));
}
